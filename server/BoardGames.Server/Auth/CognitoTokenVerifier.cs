using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BoardGames.Server.Auth;

/// <summary>ログインしている人の ID トークンを確かめる。正しければユーザー ID（sub）、だめなら null。</summary>
public interface ITokenVerifier
{
    Task<string?> VerifyAsync(string idToken);
}

/// <summary>
/// Cognito の ID トークン（JWT、RS256）を確かめる（ステップ 5-2）。外部のライブラリは使わず、.NET の RSA で確かめる。
///   - 署名: ユーザープールの公開鍵（JWKS）。鍵は Lambda の実行環境が使い回される間は覚えておく。
///     知らない鍵（kid）のトークンが来たら読み直す（鍵の入れ替えに備える。読み直しは 5 分に 1 回まで）
///   - 中身: 発行元（iss）がこのユーザープール、宛先（aud）がサイトのクライアント、token_use が id、期限（exp）内
/// </summary>
public sealed class CognitoTokenVerifier : ITokenVerifier
{
    static readonly TimeSpan RefetchInterval = TimeSpan.FromMinutes(5);

    readonly string _issuer;
    readonly string _clientId;
    readonly Func<Task<string>> _fetchJwks;
    readonly Func<DateTimeOffset> _clock;
    readonly SemaphoreSlim _lock = new(1, 1);
    Dictionary<string, RSAParameters> _keys = new();
    DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    /// <param name="issuer">https://cognito-idp.{リージョン}.amazonaws.com/{ユーザープール ID}</param>
    /// <param name="fetchJwks">公開鍵の一覧（JSON）を読む。本番は {issuer}/.well-known/jwks.json を HTTP で読む</param>
    public CognitoTokenVerifier(string issuer, string clientId, Func<Task<string>> fetchJwks, Func<DateTimeOffset>? clock = null)
    {
        _issuer = issuer;
        _clientId = clientId;
        _fetchJwks = fetchJwks;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static CognitoTokenVerifier ForUserPool(string region, string userPoolId, string clientId, HttpClient http)
    {
        var issuer = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";
        return new CognitoTokenVerifier(issuer, clientId, () => http.GetStringAsync(issuer + "/.well-known/jwks.json"));
    }

    public async Task<string?> VerifyAsync(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length != 3) return null;

            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            if (Str(header.RootElement, "alg") != "RS256" || Str(header.RootElement, "kid") is not { } kid) return null;

            var key = await FindKeyAsync(kid);
            if (key == null) return null;
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(key.Value);
                var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
                if (!rsa.VerifyData(signed, Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return null;
            }

            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var claims = payload.RootElement;
            if (Str(claims, "iss") != _issuer || Str(claims, "aud") != _clientId || Str(claims, "token_use") != "id") return null;
            if (!claims.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number ||
                exp.GetInt64() <= _clock().ToUnixTimeSeconds()) return null;
            var sub = Str(claims, "sub");
            return string.IsNullOrEmpty(sub) || sub.Length > 64 ? null : sub;
        }
        catch (Exception e) when (e is FormatException or JsonException or CryptographicException or ArgumentException or InvalidOperationException)
        {
            return null; // 形がおかしいトークン
        }
    }

    async Task<RSAParameters?> FindKeyAsync(string kid)
    {
        if (_keys.TryGetValue(kid, out var key)) return key;
        await _lock.WaitAsync();
        try
        {
            if (_keys.TryGetValue(kid, out key)) return key;
            if (_clock() - _fetchedAt < RefetchInterval) return null; // 読み直したばかり: 知らない鍵として断る
            _fetchedAt = _clock();
            _keys = ParseJwks(await _fetchJwks());
            return _keys.TryGetValue(kid, out key) ? key : null;
        }
        catch (HttpRequestException)
        {
            return null; // 鍵を読めなかった: 今回はログインしていない扱い（次の読み直しは 5 分後）
        }
        finally
        {
            _lock.Release();
        }
    }

    static Dictionary<string, RSAParameters> ParseJwks(string json)
    {
        var keys = new Dictionary<string, RSAParameters>();
        using var doc = JsonDocument.Parse(json);
        foreach (var k in doc.RootElement.GetProperty("keys").EnumerateArray())
        {
            if (Str(k, "kty") != "RSA" || Str(k, "kid") is not { } kid || Str(k, "n") is not { } n || Str(k, "e") is not { } e) continue;
            keys[kid] = new RSAParameters { Modulus = Base64UrlDecode(n), Exponent = Base64UrlDecode(e) };
        }
        return keys;
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static byte[] Base64UrlDecode(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b += (b.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException("bad base64url") };
        return Convert.FromBase64String(b);
    }
}

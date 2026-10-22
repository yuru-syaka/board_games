using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardGames.Server.Auth;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>Cognito の ID トークンの確認（ステップ 5-2）。テストの中で作った鍵で署名したトークンを使う。</summary>
public class TokenVerifierTests
{
    const string Issuer = "https://cognito-idp.ap-northeast-1.amazonaws.com/ap-northeast-1_TEST";
    const string ClientId = "testclient";

    RSA _key = null!;
    DateTimeOffset _now;
    int _fetches;
    CognitoTokenVerifier _verifier = null!;

    [SetUp]
    public void SetUp()
    {
        _key = RSA.Create(2048);
        _now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        _fetches = 0;
        _verifier = new CognitoTokenVerifier(Issuer, ClientId, () =>
        {
            _fetches++;
            return Task.FromResult(Jwks("key1", _key));
        }, () => _now);
    }

    [TearDown]
    public void TearDown() => _key.Dispose();

    static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string B64(string json) => B64(Encoding.UTF8.GetBytes(json));

    static string Jwks(string kid, RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "RSA", kid, alg = "RS256", use = "sig", n = B64(p.Modulus!), e = B64(p.Exponent!) } },
        });
    }

    string Token(RSA? signer = null, string kid = "key1", string iss = Issuer, string aud = ClientId, string use = "id",
        long? exp = null, string sub = "user-123")
    {
        string header = B64(JsonSerializer.Serialize(new { alg = "RS256", kid }));
        string payload = B64(JsonSerializer.Serialize(new
        {
            sub, iss, aud, token_use = use, exp = exp ?? _now.AddHours(1).ToUnixTimeSeconds(), email = "a@example.com",
        }));
        var sig = (signer ?? _key).SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + payload + "." + B64(sig);
    }

    [Test]
    public async Task ValidToken_ReturnsTheUserId()
    {
        Assert.AreEqual("user-123", await _verifier.VerifyAsync(Token()));
        Assert.AreEqual("user-123", await _verifier.VerifyAsync(Token()));
        Assert.AreEqual(1, _fetches, "鍵は覚えておき、毎回は読まない");
    }

    [Test]
    public async Task Expired_IsRejected() =>
        Assert.IsNull(await _verifier.VerifyAsync(Token(exp: _now.AddSeconds(-1).ToUnixTimeSeconds())));

    [Test]
    public async Task OtherClient_IsRejected() => Assert.IsNull(await _verifier.VerifyAsync(Token(aud: "otherclient")));

    [Test]
    public async Task OtherIssuer_IsRejected() =>
        Assert.IsNull(await _verifier.VerifyAsync(Token(iss: "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_EVIL")));

    [Test]
    public async Task AccessToken_IsRejected() => Assert.IsNull(await _verifier.VerifyAsync(Token(use: "access")));

    [Test]
    public async Task WrongSignature_IsRejected()
    {
        using var other = RSA.Create(2048);
        Assert.IsNull(await _verifier.VerifyAsync(Token(signer: other)));
    }

    [Test]
    public async Task TamperedPayload_IsRejected()
    {
        var parts = Token().Split('.');
        string forged = B64(JsonSerializer.Serialize(new
        {
            sub = "someone-else", iss = Issuer, aud = ClientId, token_use = "id", exp = _now.AddHours(1).ToUnixTimeSeconds(),
        }));
        Assert.IsNull(await _verifier.VerifyAsync(parts[0] + "." + forged + "." + parts[2]));
    }

    [Test]
    public async Task UnknownKey_RefetchesAtMostEveryFiveMinutes()
    {
        Assert.IsNull(await _verifier.VerifyAsync(Token(kid: "rotated")));
        Assert.IsNull(await _verifier.VerifyAsync(Token(kid: "rotated")));
        Assert.AreEqual(1, _fetches);
        _now = _now.AddMinutes(6);
        Assert.IsNull(await _verifier.VerifyAsync(Token(kid: "rotated")));
        Assert.AreEqual(2, _fetches);
    }

    [TestCase("")]
    [TestCase("a.b")]
    [TestCase("not.a.jwt")]
    [TestCase("eyJhbGciOiJub25lIn0.e30.")] // alg: none
    public async Task Garbage_IsRejected(string token) => Assert.IsNull(await _verifier.VerifyAsync(token));
}

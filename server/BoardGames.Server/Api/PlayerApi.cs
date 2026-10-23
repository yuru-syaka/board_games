using System.Text.Json;
using BoardGames.Server.Auth;

namespace BoardGames.Server.Api;

/// <summary>ランキングの 1 行。</summary>
public sealed record RankingEntry(string UserId, string? Name, int Rating, int RatedGames, int Wins, int Losses, int Draws);

/// <summary>ランキング・プロフィール・マイページのための、プレイヤーのテーブルの読み書き（本番は DynamoDB、テストはメモリ）。</summary>
public interface IPlayerStore
{
    /// <summary>レートの高い順に、レート戦を minRatedGames 局以上遊んだ人を最大 size 人（名前つき）。</summary>
    Task<IReadOnlyList<RankingEntry>> GetRankingAsync(string game, int size, int minRatedGames);

    Task<PlayerProfile> GetPlayerAsync(string userId);

    Task SetNameAsync(string userId, string name);

    /// <summary>その人の戦績・名前・結果へのつながりを消し、対局結果の行からその人の ID を取り除く。</summary>
    Task DeleteAccountDataAsync(string userId);
}

/// <summary>ログインのアカウント（本番は Cognito）。</summary>
public interface IAccountDirectory
{
    Task DeleteUserAsync(string username);
}

public sealed record ApiRequest(string Method, string Path, string? Authorization, string? Body);

/// <summary>API の返事。CacheControl は CloudFront とブラウザに「どれだけ覚えてよいか」を伝える。</summary>
public sealed record ApiResponse(int Status, string Body, string CacheControl);

/// <summary>API の設定。変えるときはここだけを直す。</summary>
public sealed class ApiSettings
{
    /// <summary>ランキングに載せる人数。</summary>
    public int RankingSize { get; init; } = 100;
    /// <summary>ランキングに載せる条件（レート戦を何局以上遊んだか）。</summary>
    public int RankingMinRatedGames { get; init; } = 1;
    /// <summary>ランキング・プロフィールを CloudFront とブラウザが覚えてよい秒数。</summary>
    public int PublicCacheSeconds { get; init; } = 60;

    public static ApiSettings Default { get; } = new();
}

/// <summary>
/// ランキング・プロフィール・マイページの API（ステップ 5-4）。サイトの https://boardgamepia.com/api/… から呼ぶ。
///   GET    /api/ranking/{game}   レートの上位（だれでも。60 秒キャッシュ）
///   GET    /api/players/{id}     プロフィール（だれでも。60 秒キャッシュ）。いなければ {"player":null}
///   GET    /api/me               自分（ログインが要る。Authorization: Bearer ID トークン）
///   PUT    /api/me/name          表示名を決める・変える（本文 {"name":"…"}）
///   DELETE /api/me               アカウントを削除する（戦績・名前を消し、結果の行から ID を取り除き、Cognito のユーザーも消す）
///   POST   /api/csp-report       ブラウザからの CSP の違反の報告（ステップ 6-3）。ログに書くだけで、いつも 204
/// 状態コードに 403・404 は使わない（CloudFront がサイトの 404 ページに差し替えてしまうため）。
/// ログインが要るのにトークンが無い・確かめられないときは 401。入力がおかしいときは 400 と日本語の error。
/// </summary>
public sealed class PlayerApi
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    const string NoStore = "no-store";

    readonly IPlayerStore _store;
    readonly ITokenVerifier? _verifier;
    readonly IAccountDirectory _accounts;
    readonly HashSet<string> _games;
    readonly ApiSettings _settings;
    readonly int _initialRating;
    readonly Action<string> _log;

    /// <param name="log">ログに 1 行書く（CSP の違反の報告など）。本番は CloudWatch Logs</param>
    public PlayerApi(IPlayerStore store, ITokenVerifier? verifier, IAccountDirectory accounts, IEnumerable<string> games,
        ApiSettings? settings = null, int initialRating = 1500, Action<string>? log = null)
    {
        _store = store;
        _verifier = verifier;
        _accounts = accounts;
        _games = new HashSet<string>(games);
        _settings = settings ?? ApiSettings.Default;
        _initialRating = initialRating;
        _log = log ?? (_ => { });
    }

    string PublicCache => $"public, max-age={_settings.PublicCacheSeconds}";

    public async Task<ApiResponse> HandleAsync(ApiRequest request)
    {
        var parts = request.Path.Trim('/').Split('/');
        if (parts.Length < 2 || parts[0] != "api") return Error(400, "not_found", "その API はありません。");
        var method = request.Method.ToUpperInvariant();

        switch (parts[1])
        {
            case "ranking" when parts.Length == 3 && method == "GET":
                return await RankingAsync(parts[2]);
            case "players" when parts.Length == 3 && method == "GET":
                return await PlayerAsync(parts[2]);
            case "csp-report" when parts.Length == 2 && method == "POST":
                foreach (var line in CspReports.Parse(request.Body)) _log(line);
                return new ApiResponse(204, "", NoStore);
            case "me":
            {
                var user = await AuthenticateAsync(request.Authorization);
                if (user == null) return Error(401, "unauthorized", "ログインし直してください。");
                if (parts.Length == 2 && method == "GET") return await MeAsync(user.Sub);
                if (parts.Length == 3 && parts[2] == "name" && method == "PUT") return await SetNameAsync(user.Sub, request.Body);
                if (parts.Length == 2 && method == "DELETE") return await DeleteAsync(user);
                break;
            }
        }
        return Error(400, "not_found", "その API はありません。");
    }

    // ---------- だれでも ----------

    async Task<ApiResponse> RankingAsync(string game)
    {
        if (!_games.Contains(game)) return Error(400, "unknown_game", "そのゲームはありません。");
        var entries = await _store.GetRankingAsync(game, _settings.RankingSize, _settings.RankingMinRatedGames);
        // 同じレートは同じ順位（1, 2, 2, 4 …）
        var players = new List<RankRow>();
        int rank = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (i == 0 || entries[i - 1].Rating != e.Rating) rank = i + 1;
            players.Add(new RankRow(rank, e.UserId, e.Name, e.Rating, e.RatedGames, e.Wins, e.Losses, e.Draws));
        }
        return Ok(new { game, minRatedGames = _settings.RankingMinRatedGames, players }, PublicCache);
    }

    sealed record RankRow(int rank, string id, string? name, int rating, int ratedGames, int wins, int losses, int draws);

    async Task<ApiResponse> PlayerAsync(string id)
    {
        if (!IsUserId(id)) return Error(400, "bad_id", "プレイヤーの ID が正しくありません。");
        var profile = await _store.GetPlayerAsync(id);
        if (profile.Name == null && profile.Stats.Count == 0) return Ok(new { player = (object?)null }, PublicCache);
        return Ok(new { player = ProfileJson(profile) }, PublicCache);
    }

    // ---------- 自分 ----------

    async Task<VerifiedUser?> AuthenticateAsync(string? authorization)
    {
        const string bearer = "Bearer ";
        if (_verifier == null || authorization == null || !authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)) return null;
        var token = authorization.Substring(bearer.Length).Trim();
        if (token.Length == 0 || token.Length > 4096) return null;
        return await _verifier.VerifyUserAsync(token);
    }

    async Task<ApiResponse> MeAsync(string userId)
    {
        var profile = await _store.GetPlayerAsync(userId);
        return Ok(new { player = ProfileJson(profile) }, NoStore);
    }

    async Task<ApiResponse> SetNameAsync(string userId, string? body)
    {
        string? input = null;
        try
        {
            using var doc = JsonDocument.Parse(body ?? "");
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("name", out var n) &&
                n.ValueKind == JsonValueKind.String)
                input = n.GetString();
        }
        catch (JsonException)
        {
            // 下で「名前を入力してください」
        }
        if (!NameRules.TryNormalize(input, out var name, out var error)) return Error(400, "bad_name", error);
        await _store.SetNameAsync(userId, name);
        return Ok(new { name }, NoStore);
    }

    async Task<ApiResponse> DeleteAsync(VerifiedUser user)
    {
        // 先に戦績などを消し、最後にログインのアカウントを消す（途中で失敗しても、もう一度削除し直せるように）
        await _store.DeleteAccountDataAsync(user.Sub);
        await _accounts.DeleteUserAsync(user.Username);
        return Ok(new { deleted = true }, NoStore);
    }

    // ---------- 内部 ----------

    object ProfileJson(PlayerProfile profile) => new
    {
        id = profile.UserId,
        name = profile.Name,
        games = profile.Stats.Values
            .Where(s => _games.Contains(s.Game))
            .OrderBy(s => s.Game, StringComparer.Ordinal)
            .Select(s => new
            {
                game = s.Game,
                rating = s.Rating,
                ratedGames = s.RatedGames,
                games = s.Games,
                wins = s.Wins,
                losses = s.Losses,
                draws = s.Draws,
            }),
        initialRating = _initialRating,
    };

    /// <summary>ユーザー ID（Cognito の sub。英数字とハイフン、64 文字まで）か。</summary>
    public static bool IsUserId(string s) =>
        s.Length is > 0 and <= 64 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '-');

    static ApiResponse Ok(object body, string cache) => new(200, JsonSerializer.Serialize(body, Json), cache);

    static ApiResponse Error(int status, string code, string message) =>
        new(status, JsonSerializer.Serialize(new { error = code, message }, Json), NoStore);
}

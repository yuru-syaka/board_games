using System.Text.Json;
using BoardGames.Server.Api;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>プレイヤーのテーブルの代わり（API のテスト用）。</summary>
sealed class InMemoryPlayerStore : IPlayerStore
{
    public Dictionary<string, string> Names { get; } = new();
    public List<PlayerStats> Stats { get; } = new();
    public List<string> Deleted { get; } = new();

    public Task<IReadOnlyList<RankingEntry>> GetRankingAsync(string game, int size, int minRatedGames) =>
        Task.FromResult<IReadOnlyList<RankingEntry>>(Stats
            .Where(s => s.Game == game && s.RatedGames >= minRatedGames)
            .OrderByDescending(s => s.Rating)
            .Take(size)
            .Select(s => new RankingEntry(s.UserId, Names.GetValueOrDefault(s.UserId), s.Rating, s.RatedGames, s.Wins, s.Losses, s.Draws))
            .ToList());

    public Task<PlayerProfile> GetPlayerAsync(string userId) =>
        Task.FromResult(new PlayerProfile(userId, Names.GetValueOrDefault(userId),
            Stats.Where(s => s.UserId == userId).ToDictionary(s => s.Game)));

    public Task SetNameAsync(string userId, string name)
    {
        Names[userId] = name;
        return Task.CompletedTask;
    }

    public Task DeleteAccountDataAsync(string userId)
    {
        Deleted.Add(userId);
        Names.Remove(userId);
        Stats.RemoveAll(s => s.UserId == userId);
        return Task.CompletedTask;
    }
}

sealed class FakeAccounts : IAccountDirectory
{
    public List<string> Deleted { get; } = new();

    public Task DeleteUserAsync(string username)
    {
        Deleted.Add(username);
        return Task.CompletedTask;
    }
}

/// <summary>ランキング・プロフィール・マイページの API（ステップ 5-4）。</summary>
public class ApiTests
{
    InMemoryPlayerStore _store = null!;
    FakeAccounts _accounts = null!;
    PlayerApi _api = null!;

    static readonly string[] Games = { "vanish", "gobblet", "quarto", "geister", "battleline", "centuryghost" };

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryPlayerStore();
        _accounts = new FakeAccounts();
        _api = new PlayerApi(_store, new FakeVerifier(), _accounts, Games);
    }

    static PlayerStats Stats(string user, string game, int rating, int rated, int wins = 0, int losses = 0) =>
        new(user, game, rating, rated, wins, losses, 0, rated, 1);

    async Task<(int Status, JsonElement Json, string Cache)> Call(string method, string path, string? user = null, string? body = null)
    {
        var auth = user == null ? null : "Bearer " + FakeVerifier.TokenFor(user);
        var response = await _api.HandleAsync(new ApiRequest(method, path, auth, body));
        return (response.Status, JsonDocument.Parse(response.Body).RootElement.Clone(), response.CacheControl);
    }

    // ---------- ランキング ----------

    [Test]
    public async Task Ranking_IsOrderedByRatingWithSharedRanksAndNames()
    {
        _store.Stats.AddRange(new[]
        {
            Stats("alice", "vanish", 1532, 3, wins: 2, losses: 1),
            Stats("bob", "vanish", 1500, 2),
            Stats("carol", "vanish", 1532, 1),
            Stats("dave", "vanish", 1600, 0), // レート戦なし（ルームコードだけ）: 載らない
            Stats("erin", "gobblet", 1700, 5), // 別のゲーム
        });
        _store.Names["alice"] = "アリス";

        var (status, json, cache) = await Call("GET", "/api/ranking/vanish");
        Assert.AreEqual(200, status);
        Assert.AreEqual("public, max-age=60", cache);
        var players = json.GetProperty("players").EnumerateArray().ToList();
        CollectionAssert.AreEqual(new[] { "alice", "carol", "bob" }, players.Select(p => p.GetProperty("id").GetString()));
        CollectionAssert.AreEqual(new[] { 1, 1, 3 }, players.Select(p => p.GetProperty("rank").GetInt32()));
        Assert.AreEqual("アリス", players[0].GetProperty("name").GetString());
        Assert.AreEqual(JsonValueKind.Null, players[1].GetProperty("name").ValueKind);
        Assert.AreEqual(2, players[0].GetProperty("wins").GetInt32());
    }

    [Test]
    public async Task Ranking_UnknownGame_Is400()
    {
        var (status, json, _) = await Call("GET", "/api/ranking/chess");
        Assert.AreEqual(400, status);
        Assert.AreEqual("unknown_game", json.GetProperty("error").GetString());
    }

    // ---------- プロフィール ----------

    [Test]
    public async Task Player_ShowsNameAndStats()
    {
        _store.Names["alice"] = "アリス";
        _store.Stats.Add(Stats("alice", "vanish", 1516, 1, wins: 1));
        var (status, json, cache) = await Call("GET", "/api/players/alice");
        Assert.AreEqual(200, status);
        Assert.AreEqual("public, max-age=60", cache);
        var player = json.GetProperty("player");
        Assert.AreEqual("アリス", player.GetProperty("name").GetString());
        Assert.AreEqual(1516, player.GetProperty("games")[0].GetProperty("rating").GetInt32());
    }

    [Test]
    public async Task Player_Unknown_IsNullNot404()
    {
        var (status, json, _) = await Call("GET", "/api/players/nobody");
        Assert.AreEqual(200, status); // 404 は CloudFront がサイトの 404 ページに差し替えるので使わない
        Assert.AreEqual(JsonValueKind.Null, json.GetProperty("player").ValueKind);
    }

    [Test]
    public async Task Player_BadId_Is400() => Assert.AreEqual(400, (await Call("GET", "/api/players/a%20b")).Status);

    // ---------- 自分 ----------

    [Test]
    public async Task Me_WithoutOrWithBadToken_Is401()
    {
        Assert.AreEqual(401, (await Call("GET", "/api/me")).Status);
        Assert.AreEqual(401, (await Call("GET", "/api/me", user: "badtoken")).Status);
        Assert.AreEqual(401, (await Call("PUT", "/api/me/name", body: "{\"name\":\"アリス\"}")).Status);
        Assert.AreEqual(401, (await Call("DELETE", "/api/me")).Status);
    }

    [Test]
    public async Task Me_ReturnsOwnProfileWithoutCaching()
    {
        var (status, json, cache) = await Call("GET", "/api/me", user: "alice");
        Assert.AreEqual(200, status);
        Assert.AreEqual("no-store", cache);
        Assert.AreEqual("alice", json.GetProperty("player").GetProperty("id").GetString());
        Assert.AreEqual(JsonValueKind.Null, json.GetProperty("player").GetProperty("name").ValueKind);
    }

    [Test]
    public async Task SetName_SavesTheCleanedName()
    {
        var (status, json, _) = await Call("PUT", "/api/me/name", user: "alice", body: "{\"name\":\"  ゴースト   使い  \"}");
        Assert.AreEqual(200, status);
        Assert.AreEqual("ゴースト 使い", json.GetProperty("name").GetString());
        Assert.AreEqual("ゴースト 使い", _store.Names["alice"]);
    }

    [TestCase("{\"name\":\"あ\"}")]
    [TestCase("{\"name\":\"1234567890123\"}")]
    [TestCase("{\"name\":\"運営チーム\"}")]
    [TestCase("{\"name\":123}")]
    [TestCase("not json")]
    public async Task SetName_Invalid_Is400WithAMessage(string body)
    {
        var (status, json, _) = await Call("PUT", "/api/me/name", user: "alice", body: body);
        Assert.AreEqual(400, status);
        Assert.AreEqual("bad_name", json.GetProperty("error").GetString());
        Assert.IsNotEmpty(json.GetProperty("message").GetString());
        Assert.IsFalse(_store.Names.ContainsKey("alice"));
    }

    [Test]
    public async Task Delete_RemovesDataThenTheAccount()
    {
        _store.Names["alice"] = "アリス";
        _store.Stats.Add(Stats("alice", "vanish", 1516, 1));
        var (status, _, _) = await Call("DELETE", "/api/me", user: "alice");
        Assert.AreEqual(200, status);
        CollectionAssert.AreEqual(new[] { "alice" }, _store.Deleted);
        CollectionAssert.AreEqual(new[] { "alice" }, _accounts.Deleted);
        Assert.IsEmpty(_store.Stats);
    }

    [Test]
    public async Task UnknownRoute_Is400()
    {
        Assert.AreEqual(400, (await Call("GET", "/api/nothing")).Status);
        Assert.AreEqual(400, (await Call("POST", "/api/ranking/vanish")).Status);
        Assert.AreEqual(400, (await Call("GET", "/other")).Status);
    }

    // ---------- 名前の決まり ----------

    [TestCase("アリス", "アリス")]
    [TestCase("  ゴースト\t 使い ", "ゴースト 使い")]
    [TestCase("👨‍👩‍👧‍👦家族", "👨‍👩‍👧‍👦家族")] // 絵文字（ZWJ でつないだもの）は 1 文字
    [TestCase("シネマ好き", "シネマ好き")]        // 普通の言葉をはねない
    [TestCase("Essex", "Essex")]
    [TestCase("ab", "ab")]
    [TestCase("123456789012", "123456789012")]
    public void NameRules_Accepts(string input, string expected)
    {
        Assert.IsTrue(NameRules.TryNormalize(input, out var name, out var error), error);
        Assert.AreEqual(expected, name);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("あ")]
    [TestCase("1234567890123")]
    [TestCase("改行\nあり")]
    [TestCase("見えない​もじ")]
    [TestCase("ＡＤＭＩＮ")]           // 全角・大文字でも
    [TestCase("ボード・ピア運営")]
    [TestCase("死ねばいい")]
    [TestCase("こ ろ す")]              // 空白をはさんでも、ひらがなでも
    public void NameRules_Rejects(string? input)
    {
        Assert.IsFalse(NameRules.TryNormalize(input, out _, out var error));
        Assert.IsNotEmpty(error);
    }
}

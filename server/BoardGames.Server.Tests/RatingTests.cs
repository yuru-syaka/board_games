using BoardGames.Net;
using BoardGames.Server.Games;
using BoardGames.Vanish;
using BoardGames.Vanish.Online;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>
/// ログインと戦績（ステップ 5-2）。@auth で席とユーザーを結びつけ、終局したら勝敗の数を、
/// 両者ログイン中のランダムマッチならレートも、結果と同時に 1 回だけ書く。
/// </summary>
public class RatingTests
{
    InMemoryStore _store = null!;
    FakePush _push = null!;
    GameServer _server = null!;
    DateTimeOffset _now;
    readonly VanishRules _rules = new();

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryStore();
        _push = new FakePush();
        _now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        _server = new GameServer(_store, _push, GameRegistry.Default(), new Random(11), () => _now, verifier: new FakeVerifier());
    }

    FakeClient Client(string id, string? user)
    {
        var c = new FakeClient(_server, _push, id);
        if (user != null) c.Send(LobbyProtocol.Auth(FakeVerifier.TokenFor(user)));
        return c;
    }

    /// <summary>2 人をつないで対局を始める（room = true ならルームコード、false ならランダムマッチ）。</summary>
    (FakeClient a, FakeClient b, OnlineMatch ma, OnlineMatch mb) Pair(string? userA, string? userB, bool room = false)
    {
        var a = Client("A", userA);
        var b = Client("B", userB);
        if (room)
        {
            a.Send(LobbyProtocol.Create("vanish"));
            b.Send(LobbyProtocol.Join("vanish", a.Lobby.Last(m => m.Kind == LobbyMessageKind.Joined).Code));
        }
        else
        {
            a.Send(LobbyProtocol.Quick("vanish"));
            b.Send(LobbyProtocol.Quick("vanish"));
        }
        var ma = new OnlineMatch(a, 0);
        var mb = new OnlineMatch(b, 1);
        a.Deliver(a.Received.Last(m => m.StartsWith("S|")));
        b.Deliver(b.Received.Last(m => m.StartsWith("S|")));
        return (a, b, ma, mb);
    }

    void PlayToTheEnd(OnlineMatch ma, OnlineMatch mb)
    {
        while (!ma.State.Result.IsOver)
        {
            var side = ma.State.CurrentPlayer == 0 ? ma : mb;
            side.SubmitMove(_rules.GetLegalMoves(side.State)[0]);
        }
    }

    // ---------- ログイン ----------

    [Test]
    public void Auth_Accepted_RepliesWithTheDisplayName()
    {
        _store.SetName("alice", "アリス");
        var a = Client("A", "alice");
        Assert.AreEqual(LobbyMessageKind.Authed, a.LastLobby!.Value.Kind);
        Assert.AreEqual("アリス", a.LastLobby!.Value.Name);

        var b = Client("B", "bob"); // 名前をまだ決めていない
        Assert.AreEqual(LobbyMessageKind.Authed, b.LastLobby!.Value.Kind);
        Assert.IsNull(b.LastLobby!.Value.Name);
    }

    [Test]
    public void Auth_Rejected_RepliesAuthFailedAndPlaysAsGuest()
    {
        var a = Client("A", "badtoken");
        Assert.AreEqual(LobbyErrors.AuthFailed, a.LastLobby!.Value.ErrorCode);

        var b = Client("B", "bob");
        a.Send(LobbyProtocol.Quick("vanish"));
        b.Send(LobbyProtocol.Quick("vanish"));
        var match = _store.PeekMatch()!;
        Assert.IsNull(match.Users[0]);
        Assert.AreEqual("bob", match.Users[1]);
    }

    [Test]
    public void Auth_WithoutVerifier_IsRejected()
    {
        var server = new GameServer(_store, _push, GameRegistry.Default());
        var a = new FakeClient(server, _push, "A");
        a.Send(LobbyProtocol.Auth(FakeVerifier.TokenFor("alice")));
        Assert.AreEqual(LobbyErrors.AuthFailed, a.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void GameStart_SendsBothNamesAndRatings()
    {
        _store.SetName("alice", "アリス");
        var (a, b, _, _) = Pair("alice", null);
        foreach (var c in new[] { a, b })
        {
            var players = c.Lobby.Last(m => m.Kind == LobbyMessageKind.Players);
            CollectionAssert.AreEqual(new[] { "アリス", null }, players.Names);
            CollectionAssert.AreEqual(new[] { 1500, -1 }, players.Ratings);
        }
    }

    // ---------- 戦績とレート ----------

    [Test]
    public void RankedGame_UpdatesRatingsAndCountsOnce()
    {
        var (a, b, ma, mb) = Pair("alice", "bob");
        PlayToTheEnd(ma, mb);

        var result = _store.Results.Single();
        CollectionAssert.AreEqual(new[] { "alice", "bob" }, result.Users);
        int winner = result.Winner;
        var expectedAfter = winner switch { 0 => new[] { 1516, 1484 }, 1 => new[] { 1484, 1516 }, _ => new[] { 1500, 1500 } };
        CollectionAssert.AreEqual(expectedAfter, result.RatingsAfter);
        CollectionAssert.AreEqual(expectedAfter.Select(r => r - 1500).ToArray(), result.RatingDeltas);

        var alice = _store.Stats("alice", "vanish")!;
        var bob = _store.Stats("bob", "vanish")!;
        Assert.AreEqual(expectedAfter[0], alice.Rating);
        Assert.AreEqual(expectedAfter[1], bob.Rating);
        Assert.AreEqual((1, 1, 1), (alice.Games, alice.RatedGames, bob.Games));
        Assert.AreEqual(winner == 0 ? 1 : 0, alice.Wins);
        Assert.AreEqual(winner == 1 ? 1 : 0, alice.Losses);
        Assert.AreEqual(winner == -1 ? 1 : 0, alice.Draws);

        // 両者に新しいレートと増減が届く
        var ratedA = a.Lobby.Single(m => m.Kind == LobbyMessageKind.Rated);
        var ratedB = b.Lobby.Single(m => m.Kind == LobbyMessageKind.Rated);
        Assert.AreEqual((expectedAfter[0], expectedAfter[0] - 1500), (ratedA.Rating, ratedA.Delta));
        Assert.AreEqual((expectedAfter[1], expectedAfter[1] - 1500), (ratedB.Rating, ratedB.Delta));

        // 終局後に抜けても、もう一度数えない
        a.Send(LobbyProtocol.Leave());
        Assert.AreEqual(1, _store.Results.Count);
        Assert.AreEqual(1, _store.Stats("alice", "vanish")!.Games);
    }

    [Test]
    public void Rematch_StartsFromTheNewRatings()
    {
        var (a, b, ma, mb) = Pair("alice", "bob");
        PlayToTheEnd(ma, mb);
        int aliceAfter = _store.Stats("alice", "vanish")!.Rating;

        a.Send("R|1");
        b.Send("R|1");
        var players = a.Lobby.Last(m => m.Kind == LobbyMessageKind.Players);
        Assert.AreEqual(aliceAfter, players.Ratings[0]);
    }

    [Test]
    public void RoomCodeGame_CountsWinsButKeepsRatings()
    {
        var (a, b, ma, mb) = Pair("alice", "bob", room: true);
        PlayToTheEnd(ma, mb);

        var result = _store.Results.Single();
        CollectionAssert.AreEqual(new[] { "alice", "bob" }, result.Users);
        Assert.IsNull(result.RatingsAfter);
        var alice = _store.Stats("alice", "vanish")!;
        Assert.AreEqual((1500, 1, 0), (alice.Rating, alice.Games, alice.RatedGames));
        Assert.IsFalse(a.Lobby.Any(m => m.Kind == LobbyMessageKind.Rated));
        Assert.IsFalse(b.Lobby.Any(m => m.Kind == LobbyMessageKind.Rated));
    }

    [Test]
    public void GuestOpponent_CountsOnlyTheSignedInPlayerWithoutRating()
    {
        var (_, _, ma, mb) = Pair("alice", null);
        PlayToTheEnd(ma, mb);

        var result = _store.Results.Single();
        CollectionAssert.AreEqual(new[] { "alice", null }, result.Users);
        Assert.IsNull(result.RatingsAfter);
        Assert.AreEqual((1500, 1), (_store.Stats("alice", "vanish")!.Rating, _store.Stats("alice", "vanish")!.Games));
    }

    [Test]
    public void SameAccountOnBothSeats_CountsNothing()
    {
        var (_, _, ma, mb) = Pair("alice", "alice");
        PlayToTheEnd(ma, mb);

        Assert.AreEqual(1, _store.Results.Count);
        Assert.IsNull(_store.Results[0].RatingsAfter);
        Assert.IsNull(_store.Stats("alice", "vanish"));
    }

    [Test]
    public void LeavingMidGame_IsALossForTheRating()
    {
        var (a, b, _, _) = Pair("alice", "bob");
        a.Send(LobbyProtocol.Leave());

        var result = _store.Results.Single();
        Assert.AreEqual((1, ResultReasons.Left), (result.Winner, result.Reason));
        Assert.AreEqual(1484, _store.Stats("alice", "vanish")!.Rating);
        Assert.AreEqual(1516, _store.Stats("bob", "vanish")!.Rating);
        // 残った側には「相手が抜けた」のあとにレートが届く
        int left = b.Received.FindIndex(m => m == LobbyProtocol.OpponentLeft());
        int rated = b.Received.FindIndex(m => m.StartsWith("@rated|"));
        Assert.That(left, Is.GreaterThanOrEqualTo(0));
        Assert.That(rated, Is.GreaterThan(left));
    }

    [Test]
    public void Timeout_IsALossForTheRating()
    {
        var (a, b, ma, _) = Pair("alice", "bob");
        int mover = ma.State.CurrentPlayer;
        var (moverClient, waiterClient) = mover == 0 ? (a, b) : (b, a);
        // 手番の側が制限時間を過ぎる → 待っている側が申請して勝つ
        _now = _now.AddSeconds(91);
        waiterClient.Send(LobbyProtocol.Claim());

        var result = _store.Results.Single();
        Assert.AreEqual((1 - mover, ResultReasons.Timeout), (result.Winner, result.Reason));
        var users = new[] { "alice", "bob" };
        Assert.AreEqual(1484, _store.Stats(users[mover], "vanish")!.Rating);
        Assert.AreEqual(1516, _store.Stats(users[1 - mover], "vanish")!.Rating);
        // 時間切れのお知らせのあとに、レートが届く
        Assert.IsTrue(moverClient.Lobby.Any(m => m.Kind == LobbyMessageKind.Rated && m.Delta == -16));
        Assert.IsTrue(waiterClient.Lobby.Any(m => m.Kind == LobbyMessageKind.Rated && m.Delta == 16));
    }

    [Test]
    public void ConflictingStatsWrite_IsRetried()
    {
        var (_, _, ma, mb) = Pair("alice", "bob");
        _store.FailNextResultWrites = 2;
        PlayToTheEnd(ma, mb);

        Assert.AreEqual(1, _store.Results.Count);
        Assert.AreEqual(1, _store.Stats("alice", "vanish")!.Games);
    }

    [Test]
    public void Resume_SendsPlayersAgain()
    {
        var (a, _, _, _) = Pair("alice", "bob");
        string token = a.Lobby.Last(m => m.Kind == LobbyMessageKind.Token).Token;
        a.Disconnect();

        var a2 = new FakeClient(_server, _push, "A2");
        a2.Send(LobbyProtocol.Resume(token));
        var players = a2.Lobby.Single(m => m.Kind == LobbyMessageKind.Players);
        Assert.AreEqual(1500, players.Ratings[0]);
        Assert.AreEqual("alice", _store.PeekMatch()!.Users[0], "戻っても席のユーザーは変わらない");
    }

    // ---------- Elo ----------

    [TestCase(1500, 1500, 1.0, 16)]
    [TestCase(1500, 1500, 0.5, 0)]
    [TestCase(1500, 1500, 0.0, -16)]
    [TestCase(1700, 1500, 1.0, 8)]  // 強い側が勝っても少ししか上がらない
    [TestCase(1500, 1700, 1.0, 24)] // 弱い側が勝つと大きく上がる
    public void Elo_Deltas(int a, int b, double score, int expectedA)
    {
        var (da, db) = Elo.Deltas(a, b, score, 32);
        Assert.AreEqual(expectedA, da);
        Assert.AreEqual(-expectedA, db);
    }

    // ---------- メッセージの形 ----------

    [Test]
    public void Protocol_RoundTripsNewMessages()
    {
        Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Players("A|B", 1520, null, -1), out var p));
        CollectionAssert.AreEqual(new[] { "A|B", null }, p.Names);
        CollectionAssert.AreEqual(new[] { 1520, -1 }, p.Ratings);

        Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Rated(1484, -16), out var r));
        Assert.AreEqual((1484, -16), (r.Rating, r.Delta));

        Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Authed(null), out var authed));
        Assert.IsNull(authed.Name);

        string jwt = "eyJhbGciOiJSUzI1NiJ9." + new string('a', 1200) + ".sig_-x";
        Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Auth(jwt), out var auth));
        Assert.AreEqual(jwt, auth.IdToken);
        Assert.IsFalse(LobbyProtocol.TryParse("@auth|not a token", out _));
    }
}

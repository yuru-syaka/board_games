using BoardGames.GhostTrend;
using BoardGames.GhostTrend.Online;
using BoardGames.Net;
using BoardGames.Server.Games;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>
/// 多人数の部屋（ゴースト・トレンド）。全席が人間で、ホストが開始し、抜けた席だけ CPU が代わる。
/// ブラウザ側は Unity と同じ OnlineMatch を使う。
/// </summary>
public class TableTests
{
    const string Game = GhostTrendEngine.GameId;

    InMemoryStore _store = null!;
    FakePush _push = null!;
    GameServer _server = null!;
    DateTimeOffset _now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryStore();
        _push = new FakePush();
        _server = new GameServer(_store, _push, GameRegistry.Default(), new Random(99), () => _now, verifier: new FakeVerifier());
    }

    FakeClient Client(string id, string? user = null)
    {
        var c = new FakeClient(_server, _push, id);
        if (user != null) c.Send(LobbyProtocol.Auth(FakeVerifier.TokenFor(user)));
        return c;
    }

    static int SeatOf(FakeClient c) => c.Lobby.Last(l => l.Kind == LobbyMessageKind.Joined || l.Kind == LobbyMessageKind.Resumed).Seat;

    static LobbyMessage LastTable(FakeClient c) => c.Lobby.Last(l => l.Kind == LobbyMessageKind.Table);

    static string LastSnapshot(FakeClient c) => c.Received.Last(m => m.StartsWith("S|", StringComparison.Ordinal));

    static bool GotError(FakeClient c, string code) => c.Lobby.Any(l => l.Kind == LobbyMessageKind.Error && l.ErrorCode == code);

    /// <summary>n 人がランダムマッチで同じ部屋に入る（先頭がホスト）。</summary>
    List<FakeClient> QuickTable(int n, params string?[] users)
    {
        var clients = new List<FakeClient>();
        for (int i = 0; i < n; i++)
        {
            var c = Client("P" + i, i < users.Length ? users[i] : null);
            c.Send(LobbyProtocol.Quick(Game));
            clients.Add(c);
        }
        return clients;
    }

    /// <summary>n 人で始めた対局（5 人未満ならホストが @start）。</summary>
    List<FakeClient> StartedTable(int n, params string?[] users)
    {
        var clients = QuickTable(n, users);
        if (n < TrendLogic.MaxPlayers) clients[0].Send(LobbyProtocol.Start());
        return clients;
    }

    /// <summary>各ブラウザの OnlineMatch（始まったあとの席で作り、最初の局面を渡す）。</summary>
    static Dictionary<FakeClient, OnlineMatch> Matches(IEnumerable<FakeClient> clients)
    {
        var matches = new Dictionary<FakeClient, OnlineMatch>();
        foreach (var c in clients)
        {
            var m = new OnlineMatch(c, SeatOf(c));
            c.Deliver(LastSnapshot(c));
            matches[c] = m;
        }
        return matches;
    }

    static TrendMove Pick(MatchSnapshot s, Random rng)
    {
        var moves = s.View.GetLegalMoves();
        var quit = moves.Where(m => m.Kind is MoveKind.Pass or MoveKind.Decline).ToList();
        return quit.Count > 0 && rng.Next(2) == 0 ? quit[0] : moves[rng.Next(Math.Min(moves.Count, 12))];
    }

    /// <summary>手番の人（まだ座っている人）がランダムに指して、最後まで進める。</summary>
    static void PlayOut(Dictionary<FakeClient, OnlineMatch> matches, Random rng, int maxPlies = 40000)
    {
        for (int i = 0; i < maxPlies; i++)
        {
            var any = matches.Values.First();
            if (any.Snapshot.Result.IsOver) return;
            var mover = matches.Values.Single(m => m.LocalPlayer == any.Snapshot.CurrentPlayer);
            Assert.IsTrue(mover.Snapshot.View.IsMyTurn);
            mover.SubmitLocalMove(Pick(mover.Snapshot, rng));
        }
        Assert.Fail("終局しなかった");
    }

    // ---------- 部屋に入る・始める ----------

    [Test]
    public void QuickMatch_GathersPlayers_ShowsTheTable_AndTheHostStarts()
    {
        var p = QuickTable(3);
        Assert.AreEqual(new[] { 0, 1, 2 }, p.Select(SeatOf).ToArray());
        var table = LastTable(p[0]);
        Assert.AreEqual(3, table.Count);
        Assert.AreEqual(TrendLogic.MaxPlayers, table.Capacity);
        Assert.AreEqual(0, table.Host);
        Assert.IsTrue(_store.HasQueue(Game), "まだ空きがあるので、次の人が入れる");

        p[1].Send(LobbyProtocol.Start());
        Assert.IsTrue(GotError(p[1], LobbyErrors.NotHost), "ホスト以外は始められない");

        p[0].Send(LobbyProtocol.Start());
        Assert.IsFalse(_store.HasQueue(Game), "始まったら、もう入れない");
        var m = Matches(p);
        Assert.AreEqual(3, m[p[0]].Snapshot.PlayerCount);
        Assert.AreEqual(new[] { 0, 1, 2 }, p.Select(c => m[c].Snapshot.Viewer).ToArray());
        Assert.AreEqual(TrendPlayerView.HiddenMoney, m[p[0]].Snapshot.View.Money[1], "相手の所持金は届かない");
    }

    [Test]
    public void TwoPlayers_CannotStart()
    {
        var p = QuickTable(2);
        p[0].Send(LobbyProtocol.Start());
        Assert.IsTrue(GotError(p[0], LobbyErrors.NotEnoughPlayers));
        Assert.IsFalse(_store.PeekMatch()!.Started);
        Assert.IsFalse(p[0].Received.Any(r => r.StartsWith("S|", StringComparison.Ordinal)));
    }

    [Test]
    public void FivePlayers_StartAutomatically_AndTheSixthGetsANewRoom()
    {
        var p = QuickTable(5);
        Assert.IsTrue(p.All(c => c.Received.Any(r => r.StartsWith("S|", StringComparison.Ordinal))), "5 人で自動で始まる");
        Assert.AreEqual(5, Matches(p)[p[4]].Snapshot.PlayerCount);
        var sixth = Client("P5");
        sixth.Send(LobbyProtocol.Quick(Game));
        Assert.AreEqual(0, SeatOf(sixth), "新しい部屋のホストになる");
        Assert.AreEqual(1, LastTable(sixth).Count);
    }

    [Test]
    public void RoomCode_JoinsUpToFive_ThenIsFull()
    {
        var host = Client("H");
        host.Send(LobbyProtocol.Create(Game));
        var code = host.Lobby.First(l => l.Kind == LobbyMessageKind.Joined).Code;
        Assert.IsNotNull(code);
        Assert.IsFalse(_store.HasQueue(Game), "ルームコードの部屋はランダムマッチに出さない");
        for (int i = 0; i < 4; i++) Client("J" + i).Send(LobbyProtocol.Join(Game, code));
        Assert.IsTrue(_store.PeekMatch()!.Started, "5 人で自動で始まる");
        var late = Client("Late");
        late.Send(LobbyProtocol.Join(Game, code));
        Assert.IsTrue(GotError(late, LobbyErrors.RoomNotFound) || GotError(late, LobbyErrors.RoomFull));
    }

    [Test]
    public void HostLeavesTheWaitingRoom_TheNextPlayerBecomesHost_AndSeatsAreRenumberedAtStart()
    {
        var p = QuickTable(3);
        p[0].Send(LobbyProtocol.Leave());
        var table = LastTable(p[1]);
        Assert.AreEqual(2, table.Count);
        Assert.AreEqual(1, table.Host, "席 1 の人が次のホスト");

        var d = Client("D");
        d.Send(LobbyProtocol.Quick(Game));
        Assert.AreEqual(0, SeatOf(d), "空いた席 0 に入る");
        Assert.AreEqual(3, LastTable(p[1]).Count);

        p[1].Send(LobbyProtocol.Start());
        var players = new[] { p[1], p[2], d };
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, players.Select(SeatOf), "始めるときに 0 から詰め直す");
        var m = Matches(players);
        Assert.AreEqual(3, m[d].Snapshot.PlayerCount);
    }

    // ---------- 対局 ----------

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void FullGame_RanksByMoney_AndMovesEveryRating(int n)
    {
        var users = Enumerable.Range(0, n).Select(i => "user" + i).ToArray();
        var p = StartedTable(n, users);
        var m = Matches(p);
        PlayOut(m, new Random(n));

        var result = _store.Results.Single();
        Assert.AreEqual(Game, result.Game);
        Assert.AreEqual(n, result.Users!.Count);
        var money = m[p[0]].Snapshot.View.Money;
        for (int s = 0; s < n; s++)
            Assert.AreEqual(Enumerable.Range(0, n).Count(o => money[o] > money[s]), result.Ranks![s], "所持金の順位");
        Assert.IsNotNull(result.RatingDeltas, "ランダムマッチでログインした人が 2 人以上: レートが動く");
        Assert.That(Math.Abs(result.RatingDeltas!.Sum()), Is.LessThanOrEqualTo(n), "合計はほぼ 0（丸めの分だけずれる）");
        foreach (var c in p) Assert.IsTrue(c.Lobby.Any(l => l.Kind == LobbyMessageKind.Rated), "全員に新しいレート");

        int firstPlaces = result.Ranks!.Count(r => r == 0);
        for (int s = 0; s < n; s++)
        {
            var stats = _store.Stats(result.Users[s]!, Game)!;
            Assert.AreEqual(1, stats.Games);
            Assert.AreEqual(result.Ranks[s] == 0 && firstPlaces == 1 ? 1 : 0, stats.Wins);
            Assert.AreEqual(result.Ranks[s] == 0 && firstPlaces > 1 ? 1 : 0, stats.Draws);
            Assert.AreEqual(result.Ranks[s] > 0 ? 1 : 0, stats.Losses);
            Assert.AreEqual(result.RatingsAfter![s], stats.Rating);
        }
    }

    [Test]
    public void RoomCodeGame_KeepsTheCountsButDoesNotMoveRatings()
    {
        var host = Client("H", "u0");
        host.Send(LobbyProtocol.Create(Game));
        var code = host.Lobby.First(l => l.Kind == LobbyMessageKind.Joined).Code;
        var b = Client("B", "u1");
        var c = Client("C", "u2");
        b.Send(LobbyProtocol.Join(Game, code));
        c.Send(LobbyProtocol.Join(Game, code));
        host.Send(LobbyProtocol.Start());
        PlayOut(Matches(new[] { host, b, c }), new Random(5));
        var result = _store.Results.Single();
        Assert.IsNull(result.RatingDeltas);
        Assert.AreEqual(1, _store.Stats("u0", Game)!.Games);
        Assert.AreEqual(0, _store.Stats("u0", Game)!.RatedGames);
    }

    [Test]
    public void GuestsAndADuplicateAccount_AreNotCounted()
    {
        var p = StartedTable(4, "same", "same", "solo", null);
        PlayOut(Matches(p), new Random(3));
        Assert.IsNull(_store.Stats("same", Game), "同じアカウントが 2 席: 数えない");
        Assert.AreEqual(1, _store.Stats("solo", Game)!.Games);
        Assert.IsNull(_store.Results.Single().RatingDeltas, "数える人が 1 人だけ: レートは動かない");
    }

    [Test]
    public void LeavingMidGame_HandsTheSeatToTheCpu_AndTheLeaverIsLast()
    {
        var p = StartedTable(3, "a", "b", "c");
        var m = Matches(p);
        var leaver = p.First(c => SeatOf(c) == 2);
        leaver.Send(LobbyProtocol.Leave());
        var stay = p.Where(c => c != leaver).ToList();
        foreach (var c in stay) Assert.IsTrue(c.Lobby.Any(l => l.Kind == LobbyMessageKind.CpuSeat && l.Seat == 2));
        m.Remove(leaver);
        foreach (var c in stay) c.Deliver(LastSnapshot(c));
        PlayOut(m, new Random(11));

        var result = _store.Results.Single();
        Assert.AreEqual(2, result.Ranks![2], "抜けた人は最下位");
        Assert.AreEqual(1, _store.Stats("c", Game)!.Losses);
        Assert.IsTrue(result.Moves.Contains("2:@cpu"), "代わった時点が手の一覧に残る");
    }

    [Test]
    public void EveryoneLeaving_FinishesTheGameWithCpus_AndClosesTheRoom()
    {
        var p = StartedTable(3);
        foreach (var c in p) c.Send(LobbyProtocol.Leave());
        Assert.AreEqual(1, _store.Results.Count, "全員 CPU で最後まで進み、結果が残る");
        Assert.AreEqual(0, _store.MatchCount);
        Assert.AreEqual(0, _store.TokenCount);
    }

    [Test]
    public void Disconnect_WaitsThenClaimHandsTheSeatToTheCpu()
    {
        var p = StartedTable(3);
        var m = Matches(p);
        var gone = p.First(c => SeatOf(c) == 1);
        gone.Disconnect();
        var stay = p.Where(c => c != gone).ToList();
        Assert.IsTrue(stay.All(c => c.Lobby.Any(l => l.Kind == LobbyMessageKind.OpponentAway && l.Seat == 1 && l.Seconds == 90)));

        stay[0].Send(LobbyProtocol.Claim());
        Assert.IsTrue(GotError(stay[0], LobbyErrors.TooEarly), "待ち時間のうちは代われない");

        _now = _now.AddSeconds(91);
        stay[0].Send(LobbyProtocol.Claim());
        Assert.IsTrue(stay.All(c => c.Lobby.Any(l => l.Kind == LobbyMessageKind.CpuSeat && l.Seat == 1)));
        m.Remove(gone);
        foreach (var c in stay) c.Deliver(LastSnapshot(c));
        PlayOut(m, new Random(4));
        Assert.AreEqual(2, _store.Results.Single().Ranks![1]);
    }

    [Test]
    public void Disconnect_ThenResume_ReturnsToTheSameSeat()
    {
        var p = StartedTable(4);
        var gone = p.First(c => SeatOf(c) == 3);
        var token = gone.Lobby.Last(l => l.Kind == LobbyMessageKind.Token).Token;
        gone.Disconnect();
        var back = Client("Back");
        back.Send(LobbyProtocol.Resume(token));
        Assert.AreEqual(3, SeatOf(back));
        Assert.IsTrue(back.Received.Any(r => r.StartsWith("S|", StringComparison.Ordinal)), "今の局面が届く");
        Assert.IsTrue(p[0].Lobby.Any(l => l.Kind == LobbyMessageKind.OpponentBack));
    }

    [Test]
    public void Timeout_AutoMovesForTheSlowPlayer_AndTheThirdTimeTheCpuTakesOver()
    {
        var p = StartedTable(3);
        var m = Matches(p);
        int slow = m[p[0]].Snapshot.CurrentPlayer;
        var slowClient = p.First(c => SeatOf(c) == slow);
        var other = p.First(c => SeatOf(c) != slow);

        other.Send(LobbyProtocol.Claim());
        Assert.IsTrue(GotError(other, LobbyErrors.TooEarly));

        int autoMoves = 0;
        for (int round = 0; round < 200 && !p.Any(c => c.Lobby.Any(l => l.Kind == LobbyMessageKind.CpuSeat)); round++)
        {
            var snap = m[other].Snapshot;
            if (snap.Result.IsOver) break;
            if (snap.CurrentPlayer == slow)
            {
                _now = _now.AddSeconds(91);
                int before = _store.PeekMatch()!.Moves.Count;
                other.Send(LobbyProtocol.Claim());
                Assert.Greater(_store.PeekMatch()?.Moves.Count ?? int.MaxValue, before, "時間切れの人の手が進む");
                autoMoves++;
            }
            else
            {
                var mover = m.Values.Single(x => x.LocalPlayer == snap.CurrentPlayer);
                mover.SubmitLocalMove(Pick(mover.Snapshot, new Random(round)));
            }
        }
        Assert.AreEqual(TrendTable.TimeoutStrikesToTakeover, autoMoves, "3 回目で CPU が代わる");
        Assert.IsTrue(slowClient.Lobby.Any(l => l.Kind == LobbyMessageKind.CpuSeat && l.Seat == slow), "本人にも知らせる");
        Assert.IsTrue(other.Lobby.Any(l => l.Kind == LobbyMessageKind.CpuSeat && l.Seat == slow));
    }

    // ---------- 再戦 ----------

    [Test]
    public void Rematch_WhenEveryoneAsks_StartsANewGameWithTheSamePlayers()
    {
        var p = StartedTable(3);
        var m = Matches(p);
        PlayOut(m, new Random(1));
        foreach (var c in p) c.Send("R|1");
        var match = _store.PeekMatch()!;
        Assert.AreEqual(2, match.GameNo);
        Assert.AreEqual(0, match.Moves.Count(r => !r.EndsWith(":@cpu", StringComparison.Ordinal)), "新しい対局");
        Assert.IsTrue(p.All(c => c.Lobby.Count(l => l.Kind == LobbyMessageKind.Joined) >= 3), "席を知らせ直す");
    }

    [Test]
    public void Rematch_WithTooFewPlayersLeft_SaysSo()
    {
        var p = StartedTable(3);
        PlayOut(Matches(p), new Random(2));
        p[2].Send(LobbyProtocol.Leave());
        p[0].Send("R|1");
        p[1].Send("R|1");
        Assert.IsTrue(GotError(p[0], LobbyErrors.NotEnoughPlayers));
        Assert.AreEqual(1, _store.PeekMatch()!.GameNo, "新しい対局は始まらない");
    }

    // ---------- 2 人用には影響しない ----------

    [Test]
    public void TwoSeatGames_StillPairAtOnce()
    {
        var a = Client("A");
        var b = Client("B");
        a.Send(LobbyProtocol.Quick("geister"));
        b.Send(LobbyProtocol.Quick("geister"));
        Assert.IsFalse(a.Lobby.Any(l => l.Kind == LobbyMessageKind.Table), "2 人用には @table を送らない");
        Assert.IsTrue(b.Received.Any(r => r.StartsWith("S|", StringComparison.Ordinal)));
    }
}

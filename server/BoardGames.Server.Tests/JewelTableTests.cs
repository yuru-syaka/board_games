using BoardGames.GhostJewel;
using BoardGames.GhostJewel.Online;
using BoardGames.Net;
using BoardGames.Server.Games;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>
/// ゴースト・ジュエルの多人数の部屋（2〜4 人。仕組みはゴースト・トレンドと同じ GameServer.Table.cs）。
/// ブラウザ側は Unity と同じ OnlineMatch（com.boardpia.ghostjewel）を使う。
/// </summary>
public class JewelTableTests
{
    const string Game = GhostJewelEngine.GameId;

    InMemoryStore _store = null!;
    FakePush _push = null!;
    GameServer _server = null!;
    readonly DateTimeOffset _now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryStore();
        _push = new FakePush();
        _server = new GameServer(_store, _push, GameRegistry.Default(), new Random(7), () => _now, verifier: new FakeVerifier());
    }

    FakeClient Client(string id, string? user = null)
    {
        var c = new FakeClient(_server, _push, id);
        if (user != null) c.Send(LobbyProtocol.Auth(FakeVerifier.TokenFor(user)));
        return c;
    }

    static int SeatOf(FakeClient c) => c.Lobby.Last(l => l.Kind == LobbyMessageKind.Joined || l.Kind == LobbyMessageKind.Resumed).Seat;

    static string LastSnapshot(FakeClient c) => c.Received.Last(m => m.StartsWith("S|", StringComparison.Ordinal));

    List<FakeClient> StartedTable(int n, params string?[] users)
    {
        var clients = new List<FakeClient>();
        for (int i = 0; i < n; i++)
        {
            var c = Client("P" + i, i < users.Length ? users[i] : null);
            c.Send(LobbyProtocol.Quick(Game));
            clients.Add(c);
        }
        if (n < JewelLogic.MaxPlayers) clients[0].Send(LobbyProtocol.Start());
        return clients;
    }

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

    /// <summary>手番の人が CPU と同じ考え方で指して、最後まで進める（ランダムだと終局まで長いので）。</summary>
    static void PlayOut(Dictionary<FakeClient, OnlineMatch> matches, int seed)
    {
        var cpu = new JewelCpu(new Random(seed));
        for (int i = 0; i < 5000; i++)
        {
            var any = matches.Values.First();
            if (any.Snapshot.Result.IsOver) return;
            var mover = matches.Values.Single(m => m.LocalPlayer == any.Snapshot.CurrentPlayer);
            Assert.IsTrue(mover.Snapshot.View.IsMyTurn);
            int before = mover.Snapshot.Ply;
            mover.SubmitLocalMove(cpu.ChooseMove(mover.Snapshot.View));
            Assert.Greater(mover.Snapshot.Ply, before, "サーバーが手を受け付けた");
        }
        Assert.Fail("終局しなかった");
    }

    [Test]
    public void TwoPlayers_CanStart_AndFourStartAutomatically()
    {
        var two = StartedTable(2);
        var m = Matches(two);
        Assert.AreEqual(2, m[two[0]].Snapshot.PlayerCount, "2 人でホストが始められる");

        var four = new List<FakeClient>();
        for (int i = 0; i < 4; i++)
        {
            var c = Client("Q" + i);
            c.Send(LobbyProtocol.Quick(Game));
            four.Add(c);
        }
        Assert.IsTrue(four.All(c => c.Received.Any(r => r.StartsWith("S|", StringComparison.Ordinal))), "4 人で自動で始まる");
        Assert.AreEqual(4, Matches(four)[four[3]].Snapshot.PlayerCount);
    }

    [Test]
    public void OnePlayer_CannotStart()
    {
        var solo = Client("S");
        solo.Send(LobbyProtocol.Quick(Game));
        solo.Send(LobbyProtocol.Start());
        Assert.IsTrue(solo.Lobby.Any(l => l.Kind == LobbyMessageKind.Error && l.ErrorCode == LobbyErrors.NotEnoughPlayers));
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void FullGame_RanksByPoints_AndMovesEveryRating(int n)
    {
        var users = Enumerable.Range(0, n).Select(i => "user" + i).ToArray();
        var p = StartedTable(n, users);
        var m = Matches(p);
        PlayOut(m, n);

        var result = _store.Results.Single();
        Assert.AreEqual(Game, result.Game);
        var view = m[p[0]].Snapshot.View;
        for (int s = 0; s < n; s++)
        {
            var me = view.PlayerAt(s);
            int better = Enumerable.Range(0, n).Count(o =>
                view.PlayerAt(o).Points > me.Points ||
                (view.PlayerAt(o).Points == me.Points && view.PlayerAt(o).Bought.Count < me.Bought.Count));
            Assert.AreEqual(better, result.Ranks![s], "点の順位（同点ならカードが少ない人が上）");
        }
        Assert.IsNotNull(result.RatingDeltas, "ランダムマッチでログインした人が 2 人以上: レートが動く");
        foreach (var c in p) Assert.IsTrue(c.Lobby.Any(l => l.Kind == LobbyMessageKind.Rated), "全員に新しいレート");
        // 全員の画面が同じ終局を見ている
        Assert.IsTrue(m.Values.All(x => x.Snapshot.Result.IsOver));
    }

    [Test]
    public void DeckReservations_StayHiddenFromOpponents()
    {
        var p = StartedTable(2);
        var m = Matches(p);
        var mover = m.Values.Single(x => x.Snapshot.View.IsMyTurn);
        var other = m.Values.Single(x => x != mover);
        mover.SubmitLocalMove(JewelMove.ReserveDeck(2));
        Assert.AreNotEqual(-1, mover.Snapshot.View.PlayerAt(mover.LocalPlayer).Reserved[0], "自分には見える");
        Assert.AreEqual(-1, other.Snapshot.View.PlayerAt(mover.LocalPlayer).Reserved[0], "相手には裏向き");
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
        PlayOut(m, 11);

        var result = _store.Results.Single();
        Assert.AreEqual(2, result.Ranks![2], "抜けた人は最下位");
        Assert.AreEqual(1, _store.Stats("c", Game)!.Losses);
    }

    [Test]
    public void EveryoneLeaving_FinishesTheGameWithCpus()
    {
        var p = StartedTable(2);
        foreach (var c in p) c.Send(LobbyProtocol.Leave());
        Assert.AreEqual(1, _store.Results.Count, "全員 CPU で最後まで進み、結果が残る");
        Assert.AreEqual(0, _store.MatchCount);
    }

    [Test]
    public void GarbageMove_IsRejected()
    {
        var p = StartedTable(2);
        var m = Matches(p);
        var mover = p.Single(c => m[c].Snapshot.View.IsMyTurn);
        int ply = m[mover].Snapshot.Ply;
        mover.Send("M|1|not-a-move");
        mover.Send(JewelNetProtocol.EncodeMove(JewelMove.Buy(-3)));
        Assert.AreEqual(ply, m[mover].Snapshot.Ply);
    }
}

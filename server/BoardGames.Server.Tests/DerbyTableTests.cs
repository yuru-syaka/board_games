using BoardGames.GhostDerby;
using BoardGames.GhostDerby.Online;
using BoardGames.Net;
using BoardGames.Server.Games;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>
/// ゴースト・ダービーの多人数の部屋（2〜5 人。仕組みはゴースト・ジュエルと同じ GameServer.Table.cs）。
/// ブラウザ側は Unity と同じ OnlineMatch（com.boardpia.ghostderby）を使う。
/// </summary>
public class DerbyTableTests
{
    const string Game = GhostDerbyEngine.GameId;

    InMemoryStore _store = null!;
    FakePush _push = null!;
    GameServer _server = null!;
    readonly DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

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
        if (n < DerbyLogic.MaxPlayers) clients[0].Send(LobbyProtocol.Start());
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

    /// <summary>手番の人が CPU と同じ考え方で指して、最後まで進める。</summary>
    static void PlayOut(Dictionary<FakeClient, OnlineMatch> matches, int seed)
    {
        var cpu = new DerbyCpu(new Random(seed));
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
    public void TwoPlayers_CanStart_AndFiveStartAutomatically()
    {
        var two = StartedTable(2);
        var m = Matches(two);
        Assert.AreEqual(2, m[two[0]].Snapshot.PlayerCount, "2 人でホストが始められる");

        var five = new List<FakeClient>();
        for (int i = 0; i < 5; i++)
        {
            var c = Client("Q" + i);
            c.Send(LobbyProtocol.Quick(Game));
            five.Add(c);
        }
        Assert.IsTrue(five.All(c => c.Received.Any(r => r.StartsWith("S|", StringComparison.Ordinal))), "5 人で自動で始まる");
        Assert.AreEqual(5, Matches(five)[five[4]].Snapshot.PlayerCount);
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
    [TestCase(5)]
    public void FullGame_RanksByCoins_AndMovesEveryRating(int n)
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
            int better = Enumerable.Range(0, n).Count(o =>
                view.Coins(o) > view.Coins(s) || (view.Coins(o) == view.Coins(s) && view.Hits(o) > view.Hits(s)));
            Assert.AreEqual(better, result.Ranks![s], "コインの順位");
        }
        Assert.IsNotNull(result.RatingDeltas, "ランダムマッチでログインした人が 2 人以上: レートが動く");
        foreach (var c in p) Assert.IsTrue(c.Lobby.Any(l => l.Kind == LobbyMessageKind.Rated), "全員に新しいレート");
        Assert.IsTrue(m.Values.All(x => x.Snapshot.Result.IsOver));
    }

    [Test]
    public void TheDeckHandsAndHiddenBets_StayHidden()
    {
        var p = StartedTable(3);
        var m = Matches(p);
        var first = p.Single(c => m[c].Snapshot.View.IsMyTurn);
        int seat = m[first].LocalPlayer;
        m[first].SubmitLocalMove(DerbyMove.Bet(4, 3));
        foreach (var (client, match) in m)
        {
            client.Deliver(LastSnapshot(client));
            var v = match.Snapshot.View;
            Assert.IsTrue(v.Masked.Deck.All(c => c == -1), "山札は届かない");
            Assert.IsTrue(v.Masked.DrawPile.All(c => c == -1), "いたずら札の山は届かない");
            Assert.IsTrue(v.HasDecided(seat), "決めたことは全員に見える");
            for (int o = 0; o < 3; o++)
                if (o != match.LocalPlayer) Assert.IsTrue(v.Masked.Players[o].Hand.All(c => c == -1), "ほかの人の手札は届かない");
            if (client == first) Assert.AreEqual(4, v.BetRunner(seat), "自分の賭けは自分に見える");
            else
            {
                Assert.AreEqual(-1, v.BetRunner(seat), "公開前の賭けは届かない");
                Assert.AreEqual(DerbyLogic.StartCoins, v.Coins(seat), "賭けた額もコインから分からない");
            }
        }
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
        mover.Send("M|1|9|0|0");
        mover.Send("M|1|0|9|9");
        var other = p.Single(c => c != mover);
        other.Send(DerbyNetProtocol.EncodeMove(DerbyMove.Bet(0, 1))); // 手番でない人の手
        Assert.AreEqual(ply, m[mover].Snapshot.Ply);
    }
}

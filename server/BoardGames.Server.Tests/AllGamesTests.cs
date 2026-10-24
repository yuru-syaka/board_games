using BoardGames.Net;
using BoardGames.Server.Games;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>
/// ゴースト・マルバツ以外の 6 本を、サーバー経由で最後まで遊ぶ。
/// ブラウザ側は各ゲームの OnlineMatch（サーバーが決めた席のゲスト）で、打てる手からランダムに選ぶ。
/// 確かめること: 両者が同じ対局を見ていること、席ごとの視点で届くこと、隠し情報が相手に漏れないこと、終局すること。
/// </summary>
public class AllGamesTests
{
    InMemoryStore _store = null!;
    FakePush _push = null!;
    GameServer _server = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryStore();
        _push = new FakePush();
        _server = new GameServer(_store, _push, GameRegistry.Default(), new Random(99));
    }

    /// <summary>ランダムマッチで 2 人を組ませる（a が席 0、b が席 1）。</summary>
    (FakeClient a, FakeClient b) Pair(string game)
    {
        var a = new FakeClient(_server, _push, "A-" + game);
        var b = new FakeClient(_server, _push, "B-" + game);
        a.Send(LobbyProtocol.Quick(game));
        b.Send(LobbyProtocol.Quick(game));
        Assert.AreEqual(0, a.Lobby.First(l => l.Kind == LobbyMessageKind.Joined).Seat);
        Assert.AreEqual(1, b.Lobby.First(l => l.Kind == LobbyMessageKind.Joined).Seat);
        return (a, b);
    }

    /// <summary>OnlineMatch を作ってから、届いていた最初の局面を渡し直す。</summary>
    static void Replay(FakeClient client) => client.Deliver(client.Received.Last(m => m.StartsWith("S|")));

    static string LastSnapshot(FakeClient client) => client.Received.Last(m => m.StartsWith("S|"));

    const int Guard = 2000;

    [Test]
    public void Gobblet_PlaysToTheEnd()
    {
        var (a, b) = Pair("gobblet");
        var ma = new Gobblet.Online.OnlineMatch(a, 0);
        var mb = new Gobblet.Online.OnlineMatch(b, 1);
        Replay(a);
        Replay(b);
        var rules = new Gobblet.GobbletRules();
        var rng = new Random(1);
        for (int i = 0; i < Guard && !ma.State.Result.IsOver; i++)
        {
            var mover = ma.State.CurrentPlayer == 0 ? ma : mb;
            var moves = rules.GetLegalMoves(mover.State);
            Assert.IsTrue(mover.SubmitMove(moves[rng.Next(moves.Count)]));
            Assert.AreEqual(LastSnapshot(a), LastSnapshot(b), "完全情報なので同じ局面");
        }
        Assert.IsTrue(ma.State.Result.IsOver && mb.State.Result.IsOver);
    }

    [Test]
    public void Quarto_PlaysToTheEnd()
    {
        var (a, b) = Pair("quarto");
        var ma = new Quarto.Online.OnlineMatch(a, 0);
        var mb = new Quarto.Online.OnlineMatch(b, 1);
        Replay(a);
        Replay(b);
        Assert.AreEqual(0, ma.Snapshot.Viewer);
        Assert.AreEqual(1, mb.Snapshot.Viewer);
        var rng = new Random(2);
        for (int i = 0; i < Guard && !ma.Snapshot.Result.IsOver; i++)
        {
            var mover = ma.Snapshot.CurrentPlayer == 0 ? ma : mb;
            var moves = mover.Snapshot.GetLegalMoves();
            mover.SubmitLocalMove(moves[rng.Next(moves.Count)]);
            Assert.AreEqual(ma.Snapshot.Ply, mb.Snapshot.Ply);
        }
        Assert.IsTrue(ma.Snapshot.Result.IsOver && mb.Snapshot.Result.IsOver);
        Assert.AreEqual(ma.Snapshot.Result.Winner, mb.Snapshot.Result.Winner);
    }

    [Test]
    public void Geister_PlaysToTheEnd_AndHidesTheOpponentsGhosts()
    {
        var (a, b) = Pair("geister");
        Assert.AreNotEqual(LastSnapshot(a), LastSnapshot(b), "相手の駒の種類は席ごとに伏せる");
        var ma = new Geister.Online.OnlineMatch(a, 0);
        var mb = new Geister.Online.OnlineMatch(b, 1);
        Replay(a);
        Replay(b);
        Assert.AreEqual(0, ma.Snapshot.Viewer);
        Assert.AreEqual(1, mb.Snapshot.Viewer);
        var rng = new Random(3);
        for (int i = 0; i < Guard && !ma.Snapshot.Result.IsOver; i++)
        {
            var mover = ma.Snapshot.CurrentPlayer == 0 ? ma : mb;
            var moves = mover.Snapshot.View.GetLegalMoves();
            mover.SubmitLocalMove(moves[rng.Next(moves.Count)]);
            Assert.AreEqual(ma.Snapshot.Ply, mb.Snapshot.Ply);
        }
        Assert.IsTrue(ma.Snapshot.Result.IsOver && mb.Snapshot.Result.IsOver);
        Assert.AreEqual(ma.Snapshot.Result.Winner, mb.Snapshot.Result.Winner);
    }

    [Test]
    public void BattleLine_PlaysToTheEnd_AndHidesTheOpponentsHand()
    {
        var (a, b) = Pair("battleline");
        Assert.AreNotEqual(LastSnapshot(a), LastSnapshot(b), "相手の手札は席ごとに伏せる");
        var ma = new BattleLine.Online.OnlineMatch(a, 0);
        var mb = new BattleLine.Online.OnlineMatch(b, 1);
        Replay(a);
        Replay(b);
        Assert.AreEqual(0, ma.Snapshot.Viewer);
        Assert.AreEqual(1, mb.Snapshot.Viewer);
        var rng = new Random(4);
        for (int i = 0; i < Guard && !ma.Snapshot.Result.IsOver; i++)
        {
            var mover = ma.Snapshot.CurrentPlayer == 0 ? ma : mb;
            var moves = mover.Snapshot.View.GetLegalMoves();
            mover.SubmitLocalMove(moves[rng.Next(moves.Count)]);
            Assert.AreEqual(ma.Snapshot.Step, mb.Snapshot.Step);
        }
        Assert.IsTrue(ma.Snapshot.Result.IsOver && mb.Snapshot.Result.IsOver);
        Assert.AreEqual(ma.Snapshot.Result.Winner, mb.Snapshot.Result.Winner);
    }

    [Test]
    public void CenturyGhost_PlaysToTheEnd()
    {
        var (a, b) = Pair("centuryghost");
        var ma = new CenturyGhost.Online.OnlineMatch(a, 0);
        var mb = new CenturyGhost.Online.OnlineMatch(b, 1);
        Replay(a);
        Replay(b);
        Assert.AreEqual(0, ma.Snapshot.Viewer);
        Assert.AreEqual(1, mb.Snapshot.Viewer);
        var rng = new Random(5);
        for (int i = 0; i < Guard && !ma.Snapshot.Result.IsOver; i++)
        {
            var mover = ma.Snapshot.CurrentPlayer == 0 ? ma : mb;
            var moves = mover.Snapshot.View.GetLegalMoves();
            mover.SubmitLocalMove(moves[rng.Next(moves.Count)]);
            Assert.AreEqual(ma.Snapshot.Ply, mb.Snapshot.Ply);
        }
        Assert.IsTrue(ma.Snapshot.Result.IsOver && mb.Snapshot.Result.IsOver);
        Assert.AreEqual(ma.Snapshot.Result.Winner, mb.Snapshot.Result.Winner);
    }

    [Test]
    public void GhostReversa_PlaysToTheEnd_AndHidesTheOpponentsPieces()
    {
        var (a, b) = Pair("ghostreversa");
        var ma = new GhostReversa.Online.OnlineMatch(a, 0);
        var mb = new GhostReversa.Online.OnlineMatch(b, 1);
        Replay(a);
        Replay(b);
        Assert.AreEqual(0, ma.Snapshot.Viewer);
        Assert.AreEqual(1, mb.Snapshot.Viewer);
        var rng = new Random(6);
        for (int i = 0; i < Guard && !ma.Snapshot.Result.IsOver; i++)
        {
            var mover = ma.Snapshot.CurrentPlayer == 0 ? ma : mb;
            var moves = mover.Snapshot.View.GetLegalMoves();
            mover.SubmitLocalMove(moves[rng.Next(moves.Count)]);
            Assert.AreEqual(ma.Snapshot.Ply, mb.Snapshot.Ply);
            if (ma.Snapshot.Result.IsOver) break;
            // 相手の捨てた色・手駒の内訳は届かない
            Assert.AreEqual(GhostReversa.TileKind.Unknown, ma.Snapshot.View.DiscardOf(1));
            Assert.AreEqual(GhostReversa.ReversaPlayerView.Hidden, mb.Snapshot.View.HandOf(0, GhostReversa.TileKind.Blue));
        }
        Assert.IsTrue(ma.Snapshot.Result.IsOver && mb.Snapshot.Result.IsOver);
        Assert.AreEqual(ma.Snapshot.Result.Winner, mb.Snapshot.Result.Winner);
    }

    [TestCase("gobblet")]
    [TestCase("quarto")]
    [TestCase("geister")]
    [TestCase("battleline")]
    [TestCase("centuryghost")]
    [TestCase("ghostreversa")]
    public void SameSeed_GivesTheSameStart(string game)
    {
        var engine = GameRegistry.Default().Find(game)!;
        for (int seed = 0; seed < 10; seed++)
            for (int seat = 0; seat < 2; seat++)
                Assert.AreEqual(engine.Start(seed).SnapshotFor(seat), engine.Start(seed).SnapshotFor(seat));
    }

    [TestCase("gobblet")]
    [TestCase("quarto")]
    [TestCase("geister")]
    [TestCase("battleline")]
    [TestCase("centuryghost")]
    [TestCase("ghostreversa")]
    public void GarbageMove_IsRejectedAndResyncsOnlyTheSender(string game)
    {
        var (a, b) = Pair(game);
        int before = b.Received.Count;
        a.Send("M|1|not-a-move");
        Assert.AreEqual(before, b.Received.Count, "相手には何も送らない");
        Assert.IsTrue(a.Received.Last().StartsWith("S|"), "送った側に今の局面を送り直す");
    }
}

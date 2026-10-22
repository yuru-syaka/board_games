using BoardGames.Core;
using BoardGames.Net;
using BoardGames.Server.Games;
using BoardGames.Vanish;
using BoardGames.Vanish.Online;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>対局結果を残す（ステップ 4-4）。終わり方ごとに 1 局 1 行、2 回は書かない、棋譜から再現できる。</summary>
public class ResultTests
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
        _server = new GameServer(_store, _push, GameRegistry.Default(), new Random(11), () => _now);
    }

    (FakeClient a, FakeClient b, OnlineMatch ma, OnlineMatch mb) Pair()
    {
        var a = new FakeClient(_server, _push, "A");
        var b = new FakeClient(_server, _push, "B");
        a.Send(LobbyProtocol.Quick("vanish"));
        b.Send(LobbyProtocol.Quick("vanish"));
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

    [Test]
    public void FinishedGame_IsRecordedOnceWithItsMovesAndSeed()
    {
        var (_, _, ma, mb) = Pair();
        PlayToTheEnd(ma, mb);

        var result = _store.Results.Single();
        Assert.AreEqual("vanish", result.Game);
        Assert.AreEqual(1, result.GameNo);
        Assert.AreEqual(ma.State.Ply, result.Plies);
        Assert.AreEqual(result.Plies, result.Moves.Count);
        Assert.AreEqual(_now.ToUnixTimeMilliseconds(), result.EndedAt);
        if (ma.State.Result.Status == GameStatus.Win)
        {
            Assert.AreEqual(ResultReasons.Normal, result.Reason);
            Assert.AreEqual(ma.State.Result.Winner, result.Winner);
        }
        else
        {
            Assert.AreEqual(ResultReasons.Draw, result.Reason);
            Assert.AreEqual(-1, result.Winner);
        }
    }

    [Test]
    public void TheRecordedMovesAndSeed_ReproduceTheSameResult()
    {
        var (_, _, ma, mb) = Pair();
        PlayToTheEnd(ma, mb);
        var result = _store.Results.Single();

        var session = new VanishEngine().Start(result.Seed);
        foreach (var move in result.Moves) Assert.IsTrue(session.TryApply(move[0] - '0', move.Substring(2), out _));
        Assert.IsTrue(session.IsOver);
        Assert.AreEqual(VanishNetProtocol.EncodeSnapshot(ma.State), session.SnapshotFor(0));
    }

    [Test]
    public void Rematch_RecordsTheNextGameAsANewRow()
    {
        var (_, _, ma, mb) = Pair();
        PlayToTheEnd(ma, mb);
        ma.RequestRematch();
        mb.RequestRematch();
        PlayToTheEnd(ma, mb);
        CollectionAssert.AreEqual(new[] { 1, 2 }, _store.Results.Select(r => r.GameNo).ToArray());
        Assert.AreEqual(1, _store.Results.Select(r => r.MatchId).Distinct().Count(), "同じ部屋の 1 局目と 2 局目");
    }

    [Test]
    public void LeavingAfterTheGameEnded_AddsNoSecondRow()
    {
        var (a, _, ma, mb) = Pair();
        PlayToTheEnd(ma, mb);
        a.Send(LobbyProtocol.Leave());
        Assert.AreEqual(1, _store.Results.Count);
    }

    [Test]
    public void LeavingMidGame_IsALossForTheOneWhoLeft()
    {
        var (a, _, _, _) = Pair();
        a.Send(LobbyProtocol.Leave());
        var result = _store.Results.Single();
        Assert.AreEqual(ResultReasons.Left, result.Reason);
        Assert.AreEqual(1, result.Winner, "残った席 1 の勝ち");
    }

    [Test]
    public void Timeout_IsRecorded()
    {
        var (a, b, ma, _) = Pair();
        var waiter = ma.State.CurrentPlayer == 0 ? b : a;
        int waiterSeat = waiter == a ? 0 : 1;
        _now = _now.AddSeconds(90);
        waiter.Send(LobbyProtocol.Claim());
        var result = _store.Results.Single();
        Assert.AreEqual(ResultReasons.Timeout, result.Reason);
        Assert.AreEqual(waiterSeat, result.Winner);
    }

    [Test]
    public void Abandoning_IsRecorded()
    {
        var (a, b, _, _) = Pair();
        _now = _now.AddSeconds(5); // 手番の時間切れより先に、離脱の待ち時間が過ぎるようにする
        b.Disconnect();
        _now = _now.AddSeconds(90);
        a.Send(LobbyProtocol.Claim());
        var result = _store.Results.Single();
        Assert.AreEqual(ResultReasons.Abandoned, result.Reason);
        Assert.AreEqual(0, result.Winner);
    }

    [Test]
    public void AGameThatNeverStarted_IsNotRecorded()
    {
        var a = new FakeClient(_server, _push, "A");
        a.Send(LobbyProtocol.Create("vanish"));
        a.Send(LobbyProtocol.Leave());
        Assert.IsEmpty(_store.Results);
    }
}

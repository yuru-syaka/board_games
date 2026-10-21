using BoardGames.Net;
using BoardGames.Server.Games;
using BoardGames.Vanish;
using BoardGames.Vanish.Online;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>1 手の制限時間（ステップ 4-3）。時計はテストから進める。</summary>
public class TurnTimerTests
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
        _server = new GameServer(_store, _push, GameRegistry.Default(), new Random(3), () => _now);
    }

    (FakeClient a, FakeClient b) Pair(string game = "vanish")
    {
        var a = new FakeClient(_server, _push, "A");
        var b = new FakeClient(_server, _push, "B");
        a.Send(LobbyProtocol.Quick(game));
        b.Send(LobbyProtocol.Quick(game));
        return (a, b);
    }

    static LobbyMessage LastTurn(FakeClient c) => c.Lobby.Last(l => l.Kind == LobbyMessageKind.Turn);

    static VanishState LastState(FakeClient c)
    {
        VanishNetProtocol.TryDecodeSnapshot(c.Received.Last(m => m.StartsWith("S|")), out var s);
        return s;
    }

    /// <summary>手番の人と、待っている人。</summary>
    (FakeClient mover, FakeClient waiter) Sides(FakeClient a, FakeClient b) => LastState(a).CurrentPlayer == 0 ? (a, b) : (b, a);

    [Test]
    public void Start_SendsTheTurnAndTheFullTimeToBothSeats()
    {
        var (a, b) = Pair();
        int current = LastState(a).CurrentPlayer;
        foreach (var c in new[] { a, b })
        {
            var turn = LastTurn(c);
            Assert.AreEqual(current, turn.Seat);
            Assert.AreEqual(90, turn.Seconds);
        }
    }

    [Test]
    public void CenturyGhost_HasALongerLimit()
    {
        var (a, _) = Pair("centuryghost");
        Assert.AreEqual(180, LastTurn(a).Seconds);
    }

    [Test]
    public void AMove_RestartsTheClockForTheOtherSeat()
    {
        var (a, b) = Pair();
        var (mover, _) = Sides(a, b);
        _now = _now.AddSeconds(40);
        mover.Send(VanishNetProtocol.EncodeMove(_rules.GetLegalMoves(LastState(mover))[0]));
        var turn = LastTurn(a);
        Assert.AreEqual(LastState(a).CurrentPlayer, turn.Seat);
        Assert.AreEqual(90, turn.Seconds, "新しい手番は 90 秒から");
    }

    [Test]
    public void Claim_BeforeTheLimit_IsTooEarly()
    {
        var (a, b) = Pair();
        var (_, waiter) = Sides(a, b);
        _now = _now.AddSeconds(89);
        waiter.Send(LobbyProtocol.Claim());
        Assert.AreEqual(LobbyErrors.TooEarly, waiter.LastLobby!.Value.ErrorCode);
        Assert.AreEqual(1, _store.MatchCount);
    }

    [Test]
    public void Claim_AfterTheLimit_OnTheOpponentsTurn_WinsByTimeout()
    {
        var (a, b) = Pair();
        var (mover, waiter) = Sides(a, b);
        _now = _now.AddSeconds(90);
        waiter.Send(LobbyProtocol.Claim());
        var won = waiter.LastLobby!.Value;
        Assert.AreEqual(LobbyMessageKind.Won, won.Kind);
        Assert.AreEqual(LobbyEndReasons.Timeout, won.Reason);
        var lost = mover.LastLobby!.Value;
        Assert.AreEqual(LobbyMessageKind.Lost, lost.Kind);
        Assert.AreEqual(LobbyEndReasons.Timeout, lost.Reason);
        Assert.AreEqual(0, _store.MatchCount);
        Assert.AreEqual(0, _store.ConnectionCount);
        Assert.AreEqual(0, _store.TokenCount);
    }

    [Test]
    public void Claim_OnYourOwnTurn_IsTooEarlyEvenAfterTheLimit()
    {
        var (a, b) = Pair();
        var (mover, _) = Sides(a, b);
        _now = _now.AddMinutes(5);
        mover.Send(LobbyProtocol.Claim());
        Assert.AreEqual(LobbyErrors.TooEarly, mover.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void ALateMove_BeforeAnyClaim_IsAccepted()
    {
        var (a, b) = Pair();
        var (mover, waiter) = Sides(a, b);
        _now = _now.AddSeconds(120); // 時間は過ぎたが、まだ申請されていない
        mover.Send(VanishNetProtocol.EncodeMove(_rules.GetLegalMoves(LastState(mover))[0]));
        Assert.AreEqual(1, LastState(a).Ply);
        waiter.Send(LobbyProtocol.Claim()); // 手番が変わったので、もう申請できない
        Assert.AreEqual(LobbyErrors.TooEarly, waiter.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void AfterTheGameEnds_TheClockStops()
    {
        var (a, b) = Pair();
        var ma = new OnlineMatch(a, 0);
        var mb = new OnlineMatch(b, 1);
        a.Deliver(a.Received.Last(m => m.StartsWith("S|")));
        b.Deliver(b.Received.Last(m => m.StartsWith("S|")));
        while (!ma.State.Result.IsOver)
        {
            var side = ma.State.CurrentPlayer == 0 ? ma : mb;
            side.SubmitMove(_rules.GetLegalMoves(side.State)[0]);
        }
        Assert.AreEqual(-1, LastTurn(a).Seat, "終局したら時間の計測なし");
        _now = _now.AddMinutes(10);
        a.Send(LobbyProtocol.Claim());
        Assert.AreEqual(LobbyErrors.TooEarly, a.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void Resume_TellsHowMuchOfTheTurnIsLeft()
    {
        var (a, b) = Pair();
        string tokenB = b.Lobby.Last(l => l.Kind == LobbyMessageKind.Token).Token;
        _now = _now.AddSeconds(25);
        b.Disconnect();
        _now = _now.AddSeconds(5);
        var b2 = new FakeClient(_server, _push, "B2");
        b2.Send(LobbyProtocol.Resume(tokenB));
        Assert.AreEqual(60, LastTurn(b2).Seconds, "手番が始まってから 30 秒たっている");
    }

    [Test]
    public void Rematch_RestartsTheClock()
    {
        var (a, b) = Pair();
        var ma = new OnlineMatch(a, 0);
        var mb = new OnlineMatch(b, 1);
        a.Deliver(a.Received.Last(m => m.StartsWith("S|")));
        b.Deliver(b.Received.Last(m => m.StartsWith("S|")));
        while (!ma.State.Result.IsOver)
        {
            var side = ma.State.CurrentPlayer == 0 ? ma : mb;
            side.SubmitMove(_rules.GetLegalMoves(side.State)[0]);
        }
        _now = _now.AddMinutes(3);
        ma.RequestRematch();
        mb.RequestRematch();
        Assert.AreEqual(90, LastTurn(a).Seconds);
        Assert.AreNotEqual(-1, LastTurn(a).Seat);
    }
}

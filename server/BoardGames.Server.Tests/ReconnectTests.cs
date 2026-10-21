using BoardGames.Net;
using BoardGames.Server.Games;
using BoardGames.Vanish;
using BoardGames.Vanish.Online;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>
/// 通信が切れたとき（ステップ 4-2）: 部屋を残して待つ、合言葉で戻る、待ち時間を過ぎたら勝ちを申請できる。
/// 時計はテストから進める。
/// </summary>
public class ReconnectTests
{
    InMemoryStore _store = null!;
    FakePush _push = null!;
    GameServer _server = null!;
    DateTimeOffset _now;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryStore();
        _push = new FakePush();
        _now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        _server = new GameServer(_store, _push, GameRegistry.Default(), new Random(7), () => _now);
    }

    FakeClient Client(string id) => new FakeClient(_server, _push, id);

    static string TokenOf(FakeClient c) => c.Lobby.Last(l => l.Kind == LobbyMessageKind.Token).Token;

    (FakeClient a, FakeClient b) Pair()
    {
        var a = Client("A");
        var b = Client("B");
        a.Send(LobbyProtocol.Quick("vanish"));
        b.Send(LobbyProtocol.Quick("vanish"));
        return (a, b);
    }

    [Test]
    public void BothSeats_GetDifferentTokens()
    {
        var (a, b) = Pair();
        Assert.IsTrue(LobbyProtocol.IsToken(TokenOf(a)));
        Assert.IsTrue(LobbyProtocol.IsToken(TokenOf(b)));
        Assert.AreNotEqual(TokenOf(a), TokenOf(b));
        Assert.AreEqual(2, _store.TokenCount);
    }

    [Test]
    public void Resume_WithinTheGrace_ReturnsToTheSameSeatWithTheCurrentState()
    {
        var (a, b) = Pair();
        VanishNetProtocol.TryDecodeSnapshot(a.Received.Last(m => m.StartsWith("S|")), out var state);
        var mover = state.CurrentPlayer == 0 ? a : b;
        mover.Send(VanishNetProtocol.EncodeMove(new VanishRules().GetLegalMoves(state)[0]));
        string current = a.Received.Last(m => m.StartsWith("S|"));

        string tokenB = TokenOf(b);
        b.Disconnect();
        Assert.AreEqual(LobbyMessageKind.OpponentAway, a.LastLobby!.Value.Kind);

        _now = _now.AddSeconds(60);
        var b2 = Client("B2");
        b2.Send(LobbyProtocol.Resume(tokenB));
        var resumed = b2.Lobby.Single(l => l.Kind == LobbyMessageKind.Resumed);
        Assert.AreEqual(1, resumed.Seat);
        Assert.IsTrue(resumed.OpponentPresent);
        Assert.AreEqual(current, b2.Received.Last(m => m.StartsWith("S|")), "今の局面が届く");
        Assert.AreEqual(LobbyMessageKind.OpponentBack, a.LastLobby!.Value.Kind, "相手に「戻った」と知らせる");
        Assert.IsNull(_store.PeekMatch()!.AwaySince[1]);

        // 戻った席から、そのまま手を打てる
        var match = new OnlineMatch(b2, 1);
        b2.Deliver(current);
        VanishNetProtocol.TryDecodeSnapshot(current, out var now);
        if (now.CurrentPlayer == 1)
        {
            Assert.IsTrue(match.SubmitMove(new VanishRules().GetLegalMoves(now)[0]));
            Assert.AreEqual(now.Ply + 1, match.State.Ply);
        }
    }

    [Test]
    public void Resume_AfterThePlayerLeftOnPurpose_IsExpired()
    {
        var (a, b) = Pair();
        string tokenA = TokenOf(a);
        a.Send(LobbyProtocol.Leave());
        var a2 = Client("A2");
        a2.Send(LobbyProtocol.Resume(tokenA));
        Assert.AreEqual(LobbyErrors.SessionExpired, a2.LastLobby!.Value.ErrorCode);
        Assert.AreEqual(0, _store.TokenCount, "抜けたら合言葉も消す");
    }

    [Test]
    public void Resume_WithAnUnknownToken_IsExpired()
    {
        var c = Client("C");
        c.Send(LobbyProtocol.Resume(new string('a', LobbyProtocol.TokenLength)));
        Assert.AreEqual(LobbyErrors.SessionExpired, c.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void Resume_ReplacesAStaleConnection()
    {
        // ブラウザ側は切れたと思ったが、サーバーにはまだ切断が届いていない
        var (a, b) = Pair();
        var b2 = Client("B2");
        b2.Send(LobbyProtocol.Resume(TokenOf(b)));
        Assert.AreEqual(LobbyMessageKind.Resumed, b2.Lobby.Single(l => l.Kind == LobbyMessageKind.Resumed).Kind);
        Assert.AreEqual("B2", _store.PeekMatch()!.Connections[1]);

        // あとから古い接続の切断が届いても、部屋や相手には影響しない
        int before = a.Received.Count;
        b.Disconnect();
        Assert.AreEqual(before, a.Received.Count);
        Assert.IsNull(_store.PeekMatch()!.AwaySince[1]);
    }

    [Test]
    public void Claim_BeforeTheGraceEnds_IsTooEarly()
    {
        var (a, b) = Pair();
        b.Disconnect();
        _now = _now.AddSeconds(89);
        a.Send(LobbyProtocol.Claim());
        Assert.AreEqual(LobbyErrors.TooEarly, a.LastLobby!.Value.ErrorCode);
        Assert.AreEqual(1, _store.MatchCount);
    }

    [Test]
    public void Claim_AfterTheGrace_WinsAndClosesTheRoom()
    {
        var (a, b) = Pair();
        string tokenB = TokenOf(b);
        b.Disconnect();
        _now = _now.AddSeconds(90);
        a.Send(LobbyProtocol.Claim());
        var won = a.LastLobby!.Value;
        Assert.AreEqual(LobbyMessageKind.Won, won.Kind);
        Assert.AreEqual(LobbyEndReasons.Abandon, won.Reason);
        Assert.AreEqual(0, _store.MatchCount);
        Assert.AreEqual(0, _store.ConnectionCount);
        Assert.AreEqual(0, _store.TokenCount);

        // 遅れて戻ってきても、もう対局は無い
        var b2 = Client("B2");
        b2.Send(LobbyProtocol.Resume(tokenB));
        Assert.AreEqual(LobbyErrors.SessionExpired, b2.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void Claim_WhileTheOpponentIsConnected_IsTooEarly()
    {
        var (a, b) = Pair();
        _now = _now.AddHours(1);
        a.Send(LobbyProtocol.Claim());
        Assert.AreEqual(LobbyErrors.TooEarly, a.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void ResumeLate_ButBeforeAnyClaim_IsStillAllowed()
    {
        var (a, b) = Pair();
        string tokenB = TokenOf(b);
        b.Disconnect();
        _now = _now.AddMinutes(10); // 相手が申請しなかったので、まだ戻れる
        var b2 = Client("B2");
        b2.Send(LobbyProtocol.Resume(tokenB));
        Assert.AreEqual(LobbyMessageKind.Resumed, b2.Lobby.Single(l => l.Kind == LobbyMessageKind.Resumed).Kind);
    }

    [Test]
    public void WhenTheReturningPlayersOpponentIsAway_TheyAreToldHowLongIsLeft()
    {
        var (a, b) = Pair();
        string tokenA = TokenOf(a);
        a.Disconnect();
        _now = _now.AddSeconds(10);
        b.Disconnect();
        _now = _now.AddSeconds(20);
        var a2 = Client("A2");
        a2.Send(LobbyProtocol.Resume(tokenA));
        var away = a2.Lobby.Last(l => l.Kind == LobbyMessageKind.OpponentAway);
        Assert.AreEqual(70, away.Seconds, "相手（B）が切れてから 20 秒たっている");
    }

    [Test]
    public void Disconnect_WhileWaitingForAnOpponent_StillLeaves()
    {
        var a = Client("A");
        a.Send(LobbyProtocol.Create("vanish"));
        a.Disconnect();
        Assert.AreEqual(0, _store.MatchCount);
        Assert.AreEqual(0, _store.TokenCount);
    }

    [Test]
    public void Disconnect_AfterTheGameEnded_StillLeaves()
    {
        var (a, b) = Pair();
        var ma = new OnlineMatch(a, 0);
        var mb = new OnlineMatch(b, 1);
        a.Deliver(a.Received.Last(m => m.StartsWith("S|")));
        b.Deliver(b.Received.Last(m => m.StartsWith("S|")));
        var rules = new VanishRules();
        while (!ma.State.Result.IsOver)
        {
            var mover = ma.State.CurrentPlayer == 0 ? ma : mb;
            mover.SubmitMove(rules.GetLegalMoves(mover.State)[0]);
        }
        b.Disconnect();
        Assert.AreEqual(LobbyMessageKind.OpponentLeft, a.LastLobby!.Value.Kind);
        Assert.AreEqual(0, _store.MatchCount);
    }
}

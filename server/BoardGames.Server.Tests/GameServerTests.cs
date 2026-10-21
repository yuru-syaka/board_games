using BoardGames.Net;
using BoardGames.Server.Games;
using BoardGames.Vanish;
using BoardGames.Vanish.Online;
using NUnit.Framework;

namespace BoardGames.Server.Tests;

/// <summary>
/// サーバーの中身（GameServer）を、AWS の代わりにメモリ上の保存先・送信先で動かして確かめる。
/// ブラウザ側は Unity と同じ OnlineMatch（サーバー対戦のゲスト）を使う。
/// </summary>
public class GameServerTests
{
    InMemoryStore _store = null!;
    FakePush _push = null!;
    GameServer _server = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryStore();
        _push = new FakePush();
        _server = new GameServer(_store, _push, GameRegistry.Default(), new Random(1234));
    }

    FakeClient Client(string id) => new FakeClient(_server, _push, id);

    /// <summary>2 人をランダムマッチで組ませる（a が席 0、b が席 1）。</summary>
    (FakeClient a, FakeClient b) QuickPair()
    {
        var a = Client("A");
        var b = Client("B");
        a.Send(LobbyProtocol.Quick("vanish"));
        b.Send(LobbyProtocol.Quick("vanish"));
        return (a, b);
    }

    static OnlineMatch MatchFor(FakeClient client) => new OnlineMatch(client, client.Lobby.First(l => l.Kind == LobbyMessageKind.Joined).Seat);

    // ---------- ロビー ----------

    [Test]
    public void QuickMatch_PairsTwoPlayersAndStartsTheGame()
    {
        var a = Client("A");
        a.Send(LobbyProtocol.Quick("vanish"));
        Assert.AreEqual(LobbyMessageKind.Joined, a.LastLobby!.Value.Kind);
        Assert.AreEqual(0, a.LastLobby.Value.Seat);
        Assert.IsFalse(a.LastLobby.Value.OpponentPresent);
        Assert.IsTrue(_store.HasQueue("vanish"));

        var b = Client("B");
        b.Send(LobbyProtocol.Quick("vanish"));
        var joined = b.Lobby.Single();
        Assert.AreEqual(1, joined.Seat);
        Assert.IsTrue(joined.OpponentPresent);
        Assert.AreEqual(LobbyMessageKind.OpponentJoined, a.LastLobby!.Value.Kind);
        Assert.IsFalse(_store.HasQueue("vanish"));

        // 両者に同じ初期局面（完全情報）
        var snapA = a.Received.Last();
        var snapB = b.Received.Last();
        Assert.AreEqual(snapA, snapB);
        Assert.IsTrue(VanishNetProtocol.TryDecodeSnapshot(snapA, out var state));
        Assert.AreEqual(0, state.Ply);
    }

    [Test]
    public void FullGame_BothClientsSeeTheSameStatesUntilTheEnd()
    {
        var (a, b) = QuickPair();
        var ma = MatchFor(a);
        var mb = MatchFor(b);
        // OnlineMatch は作ったあとに届いた局面から追うので、初期局面をもう一度もらうために作り直す代わりに直接入れる
        a.Deliver(a.Received.Last(l => l.StartsWith("S|")));
        b.Deliver(b.Received.Last(l => l.StartsWith("S|")));
        Assert.AreEqual(0, ma.LocalPlayer);
        Assert.AreEqual(1, mb.LocalPlayer);

        var rules = new VanishRules();
        var rng = new Random(7);
        int guard = 0;
        while (!ma.State.Result.IsOver && guard++ < 200)
        {
            var mover = ma.State.CurrentPlayer == 0 ? ma : mb;
            var other = mover == ma ? mb : ma;
            Assert.IsFalse(other.SubmitMove(rules.GetLegalMoves(other.State)[0]), "相手の番には打てない");
            var moves = rules.GetLegalMoves(mover.State);
            Assert.IsTrue(mover.SubmitMove(moves[rng.Next(moves.Count)]));
            Assert.AreEqual(VanishNetProtocol.EncodeSnapshot(ma.State), VanishNetProtocol.EncodeSnapshot(mb.State));
        }
        Assert.IsTrue(ma.State.Result.IsOver);
        Assert.IsTrue(mb.State.Result.IsOver);
    }

    [Test]
    public void CodeRoom_CreateJoinAndCodeIsReleased()
    {
        var a = Client("A");
        a.Send(LobbyProtocol.Create("vanish"));
        var created = a.LastLobby!.Value;
        Assert.AreEqual(LobbyMessageKind.Joined, created.Kind);
        Assert.IsTrue(LobbyProtocol.IsRoomCode(created.Code));
        Assert.IsTrue(_store.HasCode("vanish", created.Code));
        Assert.IsFalse(_store.HasQueue("vanish"), "ルームコードの部屋はランダムマッチに出さない");

        var b = Client("B");
        b.Send(LobbyProtocol.Join("vanish", created.Code));
        Assert.AreEqual(1, b.Lobby.Single().Seat);
        Assert.AreEqual(created.Code, b.Lobby.Single().Code);
        Assert.IsTrue(b.Received.Last().StartsWith("S|"));
        Assert.IsFalse(_store.HasCode("vanish", created.Code), "そろったらコードを返す");

        var c = Client("C");
        c.Send(LobbyProtocol.Join("vanish", created.Code));
        Assert.AreEqual(LobbyErrors.RoomNotFound, c.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void Join_UnknownCode_IsRoomNotFound()
    {
        var a = Client("A");
        a.Send(LobbyProtocol.Join("vanish", "0000"));
        Assert.AreEqual(LobbyErrors.RoomNotFound, a.LastLobby!.Value.ErrorCode);
    }

    [Test]
    public void UnknownGameAndBrokenMessages_AreRejected()
    {
        var a = Client("A");
        a.Send(LobbyProtocol.Quick("chess"));
        Assert.AreEqual(LobbyErrors.UnknownGame, a.LastLobby!.Value.ErrorCode);
        a.Send("@quick|Vanish!");
        Assert.AreEqual(LobbyErrors.BadRequest, a.LastLobby!.Value.ErrorCode);
        a.Send("M|1|4"); // 対局に入っていない手は無視
        Assert.AreEqual(0, _store.MatchCount);
    }

    [Test]
    public void Ping_IsAnsweredWithPong()
    {
        var a = Client("A");
        a.Send(LobbyProtocol.Ping());
        Assert.AreEqual(LobbyMessageKind.Pong, a.LastLobby!.Value.Kind);
    }

    // ---------- 対局 ----------

    [Test]
    public void IllegalOrOutOfTurnMove_ResendsTheCurrentStateOnlyToTheSender()
    {
        var (a, b) = QuickPair();
        VanishNetProtocol.TryDecodeSnapshot(a.Received.Last(), out var state);
        var waiting = state.CurrentPlayer == 0 ? b : a;
        var moving = waiting == a ? b : a;
        int before = moving.Received.Count;

        waiting.Send("M|1|4");
        Assert.AreEqual(VanishNetProtocol.EncodeSnapshot(state), waiting.Received.Last(), "今の局面を送り直す");
        Assert.AreEqual(before, moving.Received.Count, "相手には何も送らない");

        waiting.Send("M|1|99"); // 盤の外
        Assert.AreEqual(VanishNetProtocol.EncodeSnapshot(state), waiting.Received.Last());
    }

    [Test]
    public void Move_RetriesWhenAnotherWriteWinsTheRace()
    {
        var (a, b) = QuickPair();
        VanishNetProtocol.TryDecodeSnapshot(a.Received.Last(), out var state);
        var moving = state.CurrentPlayer == 0 ? a : b;

        _store.FailNextMatchWrites = 2;
        moving.Send(VanishNetProtocol.EncodeMove(new VanishRules().GetLegalMoves(state)[0]));
        Assert.IsTrue(VanishNetProtocol.TryDecodeSnapshot(a.Received.Last(), out var after));
        Assert.AreEqual(1, after.Ply);
    }

    [Test]
    public void Rematch_StartsANewGameWhenBothAsk()
    {
        var (a, b) = QuickPair();
        var ma = MatchFor(a);
        var mb = MatchFor(b);
        a.Deliver(a.Received.Last(l => l.StartsWith("S|")));
        b.Deliver(b.Received.Last(l => l.StartsWith("S|")));
        var rules = new VanishRules();
        while (!ma.State.Result.IsOver)
        {
            var mover = ma.State.CurrentPlayer == 0 ? ma : mb;
            mover.SubmitMove(rules.GetLegalMoves(mover.State)[0]);
        }

        ma.RequestRematch();
        Assert.IsTrue(ma.LocalWantsRematch);
        Assert.IsTrue(mb.OpponentWantsRematch, "相手に再戦の希望が届く");
        Assert.IsTrue(ma.State.Result.IsOver, "片方だけでは始まらない");

        mb.RequestRematch();
        Assert.AreEqual(0, ma.State.Ply);
        Assert.AreEqual(0, mb.State.Ply);
        Assert.IsFalse(ma.LocalWantsRematch || ma.OpponentWantsRematch || mb.LocalWantsRematch || mb.OpponentWantsRematch);
    }

    [Test]
    public void Rematch_DuringAGame_IsIgnored()
    {
        var (a, b) = QuickPair();
        int before = b.Received.Count;
        a.Send("R|1");
        Assert.AreEqual(before, b.Received.Count);
    }

    // ---------- 退出・切断 ----------

    [Test]
    public void Leave_TellsTheOpponentAndClosesTheRoom()
    {
        var (a, b) = QuickPair();
        a.Send(LobbyProtocol.Leave());
        Assert.AreEqual(LobbyMessageKind.OpponentLeft, b.LastLobby!.Value.Kind);
        Assert.AreEqual(0, _store.MatchCount);
        Assert.AreEqual(0, _store.ConnectionCount);

        b.Send("M|1|4"); // もう対局はない
        Assert.AreEqual(LobbyMessageKind.OpponentLeft, b.LastLobby!.Value.Kind);
    }

    [Test]
    public void Disconnect_WhileWaiting_RemovesTheQueueEntry()
    {
        var a = Client("A");
        a.Send(LobbyProtocol.Quick("vanish"));
        a.Disconnect();
        Assert.IsFalse(_store.HasQueue("vanish"));
        Assert.AreEqual(0, _store.MatchCount);

        // 次に来た人は、切れた人と組まされずに待つ側になる
        var b = Client("B");
        b.Send(LobbyProtocol.Quick("vanish"));
        Assert.AreEqual(0, b.LastLobby!.Value.Seat);
        Assert.IsFalse(b.LastLobby.Value.OpponentPresent);
    }

    [Test]
    public void Disconnect_DuringAGame_TellsTheOpponent()
    {
        var (a, b) = QuickPair();
        b.Disconnect();
        Assert.AreEqual(LobbyMessageKind.OpponentLeft, a.LastLobby!.Value.Kind);
        Assert.AreEqual(0, _store.MatchCount);
    }

    [Test]
    public void StartingANewSearch_LeavesTheCurrentRoom()
    {
        var a = Client("A");
        a.Send(LobbyProtocol.Create("vanish"));
        var code = a.LastLobby!.Value.Code;
        a.Send(LobbyProtocol.Quick("vanish"));
        Assert.IsFalse(_store.HasCode("vanish", code), "前の部屋のコードは返す");
        Assert.AreEqual(1, _store.MatchCount);
    }

    // ---------- ルール ----------

    [Test]
    public void SameSeed_GivesTheSameStart()
    {
        var engine = new VanishEngine();
        for (int seed = 0; seed < 20; seed++)
            Assert.AreEqual(engine.Start(seed).SnapshotFor(0), engine.Start(seed).SnapshotFor(0));
    }
}

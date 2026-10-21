using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BoardGames.Net.Tests
{
    /// <summary>テスト用の WebSocket。送ったものを記録し、つながった・届いた・切れたをテストから起こす。</summary>
    sealed class FakeSocket : ITextSocket
    {
        readonly Queue<Action> _pending = new Queue<Action>();

        public SocketState State { get; private set; } = SocketState.Closed;
        public List<string> Sent { get; } = new List<string>();
        public List<string> ConnectedUrls { get; } = new List<string>();

        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> Closed;

        public void Connect(string url)
        {
            ConnectedUrls.Add(url);
            State = SocketState.Connecting;
        }

        public bool Send(string message)
        {
            if (State != SocketState.Open) return false;
            Sent.Add(message);
            return true;
        }

        public void Close() => DropConnection("closed");

        public void Dispose() { }

        public void Update()
        {
            while (_pending.Count > 0) _pending.Dequeue()();
        }

        // ---------- テストから起こす出来事（次の Update で通知される） ----------

        public void Open() => _pending.Enqueue(() =>
        {
            State = SocketState.Open;
            Opened?.Invoke();
        });

        public void Receive(string message) => _pending.Enqueue(() => MessageReceived?.Invoke(message));

        public void DropConnection(string reason) => _pending.Enqueue(() =>
        {
            State = SocketState.Closed;
            Closed?.Invoke(reason);
        });
    }

    public class ServerConnectionTests
    {
        FakeSocket _socket;
        ServerConnection _server;
        List<string> _errors;
        List<string> _gameMessages;
        double _now;

        [SetUp]
        public void SetUp()
        {
            _socket = new FakeSocket();
            _server = new ServerConnection("vanish", "wss://example.test", _socket);
            _errors = new List<string>();
            _gameMessages = new List<string>();
            _server.Error += _errors.Add;
            _server.GameMessage += _gameMessages.Add;
            _now = 0;
        }

        void Tick(double seconds = 0.1)
        {
            _now += seconds;
            _server.Update(_now);
        }

        void OpenLobby()
        {
            _server.Connect();
            _socket.Open();
            Tick();
        }

        [Test]
        public void Connect_GoesToLobbyWhenTheSocketOpens()
        {
            _server.Connect();
            Assert.AreEqual(ServerState.Connecting, _server.State);
            Assert.AreEqual("wss://example.test", _socket.ConnectedUrls.Single());
            _socket.Open();
            Tick();
            Assert.AreEqual(ServerState.Lobby, _server.State);
        }

        [Test]
        public void JoinRandom_BeforeConnecting_ConnectsFirstThenSendsQuick()
        {
            _server.JoinRandom();
            Assert.AreEqual(ServerState.Connecting, _server.State);
            Assert.IsEmpty(_socket.Sent);
            _socket.Open();
            Tick();
            Assert.AreEqual(ServerState.Matching, _server.State);
            Assert.AreEqual("@quick|vanish", _socket.Sent.Single());
        }

        [Test]
        public void Joined_SetsSeatCodeAndRoomState()
        {
            OpenLobby();
            int joined = 0;
            _server.JoinedRoom += () => joined++;
            _server.CreateCodeRoom();
            Assert.AreEqual("@create|vanish", _socket.Sent.Last());
            _socket.Receive("@joined|0|1234|0");
            Tick();
            Assert.AreEqual(1, joined);
            Assert.AreEqual(ServerState.InRoom, _server.State);
            Assert.AreEqual(0, _server.LocalSeat);
            Assert.AreEqual("1234", _server.RoomCode);
            Assert.IsFalse(_server.OpponentPresent);

            int opp = 0;
            _server.OpponentJoined += () => opp++;
            _socket.Receive("@opp");
            Tick();
            Assert.AreEqual(1, opp);
            Assert.IsTrue(_server.OpponentPresent);
        }

        [Test]
        public void GameMessages_PassThroughOnlyWhileInRoom()
        {
            OpenLobby();
            _server.SendGameMessage("M|1|4");
            Assert.IsEmpty(_socket.Sent, "部屋にいないときは送らない");

            _server.JoinRandom();
            _socket.Receive("@joined|1|-|1");
            _socket.Receive("S|1|0|0|0|-1|0|-|-1|-|-|-");
            Tick();
            Assert.AreEqual("S|1|0|0|0|-1|0|-|-1|-|-|-", _gameMessages.Single());
            _server.SendGameMessage("M|1|4");
            Assert.AreEqual("M|1|4", _socket.Sent.Last());
        }

        [Test]
        public void JoinCodeRoom_RejectsBadCodesWithoutSending()
        {
            OpenLobby();
            _server.JoinCodeRoom("12a4");
            Assert.AreEqual("ルームコードは数字 4 桁です。", _errors.Single());
            Assert.IsEmpty(_socket.Sent);
            _server.JoinCodeRoom("0042");
            Assert.AreEqual("@join|vanish|0042", _socket.Sent.Single());
        }

        [Test]
        public void ServerErrors_AreShownInJapaneseAndReturnToLobby()
        {
            OpenLobby();
            _server.JoinCodeRoom("9999");
            _socket.Receive("@error|room_not_found");
            Tick();
            Assert.AreEqual(ServerState.Lobby, _server.State);
            Assert.AreEqual("ルームが見つかりません。コードを確かめてください。", _errors.Single());
        }

        [Test]
        public void OpponentLeft_ReturnsToLobby()
        {
            OpenLobby();
            _server.JoinRandom();
            _socket.Receive("@joined|0|-|1");
            Tick();
            int left = 0;
            _server.OpponentLeft += () => left++;
            _socket.Receive("@left");
            Tick();
            Assert.AreEqual(1, left);
            Assert.AreEqual(ServerState.Lobby, _server.State);
            Assert.IsFalse(_server.OpponentPresent);
        }

        [Test]
        public void LeaveRoom_TellsTheServer()
        {
            OpenLobby();
            _server.JoinRandom();
            _socket.Receive("@joined|0|-|0");
            Tick();
            _server.LeaveRoom();
            Assert.AreEqual("@leave", _socket.Sent.Last());
            Assert.AreEqual(ServerState.Lobby, _server.State);
        }

        [Test]
        public void Ping_IsSentAfterFiveQuietMinutes()
        {
            OpenLobby();
            Tick(ServerConnection.PingInterval - 1);
            Assert.IsEmpty(_socket.Sent);
            Tick(2);
            Assert.AreEqual("@ping", _socket.Sent.Single());
            Tick(ServerConnection.PingInterval - 1);
            Assert.AreEqual(1, _socket.Sent.Count, "送ったばかりなので、次はまた 5 分後");
        }

        [Test]
        public void ConnectionDrop_InRoom_ShowsAnError()
        {
            OpenLobby();
            _server.JoinRandom();
            _socket.Receive("@joined|0|-|1");
            Tick();
            _socket.DropConnection("1006");
            Tick();
            Assert.AreEqual(ServerState.Offline, _server.State);
            Assert.AreEqual("通信が切断されました。", _errors.Single());
        }

        [Test]
        public void ConnectionFailure_WhileConnecting_ShowsAnError()
        {
            _server.Connect();
            _socket.DropConnection("refused");
            Tick();
            Assert.AreEqual(ServerState.Offline, _server.State);
            Assert.AreEqual("サーバーに接続できませんでした。", _errors.Single());
        }
    }
}

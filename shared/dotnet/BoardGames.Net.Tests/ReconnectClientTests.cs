using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BoardGames.Net.Tests
{
    /// <summary>通信が切れたとき（ステップ 4-2）のブラウザ側: つなぎ直し、再読み込みからの復帰、相手の離席、勝ちの申請。</summary>
    public class ReconnectClientTests
    {
        const string TokenA = "0123456789abcdef0123456789abcdef";

        FakeSocket _socket;
        MemorySessionStore _sessions;
        ServerConnection _server;
        List<string> _errors;
        double _now;

        [SetUp]
        public void SetUp()
        {
            _sessions = new MemorySessionStore();
            _now = 0;
            (_socket, _server) = NewConnection();
        }

        (FakeSocket, ServerConnection) NewConnection()
        {
            var socket = new FakeSocket();
            var server = new ServerConnection("vanish", "wss://example.test", socket, _sessions);
            _errors = new List<string>();
            server.Error += _errors.Add;
            return (socket, server);
        }

        void Tick(double seconds = 0.1)
        {
            _now += seconds;
            _server.Update(_now);
        }

        /// <summary>ランダムマッチで部屋に入り、合言葉を受け取ったところまで進める。</summary>
        void JoinWithToken()
        {
            _server.JoinRandom();
            _socket.Open();
            Tick();
            _socket.Receive("@joined|0|-|1");
            _socket.Receive("@token|" + TokenA);
            Tick();
        }

        [Test]
        public void Token_IsRememberedInTheTab()
        {
            JoinWithToken();
            Assert.AreEqual(TokenA, _sessions.Load("boardpia.session.vanish"));
            Assert.IsTrue(_server.HasSavedSession);
        }

        [Test]
        public void ConnectionDrop_InRoom_ReconnectsAndResumes()
        {
            JoinWithToken();
            int lost = 0, back = 0;
            _server.ConnectionLost += () => lost++;
            _server.Reconnected += () => back++;

            _socket.DropConnection("1006");
            Tick();
            Assert.AreEqual(1, lost);
            Assert.IsTrue(_server.IsReconnecting);
            Assert.AreEqual(ServerState.InRoom, _server.State, "部屋にいるまま");
            Assert.IsEmpty(_errors, "まだ対局は終わらない");
            Assert.AreEqual(1, _socket.ConnectedUrls.Count);

            Tick(1.1); // 1 秒あけてからつなぎ直す
            Assert.AreEqual(2, _socket.ConnectedUrls.Count);
            _socket.Open();
            Tick();
            Assert.AreEqual("@resume|" + TokenA, _socket.Sent.Last());

            _socket.Receive("@resumed|0|-|1");
            Tick();
            Assert.AreEqual(1, back);
            Assert.IsFalse(_server.IsReconnecting);
            Assert.AreEqual(ServerState.InRoom, _server.State);
        }

        [Test]
        public void Reconnect_BacksOffAndGivesUpAfterAWhile()
        {
            JoinWithToken();
            _socket.DropConnection("1006");
            Tick();
            // つながらないまま、つなぎ直しを繰り返す
            for (int i = 0; i < 40 && _server.IsReconnecting; i++)
            {
                Tick(1);
                if (_socket.State == SocketState.Connecting) _socket.DropConnection("refused");
                Tick(0.1);
            }
            Assert.Less(_socket.ConnectedUrls.Count, 15, "間隔をあけてつなぎ直す");
            Tick(ServerConnection.ReconnectGiveUp);
            if (_socket.State == SocketState.Connecting)
            {
                _socket.DropConnection("refused");
                Tick();
                Tick(10);
            }
            Assert.IsFalse(_server.IsReconnecting);
            Assert.AreEqual(ServerState.Offline, _server.State);
            Assert.AreEqual("通信が切断されました。", _errors.Single());
            Assert.IsFalse(_server.HasSavedSession);
        }

        [Test]
        public void Reconnect_WhenTheMatchIsGone_EndsTheMatch()
        {
            JoinWithToken();
            _socket.DropConnection("1006");
            Tick();
            Tick(1.1);
            _socket.Open();
            Tick();
            _socket.Receive("@error|session_expired");
            Tick();
            Assert.IsFalse(_server.IsReconnecting);
            Assert.AreEqual(ServerState.Lobby, _server.State);
            StringAssert.StartsWith("対局は終了しました。", _errors.Single());
        }

        [Test]
        public void AfterAReload_ConnectResumesTheSavedMatch()
        {
            JoinWithToken();
            // ページを再読み込みした: 新しい接続を作る（タブの中の合言葉は残っている）
            (_socket, _server) = NewConnection();
            Assert.IsTrue(_server.HasSavedSession);
            bool resumingSeen = false;
            _server.JoinedRoom += () => resumingSeen = _server.IsResuming;

            _server.Connect();
            _socket.Open();
            Tick();
            Assert.AreEqual("@resume|" + TokenA, _socket.Sent.Single());
            _socket.Receive("@resumed|0|-|1");
            Tick();
            Assert.IsTrue(resumingSeen, "画面には「再読み込み前の対局に戻った」と知らせる");
            Assert.IsFalse(_server.IsResuming);
            Assert.AreEqual(ServerState.InRoom, _server.State);
        }

        [Test]
        public void AfterAReload_AnExpiredMatchQuietlyGoesToTheLobbyAndContinues()
        {
            JoinWithToken();
            (_socket, _server) = NewConnection();
            _server.JoinRandom(); // 再読み込み後、すぐにランダムマッチを押した
            _socket.Open();
            Tick();
            Assert.AreEqual("@resume|" + TokenA, _socket.Sent.Single());
            _socket.Receive("@error|session_expired");
            Tick();
            Assert.IsEmpty(_errors, "終わっていた対局のことは言わない");
            Assert.AreEqual("@quick|vanish", _socket.Sent.Last(), "押していた操作を続ける");
            Assert.IsFalse(_server.HasSavedSession);
        }

        [Test]
        public void OpponentAwayAndBack_AreReported()
        {
            JoinWithToken();
            int awaySeconds = -1, back = 0;
            _server.OpponentAway += s => awaySeconds = s;
            _server.OpponentBack += () => back++;
            _socket.Receive("@away|90");
            Tick();
            Assert.AreEqual(90, awaySeconds);
            _socket.Receive("@back");
            Tick();
            Assert.AreEqual(1, back);
        }

        [Test]
        public void Claim_SendsAndAWinEndsTheMatch()
        {
            JoinWithToken();
            _server.Claim();
            Assert.AreEqual("@claim", _socket.Sent.Last());
            _socket.Receive("@error|too_early");
            Tick();
            Assert.IsEmpty(_errors, "早すぎた申請は、黙って待ち続ける");

            _socket.Receive("@won|abandon");
            Tick();
            Assert.AreEqual("相手が戻らなかったため、あなたの勝ちです。", _errors.Single());
            Assert.AreEqual(ServerState.Lobby, _server.State);
            Assert.IsFalse(_server.HasSavedSession);
        }

        [Test]
        public void LeavingOnPurpose_ForgetsTheTokenAndDoesNotReconnect()
        {
            JoinWithToken();
            _server.LeaveRoom();
            Assert.IsFalse(_server.HasSavedSession);
            _socket.DropConnection("1000");
            Tick();
            Tick(5);
            Assert.IsFalse(_server.IsReconnecting);
            Assert.AreEqual(1, _socket.ConnectedUrls.Count);
        }

        [Test]
        public void UnknownLobbyMessages_AreIgnored()
        {
            JoinWithToken();
            _socket.Receive("@something-new|1");
            Tick();
            Assert.IsEmpty(_errors);
            Assert.AreEqual(ServerState.InRoom, _server.State);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BoardGames.Net.Tests
{
    /// <summary>テスト用の ID トークンの受け取り先。Resolve を呼ぶまで「まだ」のまま。</summary>
    sealed class FakeTokenSource : IIdTokenSource
    {
        bool _requested;
        bool _ready;
        string _token;

        public int Requests { get; private set; }

        public void Request()
        {
            Requests++;
            _requested = true;
            _ready = false;
        }

        /// <summary>頼まれていた結果を出す(null = ログインしていない)。</summary>
        public void Resolve(string token)
        {
            if (!_requested) return;
            _ready = true;
            _token = token;
        }

        public bool TryTake(out string token)
        {
            token = null;
            if (!_ready) return false;
            _ready = _requested = false;
            token = _token;
            return true;
        }
    }

    /// <summary>ログインしてからマッチングに進む流れ(ステップ 5-3)と、名前・レートのお知らせ。</summary>
    public class AuthClientTests
    {
        const string IdToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ1In0.c2lnbmF0dXJl";

        FakeSocket _socket;
        FakeTokenSource _tokens;
        ServerConnection _server;
        List<string> _errors;
        double _now;

        [SetUp]
        public void SetUp()
        {
            _socket = new FakeSocket();
            _tokens = new FakeTokenSource();
            _server = new ServerConnection("vanish", "wss://example.test", _socket, new MemorySessionStore(), _tokens);
            _errors = new List<string>();
            _server.Error += _errors.Add;
            _now = 0;
        }

        void Tick(double seconds = 0.1)
        {
            _now += seconds;
            _server.Update(_now);
        }

        [Test]
        public void SignedIn_SendsAuthAndWaitsForTheReplyBeforeMatching()
        {
            _server.JoinRandom();
            _socket.Open();
            Tick();
            Assert.AreEqual(1, _tokens.Requests);
            Assert.AreEqual(ServerState.Connecting, _server.State, "トークンを待つ間はロビーに入らない");
            Assert.IsEmpty(_socket.Sent);

            _tokens.Resolve(IdToken);
            Tick();
            Assert.AreEqual("@auth|" + IdToken, _socket.Sent.Single());
            Assert.AreEqual(ServerState.Connecting, _server.State, "返事を待つ間もまだ");

            _socket.Receive(LobbyProtocol.Authed("アリス"));
            Tick();
            Assert.IsTrue(_server.SignedIn);
            Assert.AreEqual("アリス", _server.DisplayName);
            Assert.AreEqual(ServerState.Matching, _server.State);
            Assert.AreEqual("@quick|vanish", _socket.Sent.Last());
        }

        [Test]
        public void NotSignedIn_GoesStraightToTheLobby()
        {
            _server.JoinRandom();
            _socket.Open();
            Tick();
            _tokens.Resolve(null);
            Tick();
            Assert.IsFalse(_server.SignedIn);
            Assert.AreEqual(new[] { "@quick|vanish" }, _socket.Sent.ToArray());
        }

        [Test]
        public void AuthFailed_PlaysAsGuestWithoutShowingAnError()
        {
            _server.JoinRandom();
            _socket.Open();
            Tick();
            _tokens.Resolve(IdToken);
            Tick();
            _socket.Receive(LobbyProtocol.Error(LobbyErrors.AuthFailed));
            Tick();
            Assert.IsFalse(_server.SignedIn);
            Assert.IsEmpty(_errors);
            Assert.AreEqual("@quick|vanish", _socket.Sent.Last());
        }

        [Test]
        public void NoToken_GivesUpAfterTheTimeout()
        {
            _server.JoinRandom();
            _socket.Open();
            Tick();
            Tick(ServerConnection.AuthTimeout - 0.5);
            Assert.AreEqual(ServerState.Connecting, _server.State);
            Tick(1);
            Assert.AreEqual(new[] { "@quick|vanish" }, _socket.Sent.ToArray());
        }

        [Test]
        public void NoReply_GivesUpAfterTheTimeout()
        {
            _server.JoinRandom();
            _socket.Open();
            Tick();
            _tokens.Resolve(IdToken);
            Tick();
            Tick(ServerConnection.AuthTimeout + 0.5);
            Assert.AreEqual("@quick|vanish", _socket.Sent.Last());
            Assert.IsFalse(_server.SignedIn);
        }

        [Test]
        public void Reconnect_ResumesAtOnceAndSignsInAgain()
        {
            // 部屋に入る
            _server.JoinRandom();
            _socket.Open();
            Tick();
            _tokens.Resolve(null);
            Tick();
            _socket.Receive("@joined|0|-|1");
            _socket.Receive("@token|" + new string('a', 32));
            Tick();

            // 通信が切れ、つなぎ直す: 合言葉はトークンを待たずにすぐ送る
            _socket.DropConnection("lost");
            Tick();
            Tick(1.1);
            _socket.Open();
            Tick();
            Assert.AreEqual("@resume|" + new string('a', 32), _socket.Sent.Last());
            Assert.AreEqual(2, _tokens.Requests);
            _tokens.Resolve(IdToken);
            Tick();
            Assert.AreEqual("@auth|" + IdToken, _socket.Sent.Last());
        }

        [Test]
        public void PlayersAndRated_AreForwarded()
        {
            string[] names = null;
            int[] ratings = null;
            (int, int)? rated = null;
            _server.PlayersUpdated += (n, r) => { names = n; ratings = r; };
            _server.RatingChanged += (r, d) => rated = (r, d);
            _server.Connect();
            _socket.Open();
            Tick();

            _socket.Receive(LobbyProtocol.Players("アリス", 1516, null, -1));
            _socket.Receive(LobbyProtocol.Rated(1500, -16));
            Tick();
            CollectionAssert.AreEqual(new[] { "アリス", null }, names);
            CollectionAssert.AreEqual(new[] { 1516, -1 }, ratings);
            Assert.AreEqual((1500, -16), rated);
        }

        // ---------- 帯に出す文 ----------

        [Test]
        public void MatchStartText()
        {
            Assert.AreEqual("対戦相手: ボブ（1480）\nレート戦です（あなた 1516）",
                RatingText.MatchStart(new[] { "アリス", "ボブ" }, new[] { 1516, 1480 }, 0, roomCodeMatch: false));
            Assert.AreEqual("対戦相手: プレイヤー（1500）\nルームコードの対戦は、レートが動きません",
                RatingText.MatchStart(new string[] { null, null }, new[] { 1500, 1500 }, 1, roomCodeMatch: true));
            Assert.AreEqual("対戦相手: ゲスト\n相手がログインしていないため、レートは動きません",
                RatingText.MatchStart(new string[] { "アリス", null }, new[] { 1500, -1 }, 0, roomCodeMatch: false));
            Assert.AreEqual("対戦相手: アリス（1500）",
                RatingText.MatchStart(new string[] { "アリス", null }, new[] { 1500, -1 }, 1, roomCodeMatch: false));
            Assert.IsNull(RatingText.MatchStart(new string[] { null, null }, new[] { -1, -1 }, 0, roomCodeMatch: false));
        }

        [TestCase(1516, 16, "レート 1516（+16）")]
        [TestCase(1484, -16, "レート 1484（-16）")]
        [TestCase(1500, 0, "レート 1500（±0）")]
        public void RatedText(int rating, int delta, string expected) => Assert.AreEqual(expected, RatingText.Rated(rating, delta));
    }
}

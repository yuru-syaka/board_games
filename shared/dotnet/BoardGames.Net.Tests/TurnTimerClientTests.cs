using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BoardGames.Net.Tests
{
    /// <summary>1 手の制限時間（ステップ 4-3）のブラウザ側。</summary>
    public class TurnTimerClientTests
    {
        FakeSocket _socket;
        ServerConnection _server;
        List<string> _errors;
        List<(int seat, int seconds)> _turns;
        double _now;

        [SetUp]
        public void SetUp()
        {
            _socket = new FakeSocket();
            _server = new ServerConnection("vanish", "wss://example.test", _socket, new MemorySessionStore());
            _errors = new List<string>();
            _turns = new List<(int, int)>();
            _server.Error += _errors.Add;
            _server.TurnTimer += (seat, seconds) => _turns.Add((seat, seconds));
            _server.JoinRandom();
            _socket.Open();
            Tick();
            _socket.Receive("@joined|1|-|1");
            _socket.Receive("@token|0123456789abcdef0123456789abcdef");
            Tick();
        }

        void Tick()
        {
            _now += 0.1;
            _server.Update(_now);
        }

        [Test]
        public void TurnMessages_AreReported()
        {
            _socket.Receive("@turn|0|90");
            _socket.Receive("@turn|1|42");
            _socket.Receive("@turn|-|0");
            Tick();
            CollectionAssert.AreEqual(new[] { (0, 90), (1, 42), (-1, 0) }, _turns);
        }

        [Test]
        public void LosingOnTime_EndsTheMatch()
        {
            _socket.Receive("@lost|timeout");
            Tick();
            Assert.AreEqual("時間切れのため、あなたの負けです。", _errors.Single());
            Assert.AreEqual(ServerState.Lobby, _server.State);
            Assert.IsFalse(_server.HasSavedSession);
        }

        [Test]
        public void WinningOnTime_EndsTheMatch()
        {
            _server.Claim();
            Assert.AreEqual("@claim", _socket.Sent.Last());
            _socket.Receive("@won|timeout");
            Tick();
            Assert.AreEqual("相手が時間切れになったため、あなたの勝ちです。", _errors.Single());
            Assert.AreEqual(ServerState.Lobby, _server.State);
        }
    }
}

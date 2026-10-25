using System.Collections.Generic;
using NUnit.Framework;

namespace BoardGames.Net.Tests
{
    /// <summary>多人数の部屋(ゴースト・トレンド)のメッセージと、ServerConnection の受け取り方。2 人用の形が変わらないことも確かめる。</summary>
    public class TableClientTests
    {
        // ---------- メッセージの形 ----------

        [Test]
        public void TwoSeatMessages_KeepTheirFormat()
        {
            Assert.AreEqual("@players|a|1500||-", LobbyProtocol.Players("a", 1500, null, -1));
            Assert.AreEqual(LobbyProtocol.Players("a", 1500, null, -1), LobbyProtocol.Players(new[] { "a", null }, new[] { 1500, -1 }),
                "2 人分なら、多人数の形も 2 人用と同じ文字列");
            Assert.AreEqual("@away|90", LobbyProtocol.OpponentAway(90));
            Assert.AreEqual("@turn|1|30", LobbyProtocol.Turn(1, 30));
            Assert.AreEqual("@joined|0|-|0", LobbyProtocol.Joined(0, null, false));

            Assert.IsTrue(LobbyProtocol.TryParse("@away|90", out var away));
            Assert.AreEqual(LobbyMessageKind.OpponentAway, away.Kind);
            Assert.AreEqual(-1, away.Seat, "席の無い 2 人用の形");
            Assert.IsTrue(LobbyProtocol.TryParse("@players|a|1500||-", out var players));
            CollectionAssert.AreEqual(new[] { "a", null }, players.Names);
        }

        [Test]
        public void TableMessages_RoundTrip()
        {
            Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Start(), out var start));
            Assert.AreEqual(LobbyMessageKind.Start, start.Kind);

            Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Table(3, 5, 2), out var table));
            Assert.AreEqual(LobbyMessageKind.Table, table.Kind);
            Assert.AreEqual(3, table.Count);
            Assert.AreEqual(5, table.Capacity);
            Assert.AreEqual(2, table.Host);

            Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.CpuSeat(4), out var cpu));
            Assert.AreEqual(LobbyMessageKind.CpuSeat, cpu.Kind);
            Assert.AreEqual(4, cpu.Seat);

            Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.SeatAway(42, 3), out var away));
            Assert.AreEqual(42, away.Seconds);
            Assert.AreEqual(3, away.Seat);

            Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Turn(4, 10), out var turn));
            Assert.AreEqual(4, turn.Seat);
            Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Joined(3, "1234", true), out var joined));
            Assert.AreEqual(3, joined.Seat);

            var names = new[] { "あ", null, "c", "d", null };
            var ratings = new[] { 1500, -1, 1490, 1600, -1 };
            Assert.IsTrue(LobbyProtocol.TryParse(LobbyProtocol.Players(names, ratings), out var five));
            CollectionAssert.AreEqual(names, five.Names);
            CollectionAssert.AreEqual(ratings, five.Ratings);
        }

        [Test]
        public void BrokenTableMessages_AreRejected()
        {
            Assert.IsFalse(LobbyProtocol.TryParse("@table|6|5|0", out _), "最大より多い");
            Assert.IsFalse(LobbyProtocol.TryParse("@table|1|9|0", out _), "最大は 5 まで");
            Assert.IsFalse(LobbyProtocol.TryParse("@table|1|5|7", out _), "席は 0〜4");
            Assert.IsFalse(LobbyProtocol.TryParse("@cpu|5", out _));
            Assert.IsFalse(LobbyProtocol.TryParse("@turn|5|10", out _));
            Assert.IsFalse(LobbyProtocol.TryParse("@joined|5|-|0", out _));
            Assert.IsFalse(LobbyProtocol.TryParse("@players|a|1|b|2|c", out _), "名前とレートが組になっていない");
            Assert.IsFalse(LobbyProtocol.TryParse("@players|a|1|b|2|c|3|d|4|e|5|f|6", out _), "6 人分は多い");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => LobbyProtocol.Table(6, 5, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => LobbyProtocol.CpuSeat(5));
        }

        // ---------- ServerConnection ----------

        FakeSocket _socket;
        ServerConnection _server;
        double _now;

        [SetUp]
        public void SetUp()
        {
            _socket = new FakeSocket();
            _server = new ServerConnection("ghosttrend", "wss://example.test", _socket, new MemorySessionStore());
            _now = 0;
        }

        void Tick()
        {
            _now += 0.1;
            _server.Update(_now);
        }

        void EnterTable(int seat)
        {
            _server.Connect();
            _socket.Open();
            Tick();
            _now += ServerConnection.AuthTimeout + 1; // ログインしていない
            Tick();
            _server.JoinRandom();
            _socket.Receive(LobbyProtocol.Joined(seat, null, seat > 0));
            _socket.Receive(LobbyProtocol.Token(new string('a', LobbyProtocol.TokenLength)));
            Tick();
        }

        [Test]
        public void Table_UpdatesCountAndHost_AndOnlyTheHostIsHost()
        {
            var tables = new List<(int, int, int)>();
            _server.TableUpdated += (c, cap, h) => tables.Add((c, cap, h));
            EnterTable(1);
            _socket.Receive(LobbyProtocol.Table(2, 5, 0));
            Tick();
            Assert.AreEqual(ServerState.InRoom, _server.State);
            Assert.AreEqual(2, _server.TableCount);
            Assert.IsFalse(_server.IsTableHost);
            _socket.Receive(LobbyProtocol.Table(1, 5, 1)); // ホストが抜けて、自分がホストになった
            Tick();
            Assert.IsTrue(_server.IsTableHost);
            CollectionAssert.AreEqual(new[] { (2, 5, 0), (1, 5, 1) }, tables);
        }

        [Test]
        public void StartTable_SendsStart_AndTheSeatIsRenumberedWhenTheGameStarts()
        {
            int joinedCount = 0;
            _server.JoinedRoom += () => joinedCount++;
            EnterTable(2);
            _server.StartTable();
            CollectionAssert.Contains(_socket.Sent, LobbyProtocol.Start());
            _socket.Receive(LobbyProtocol.Joined(1, null, true)); // 開始のときに席を詰め直された
            Tick();
            Assert.AreEqual(1, _server.LocalSeat);
            Assert.AreEqual(2, joinedCount);
        }

        [Test]
        public void SeatAwayAndCpu_AreReportedWithTheSeat_AndDoNotLeaveTheRoom()
        {
            var away = new List<(int, int)>();
            var cpu = new List<int>();
            int opponentAway = 0;
            _server.SeatAway += (s, sec) => away.Add((s, sec));
            _server.SeatTakenOver += cpu.Add;
            _server.OpponentAway += _ => opponentAway++;
            EnterTable(0);
            _socket.Receive(LobbyProtocol.SeatAway(90, 3));
            _socket.Receive(LobbyProtocol.CpuSeat(3));
            Tick();
            CollectionAssert.AreEqual(new[] { (3, 90) }, away);
            CollectionAssert.AreEqual(new[] { 3 }, cpu);
            Assert.AreEqual(0, opponentAway, "2 人用のお知らせは出さない");
            Assert.AreEqual(ServerState.InRoom, _server.State);
        }

        [Test]
        public void PlayersUpdated_CarriesEverySeat()
        {
            string[] names = null;
            _server.PlayersUpdated += (n, r) => names = n;
            EnterTable(0);
            _socket.Receive(LobbyProtocol.Players(new[] { "a", "b", "c", "d" }, new[] { 1500, 1500, -1, 1510 }));
            Tick();
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, names);
        }
    }
}

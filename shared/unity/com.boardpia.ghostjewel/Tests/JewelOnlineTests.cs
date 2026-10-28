using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;
using BoardGames.GhostJewel.Online;
using NUnit.Framework;

namespace BoardGames.GhostJewel.Tests
{
    public class JewelOnlineTests
    {
        readonly JewelRules _rules = new JewelRules();

        /// <summary>局面を文字列にして戻すと、見えている情報がすべて同じになる(途中の局面も、終局も)。</summary>
        [Test]
        public void Snapshot_RoundTripsThroughTheProtocol([Values(2, 3, 4)] int players)
        {
            var s = JewelSetup.CreateRandom(new Random(players), players);
            var cpus = Enumerable.Range(0, players).Select(i => new JewelCpu(new Random(i))).ToArray();
            int checks = 0;
            while (true)
            {
                for (int viewer = 0; viewer < players; viewer++)
                {
                    var original = MatchSnapshot.For(s, viewer);
                    string text = JewelNetProtocol.EncodeSnapshot(original);
                    Assert.IsTrue(JewelNetProtocol.TryDecodeSnapshot(text, out var decoded), text);
                    Assert.AreEqual(text, JewelNetProtocol.EncodeSnapshot(decoded), "文字列に戻すと同じ");
                    AssertSameView(original.View, decoded.View);
                    checks++;
                }
                if (s.Result.IsOver) break;
                s = _rules.ApplyMove(s, cpus[s.CurrentPlayer].ChooseMove(JewelPlayerView.Create(s, s.CurrentPlayer)));
            }
            Assert.Greater(checks, players * 10);
        }

        static void AssertSameView(JewelPlayerView a, JewelPlayerView b)
        {
            Assert.AreEqual(a.Viewer, b.Viewer);
            Assert.AreEqual(a.CurrentPlayer, b.CurrentPlayer);
            Assert.AreEqual(a.Phase, b.Phase);
            Assert.AreEqual(a.Result.ToString(), b.Result.ToString());
            CollectionAssert.AreEqual(a.Bank, b.Bank);
            CollectionAssert.AreEqual(a.Nobles, b.Nobles);
            for (int t = 0; t < JewelLogic.Tiers; t++)
            {
                Assert.AreEqual(a.DeckCount(t), b.DeckCount(t));
                for (int s = 0; s < JewelLogic.MarketSize; s++) Assert.AreEqual(a.MarketAt(t, s), b.MarketAt(t, s));
            }
            for (int p = 0; p < a.PlayerCount; p++)
            {
                CollectionAssert.AreEqual(a.PlayerAt(p).Gems, b.PlayerAt(p).Gems);
                CollectionAssert.AreEqual(a.PlayerAt(p).Reserved, b.PlayerAt(p).Reserved);
                Assert.AreEqual(a.PlayerAt(p).Points, b.PlayerAt(p).Points);
            }
            CollectionAssert.AreEqual(a.GetLegalMoves(), b.GetLegalMoves(), "届いた局面でも同じ手が打てる");
            Assert.AreEqual(a.Events.Count, b.Events.Count);
        }

        [Test]
        public void Snapshot_HidesOthersDeckReservations()
        {
            var s = JewelSetup.CreateRandom(new Random(5), 2);
            s.StartPlayer = 0;
            s.CurrentPlayer = 0;
            s = _rules.ApplyMove(s, JewelMove.ReserveDeck(2));
            string forOpponent = JewelNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, 1));
            Assert.IsTrue(JewelNetProtocol.TryDecodeSnapshot(forOpponent, out var seen));
            Assert.AreEqual(-1, seen.View.PlayerAt(0).Reserved[0], "相手には予約したカードが届かない");
            Assert.IsTrue(seen.View.Events.Where(e => e.Kind == EventKind.Reserved).All(e => e.Card == -1));
            string forMe = JewelNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, 0));
            Assert.IsTrue(JewelNetProtocol.TryDecodeSnapshot(forMe, out var mine));
            Assert.AreEqual(s.Players[0].Reserved[0], mine.View.PlayerAt(0).Reserved[0], "自分には見える");
        }

        [Test]
        public void BrokenMessages_AreRejected()
        {
            Assert.IsFalse(JewelNetProtocol.TryDecodeSnapshot("S|1|0|2", out _));
            Assert.IsFalse(JewelNetProtocol.TryDecodeSnapshot("S|9|" + new string('|', 20), out _));
            Assert.IsFalse(JewelNetProtocol.TryDecodeMove("M|1|x,y", out _));
            Assert.IsFalse(JewelNetProtocol.TryDecodeMove("M|1|99,0", out _));
            Assert.AreEqual(NetMessageKind.Invalid, JewelNetProtocol.KindOf("hello"));
        }

        [Test]
        public void Move_RoundTrips()
        {
            foreach (var m in new[] { JewelMove.TakeGemsOf(0, 2, 4), JewelMove.TakeTwo(3), JewelMove.Reserve(17), JewelMove.ReserveDeck(1),
                         JewelMove.Buy(88), JewelMove.Return(Gem.Gold), JewelMove.Pass() })
            {
                Assert.IsTrue(JewelNetProtocol.TryDecodeMove(JewelNetProtocol.EncodeMove(m), out var back));
                Assert.AreEqual(m, back);
            }
        }

        /// <summary>全席が抜けるまで、抜けた席を CPU が代わって最後まで進む。順位は抜けた順に下になる。</summary>
        [Test]
        public void Table_CpuTakesOverAndRanksLeaversLast()
        {
            var table = new JewelTable(3, 42);
            var rng = new Random(1);
            int guard = 0;
            table.Takeover(2);
            while (!table.IsOver && guard++ < 2000)
            {
                int seat = table.CurrentSeat;
                Assert.IsFalse(table.IsCpu(seat), "CPU の席の番は、まとめて進んでいる");
                Assert.IsTrue(table.TryGetAutoMove(seat, out var move));
                Assert.IsTrue(table.TryApply(seat, move));
                if (guard == 30) table.Takeover(1);
            }
            Assert.IsTrue(table.IsOver);
            var ranks = table.Ranks;
            Assert.AreEqual(0, ranks[0], "最後まで残った人が 1 位");
            Assert.AreEqual(1, ranks[1]);
            Assert.AreEqual(2, ranks[2], "先に抜けた人がいちばん下");
            Assert.AreEqual(GameResult.WinFor(0).ToString(), table.SeatResult.ToString());
        }

        [Test]
        public void Table_RejectsMovesFromTheWrongSeat()
        {
            var table = new JewelTable(2, 7);
            int other = 1 - table.CurrentSeat;
            Assert.IsFalse(table.TryApply(other, JewelMove.TakeGemsOf(0, 1, 2)));
            Assert.IsFalse(table.TryGetAutoMove(other, out _));
        }

        /// <summary>OnlineMatch は届いた局面を表示し、自分の手番の合法手だけを送る。</summary>
        [Test]
        public void OnlineMatch_ShowsSnapshotsAndSendsOnlyLegalMoves()
        {
            var transport = new FakeTransport();
            var match = new OnlineMatch(transport, 0);
            var table = new JewelTable(2, 3);
            MatchSnapshot shown = null;
            match.SnapshotChanged += s => shown = s;
            transport.Deliver(table.SnapshotFor(0));
            Assert.IsNotNull(shown);
            var legal = shown.View.GetLegalMoves();
            if (shown.View.IsMyTurn)
            {
                match.SubmitLocalMove(legal[0]);
                Assert.AreEqual(JewelNetProtocol.EncodeMove(legal[0]), transport.Sent.Last());
            }
            match.SubmitLocalMove(JewelMove.Buy(-5));
            Assert.LessOrEqual(transport.Sent.Count, 1, "非合法の手は送らない");
            transport.Deliver("garbage");
            Assert.AreSame(shown, match.Snapshot);
        }

        sealed class FakeTransport : IMatchTransport
        {
            public readonly List<string> Sent = new List<string>();
            public event Action<string> MessageReceived;
            public void Send(string message) => Sent.Add(message);
            public void Deliver(string message) => MessageReceived?.Invoke(message);
        }
    }
}

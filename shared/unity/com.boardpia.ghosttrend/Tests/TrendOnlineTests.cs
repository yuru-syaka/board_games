using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;
using BoardGames.GhostTrend.Online;
using NUnit.Framework;

namespace BoardGames.GhostTrend.Tests
{
    public class TrendOnlineTests
    {
        readonly TrendRules _rules = new TrendRules();

        static int C(int artist, AuctionType type, int nth = 0) => TrendCards.Find(artist, type, nth);

        /// <summary>サーバーの代わり: TrendTable を持ち、席ごとの画面へ局面を送る。</summary>
        sealed class FakeServer
        {
            public readonly TrendTable Table;
            public readonly SeatTransport[] Seats;
            public int Rejected;

            public FakeServer(int players, int seed)
            {
                Table = new TrendTable(players, seed);
                Seats = new[] { new SeatTransport(this, 0), new SeatTransport(this, 1) };
            }

            public void Broadcast()
            {
                foreach (var s in Seats) s.Deliver(Table.SnapshotFor(s.Seat));
            }

            public void OnMessage(int seat, string message)
            {
                if (message == TrendNetProtocol.EncodeRematch()) return;
                if (!TrendNetProtocol.TryDecodeMove(message, out var move) || !Table.TryApply(seat, move))
                {
                    Rejected++;
                    return;
                }
                Broadcast();
            }
        }

        sealed class SeatTransport : IMatchTransport
        {
            readonly FakeServer _server;
            public readonly int Seat;

            public SeatTransport(FakeServer server, int seat)
            {
                _server = server;
                Seat = seat;
            }

            public event Action<string> MessageReceived;
            public void Send(string message) => _server.OnMessage(Seat, message);
            public void Deliver(string message) => MessageReceived?.Invoke(message);
        }

        static TrendMove PickRandom(List<TrendMove> moves, Random rng)
        {
            // 値上げばかりにならないよう、半分は降りる系の手を選ぶ
            var quit = moves.Where(m => m.Kind == MoveKind.Pass || m.Kind == MoveKind.Decline).ToList();
            return quit.Count > 0 && rng.Next(2) == 0 ? quit[0] : moves[rng.Next(Math.Min(moves.Count, 12))];
        }

        // ---------- メッセージ形式 ----------

        [Test]
        public void Move_RoundTripsAllKinds()
        {
            var moves = new[]
            {
                TrendMove.Play(5), TrendMove.AddDouble(69), TrendMove.Decline(), TrendMove.Bid(0), TrendMove.Bid(123),
                TrendMove.Pass(), TrendMove.SetPrice(40), TrendMove.Accept(),
            };
            foreach (var m in moves)
            {
                Assert.IsTrue(TrendNetProtocol.TryDecodeMove(TrendNetProtocol.EncodeMove(m), out var back));
                Assert.AreEqual(m, back);
            }
            Assert.IsFalse(TrendNetProtocol.TryDecodeMove("M|1|9,0,0", out _));
            Assert.IsFalse(TrendNetProtocol.TryDecodeMove("M|2|0,1,0", out _));
            Assert.IsFalse(TrendNetProtocol.TryDecodeMove("M|1|x", out _));
        }

        [Test]
        public void Snapshot_RoundTrips_MidSealedAuctionAfterARound()
        {
            int fifth = C(0, AuctionType.Open), sealedCard = C(2, AuctionType.Sealed);
            var s = TrendSetup.CreateCustom(new[] { new[] { fifth, C(3, AuctionType.Open) }, new[] { sealedCard, C(1, AuctionType.Open) },
                new[] { C(4, AuctionType.Open) } }, null, 0, 0, new[] { 4, 0, 0, 0, 0 },
                bought: new[] { new[] { C(0, AuctionType.Fixed) }, new int[0], new int[0] }, deck: TrendCards.AllCards());
            s = _rules.ApplyMove(s, TrendMove.Play(fifth)); // ラウンド終了 → 次は p1
            s = _rules.ApplyMove(s, TrendMove.Play(sealedCard));
            s = _rules.ApplyMove(s, TrendMove.Bid(17)); // p2 が伏せて入札

            var original = MatchSnapshot.For(s, 0);
            var text = TrendNetProtocol.EncodeSnapshot(original);
            Assert.IsTrue(TrendNetProtocol.TryDecodeSnapshot(text, out var back), text);
            Assert.AreEqual(text, TrendNetProtocol.EncodeSnapshot(back), "同じ文字列に戻る");
            var v = back.View;
            Assert.AreEqual(1, v.Round);
            Assert.AreEqual(TrendPhase.Auction, v.Phase);
            Assert.AreEqual(AuctionType.Sealed, v.Auction.Type);
            Assert.AreEqual(AuctionInfo.Hidden, v.Auction.Bids[2]);
            Assert.AreEqual(-1, v.Events.Last().Amount, "伏せた額は送らない");
            Assert.AreEqual(1, v.Summaries.Count);
            CollectionAssert.AreEqual(s.Hands[0], v.Hand);
            Assert.AreEqual(-1, v.Money[1]);
        }

        [Test]
        public void Snapshot_RejectsBrokenMessages()
        {
            var s = MatchSnapshot.For(TrendSetup.CreateRandom(new Random(1), 3), 0);
            var text = TrendNetProtocol.EncodeSnapshot(s);
            Assert.IsTrue(TrendNetProtocol.TryDecodeSnapshot(text, out _));
            Assert.IsFalse(TrendNetProtocol.TryDecodeSnapshot(text.Replace("S|1|0|3|", "S|1|0|9|"), out _));
            Assert.IsFalse(TrendNetProtocol.TryDecodeSnapshot(text + "|x", out _));
            Assert.IsFalse(TrendNetProtocol.TryDecodeSnapshot("S|1|garbage", out _));
            Assert.IsFalse(TrendNetProtocol.TryDecodeSnapshot("", out _));
        }

        [Test]
        public void Snapshot_DoesNotContainOtherHands()
        {
            var state = TrendSetup.CreateRandom(new Random(4), 3);
            var text = TrendNetProtocol.EncodeSnapshot(MatchSnapshot.For(state, 1));
            Assert.IsTrue(TrendNetProtocol.TryDecodeSnapshot(text, out var back));
            CollectionAssert.AreEqual(state.Hands[1], back.View.Hand);
            Assert.AreEqual(state.Deck.Count, back.View.DeckCount);
            CollectionAssert.AreEqual(new[] { -1, 100, -1 }, back.View.Money);
        }

        // ---------- サーバーの対局(人間 2 人 + CPU) ----------

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Table_OnlyHumansAreAsked_AndSameSeedGivesSameGame(int players)
        {
            var a = new TrendTable(players, 77);
            var b = new TrendTable(players, 77);
            var rng = new Random(players);
            for (int i = 0; i < 20000 && !a.IsOver; i++)
            {
                Assert.That(a.CurrentSeat, Is.EqualTo(0).Or.EqualTo(1), "CPU の番はサーバーが進めてしまう");
                var move = PickRandom(_rules.GetLegalMoves(a.State).ToList(), rng);
                int seat = a.CurrentSeat;
                Assert.IsTrue(a.TryApply(seat, move));
                Assert.IsTrue(b.TryApply(seat, move));
                Assert.AreEqual(a.SnapshotFor(0), b.SnapshotFor(0), "作り直しても同じ局面");
            }
            Assert.IsTrue(a.IsOver);
            Assert.AreEqual(-1, a.CurrentSeat);
            var expected = a.State.Money[0] == a.State.Money[1] ? GameStatus.Draw : GameStatus.Win;
            Assert.AreEqual(expected, a.SeatResult.Status);
        }

        [Test]
        public void Table_RejectsWrongSeatAndIllegalMoves()
        {
            var t = new TrendTable(4, 5);
            int other = 1 - t.CurrentSeat;
            Assert.IsFalse(t.TryApply(other, TrendMove.Pass()));
            Assert.IsFalse(t.TryApply(2, TrendMove.Pass()), "CPU の席からは指せない");
            Assert.IsFalse(t.TryApply(t.CurrentSeat, TrendMove.Play(-5)));
        }

        [Test]
        public void TwoOnlineMatches_PlayToTheEnd_ThroughTheServer()
        {
            var server = new FakeServer(4, 21);
            var m0 = new OnlineMatch(server.Seats[0], 0);
            var m1 = new OnlineMatch(server.Seats[1], 1);
            server.Broadcast();
            Assert.IsTrue(MatchSnapshot.StartsNewGame(null, m0.Snapshot));
            var rng = new Random(8);
            for (int i = 0; i < 20000 && !m0.Snapshot.Result.IsOver; i++)
            {
                var mover = m0.Snapshot.CurrentPlayer == 0 ? m0 : m1;
                Assert.IsTrue(mover.Snapshot.View.IsMyTurn);
                mover.SubmitLocalMove(PickRandom(mover.Snapshot.View.GetLegalMoves(), rng));
                Assert.AreEqual(m0.Snapshot.Ply, m1.Snapshot.Ply);
            }
            Assert.AreEqual(0, server.Rejected);
            Assert.IsTrue(m0.Snapshot.Result.IsOver && m1.Snapshot.Result.IsOver);
            Assert.IsTrue(m0.Snapshot.View.Money.All(x => x >= 0), "終局後は全員の所持金が見える");

            // 再戦の合図
            m0.RequestRematch();
            Assert.IsTrue(m0.LocalWantsRematch);
            server.Seats[1].Deliver(TrendNetProtocol.EncodeRematch());
            Assert.IsTrue(m1.OpponentWantsRematch);
        }

        [Test]
        public void OnlineMatch_IgnoresSnapshotsForOtherSeat()
        {
            var server = new FakeServer(3, 2);
            var m1 = new OnlineMatch(server.Seats[1], 1);
            string error = null;
            m1.ProtocolError += e => error = e;
            server.Seats[1].Deliver(server.Table.SnapshotFor(0));
            Assert.IsNull(m1.Snapshot);
            Assert.IsNotNull(error);
        }
    }
}

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
                Seats = Enumerable.Range(0, players).Select(s => new SeatTransport(this, s)).ToArray();
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

        // ---------- サーバーの対局(全席が人間。抜けた席だけ CPU) ----------

        /// <summary>今の手番の人が、ランダムな手を指す(最後まで)。</summary>
        void PlayOut(TrendTable t, Random rng, TrendTable twin = null)
        {
            for (int i = 0; i < 20000 && !t.IsOver; i++)
            {
                int seat = t.CurrentSeat;
                Assert.IsFalse(t.IsCpu(seat), "CPU の席の番はサーバーが進めてしまう");
                var move = PickRandom(_rules.GetLegalMoves(t.State).ToList(), rng);
                Assert.IsTrue(t.TryApply(seat, move));
                if (twin != null)
                {
                    Assert.IsTrue(twin.TryApply(seat, move));
                    Assert.AreEqual(t.SnapshotFor(0), twin.SnapshotFor(0), "作り直しても同じ局面");
                }
            }
            Assert.IsTrue(t.IsOver);
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Table_AllSeatsAreHumans_AndSameSeedGivesSameGame(int players)
        {
            var a = new TrendTable(players, 77);
            var b = new TrendTable(players, 77);
            Assert.AreEqual(players, a.HumanCount);
            var asked = new HashSet<int>();
            var rng = new Random(players);
            for (int i = 0; i < 20000 && !a.IsOver; i++)
            {
                int seat = a.CurrentSeat;
                asked.Add(seat);
                var move = PickRandom(_rules.GetLegalMoves(a.State).ToList(), rng);
                Assert.IsTrue(a.TryApply(seat, move));
                Assert.IsTrue(b.TryApply(seat, move));
            }
            Assert.IsTrue(a.IsOver);
            Assert.AreEqual(-1, a.CurrentSeat);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, players), asked, "全員に手番が回る");
            Assert.AreEqual(a.SnapshotFor(1), b.SnapshotFor(1));
        }

        [Test]
        public void Table_RanksByMoney_TiesShareARank_AndResultFollowsFirstPlace()
        {
            var t = new TrendTable(4, 3);
            PlayOut(t, new Random(4));
            var money = t.State.Money;
            var ranks = t.Ranks;
            for (int s = 0; s < 4; s++)
                Assert.AreEqual(Enumerable.Range(0, 4).Count(o => money[o] > money[s]), ranks[s]);
            var first = Enumerable.Range(0, 4).Where(s => ranks[s] == 0).ToList();
            if (first.Count == 1) Assert.AreEqual(GameResult.WinFor(first[0]).Winner, t.SeatResult.Winner);
            else Assert.AreEqual(GameStatus.Draw, t.SeatResult.Status);
        }

        [Test]
        public void Table_RejectsWrongSeatIllegalMovesAndCpuSeats()
        {
            var t = new TrendTable(4, 5);
            int other = (t.CurrentSeat + 1) % 4;
            Assert.IsFalse(t.TryApply(other, TrendMove.Pass()));
            Assert.IsFalse(t.TryApply(7, TrendMove.Pass()));
            Assert.IsFalse(t.TryApply(t.CurrentSeat, TrendMove.Play(-5)));
            Assert.IsTrue(t.Takeover(other));
            Assert.IsFalse(t.Takeover(other), "2 回は代われない");
            Assert.IsTrue(t.IsCpu(other));
            Assert.AreEqual(3, t.HumanCount);
        }

        [Test]
        public void Takeover_OfTheCurrentSeat_PlaysOnAtOnce_AndIsReproducible()
        {
            var a = new TrendTable(3, 11);
            var b = new TrendTable(3, 11);
            int seat = a.CurrentSeat;
            Assert.IsTrue(a.Takeover(seat));
            Assert.IsTrue(b.Takeover(seat));
            Assert.AreNotEqual(seat, a.CurrentSeat, "CPU がすぐに指して、次の人の番になる");
            Assert.AreEqual(a.SnapshotFor(0), b.SnapshotFor(0), "同じ種・同じ時点で代われば同じ局面");
            PlayOut(a, new Random(2), b);
            Assert.AreEqual(2, a.Ranks[seat], "代わった席は最下位");
        }

        [Test]
        public void Ranks_PutEarlierLeaversLower()
        {
            var t = new TrendTable(5, 9);
            int first = (t.CurrentSeat + 1) % 5, second = (t.CurrentSeat + 2) % 5;
            Assert.IsTrue(t.Takeover(first));
            Assert.IsTrue(t.Takeover(second));
            PlayOut(t, new Random(6));
            Assert.AreEqual(4, t.Ranks[first], "先に抜けた人がいちばん下");
            Assert.AreEqual(3, t.Ranks[second]);
            Assert.IsTrue(Enumerable.Range(0, 5).Where(s => s != first && s != second).All(s => t.Ranks[s] <= 2));
        }

        [Test]
        public void AutoMove_PassesInAuctions_AndPicksACardWhenItMustPlay()
        {
            var t = new TrendTable(3, 13);
            int seat = t.CurrentSeat;
            Assert.AreEqual(TrendPhase.Play, t.State.Phase);
            Assert.IsFalse(t.TryGetAutoMove((seat + 1) % 3, out _), "手番でない席には出さない");
            Assert.IsTrue(t.TryGetAutoMove(seat, out var play));
            Assert.AreEqual(MoveKind.Play, play.Kind, "出品は降りられないので、カードを選ぶ");
            Assert.IsTrue(t.TryApply(seat, play));

            // その後の競りでは、降りる・見送る・誘いを断る
            for (int i = 0; i < 200 && !t.IsOver; i++)
            {
                int s = t.CurrentSeat;
                Assert.IsTrue(t.TryGetAutoMove(s, out var m));
                if (t.State.Phase == TrendPhase.Auction || t.State.Phase == TrendPhase.DoubleOffer)
                {
                    bool canQuit = _rules.GetLegalMoves(t.State).Any(x => x.Kind == MoveKind.Pass || x.Kind == MoveKind.Decline);
                    if (canQuit) Assert.That(m.Kind, Is.EqualTo(MoveKind.Pass).Or.EqualTo(MoveKind.Decline));
                }
                Assert.IsTrue(t.TryApply(s, m), "自動の手はいつも合法");
            }
        }

        [TestCase(3)]
        [TestCase(5)]
        public void OnlineMatches_ForEverySeat_PlayToTheEnd_ThroughTheServer(int players)
        {
            var server = new FakeServer(players, 21);
            var ms = Enumerable.Range(0, players).Select(s => new OnlineMatch(server.Seats[s], s)).ToArray();
            server.Broadcast();
            Assert.IsTrue(MatchSnapshot.StartsNewGame(null, ms[0].Snapshot));
            var rng = new Random(8);
            for (int i = 0; i < 20000 && !ms[0].Snapshot.Result.IsOver; i++)
            {
                var mover = ms[ms[0].Snapshot.CurrentPlayer];
                Assert.IsTrue(mover.Snapshot.View.IsMyTurn);
                mover.SubmitLocalMove(PickRandom(mover.Snapshot.View.GetLegalMoves(), rng));
                Assert.IsTrue(ms.All(m => m.Snapshot.Ply == ms[0].Snapshot.Ply));
            }
            Assert.AreEqual(0, server.Rejected);
            Assert.IsTrue(ms.All(m => m.Snapshot.Result.IsOver));
            Assert.IsTrue(ms[0].Snapshot.View.Money.All(x => x >= 0), "終局後は全員の所持金が見える");

            // 再戦の合図
            ms[0].RequestRematch();
            Assert.IsTrue(ms[0].LocalWantsRematch);
            server.Seats[1].Deliver(TrendNetProtocol.EncodeRematch());
            Assert.IsTrue(ms[1].OpponentWantsRematch);
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

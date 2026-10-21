using System;
using System.Linq;
using BoardGames.Core;
using BoardGames.CenturyGhost.Online;
using NUnit.Framework;

namespace BoardGames.CenturyGhost.Tests
{
    public class CenturyOnlineTests
    {
        LoopbackTransport _hostLink;
        LoopbackTransport _guestLink;
        OnlineMatch _host;
        OnlineMatch _guest;

        [SetUp]
        public void SetUp()
        {
            LoopbackTransport.CreatePair(out _hostLink, out _guestLink);
            _host = new OnlineMatch(_hostLink, true, new Random(1));
            _guest = new OnlineMatch(_guestLink, false);
        }

        void Pump() => _hostLink.Pump();

        // ---------- プロトコル ----------

        [Test]
        public void Snapshot_RoundTrips()
        {
            var rules = new CenturyRules();
            var rng = new Random(3);
            var state = CenturySetup.CreateRandom(rng);
            for (int i = 0; i < 25 && !state.Result.IsOver; i++)
            {
                var moves = rules.GetLegalMoves(state);
                state = rules.ApplyMove(state, moves[rng.Next(moves.Count)]);
            }
            var original = MatchSnapshot.For(state, 1);

            Assert.IsTrue(CenturyNetProtocol.TryDecodeSnapshot(CenturyNetProtocol.EncodeSnapshot(original), out var decoded));
            var a = original.View;
            var b = decoded.View;
            Assert.AreEqual(a.Viewer, b.Viewer);
            Assert.AreEqual(a.FirstPlayer, b.FirstPlayer);
            Assert.AreEqual(a.CurrentPlayer, b.CurrentPlayer);
            Assert.AreEqual(a.Ply, b.Ply);
            Assert.AreEqual(a.FinalRound, b.FinalRound);
            Assert.AreEqual(a.LastMove, b.LastMove);
            Assert.AreEqual(a.MarketDeckCount, b.MarketDeckCount);
            Assert.AreEqual(a.GhostDeckCount, b.GhostDeckCount);
            Assert.AreEqual(original.Result, decoded.Result);
            CollectionAssert.AreEqual(a.Table.Market, b.Table.Market);
            CollectionAssert.AreEqual(a.Table.MarketSpirits, b.Table.MarketSpirits);
            CollectionAssert.AreEqual(a.Table.GhostRow, b.Table.GhostRow);
            Assert.AreEqual(a.Table.GoldLeft, b.Table.GoldLeft);
            Assert.AreEqual(a.Table.SilverLeft, b.Table.SilverLeft);
            for (int p = 0; p < 2; p++)
                Assert.AreEqual(a.Players[p].ToString(), b.Players[p].ToString());
        }

        [Test]
        public void Move_RoundTrips()
        {
            foreach (var m in new[]
                     {
                         CenturyMove.Play(0), CenturyMove.Trade(18, 3), CenturyMove.Upgrade(17, 0, 1, 2), CenturyMove.Acquire(4),
                         CenturyMove.Claim(2), CenturyMove.Rest(),
                     })
            {
                Assert.IsTrue(CenturyNetProtocol.TryDecodeMove(CenturyNetProtocol.EncodeMove(m), out var decoded), m.ToString());
                Assert.AreEqual(m, decoded);
            }
        }

        [Test]
        public void MalformedMessages_AreRejected()
        {
            foreach (var bad in new[]
                     {
                         "", "S", "S|9|x", "M|1|1,2", "M|1|a,b,c,", "M|1|9,0,0,", "M|1|0,0,0,7", "X|1",
                         "S|1|0|0|0|0|0|0|-1|0|0|0|-|//////|_/////0/0|_/////0/0",
                         "S|1|0|0|0|0|0|0|-1|0|0|0|-|99/_//4/4|_////0/0|_////0/0",
                     })
            {
                Assert.IsFalse(CenturyNetProtocol.TryDecodeSnapshot(bad, out _), bad);
                Assert.IsFalse(CenturyNetProtocol.TryDecodeMove(bad, out _), bad);
            }
        }

        // ---------- 対局の進行 ----------

        [Test]
        public void StartNewGame_GivesBothSidesTheSameTable()
        {
            _host.StartNewGame();
            Pump();
            Assert.IsNotNull(_guest.Snapshot);
            Assert.AreEqual(1, _guest.Snapshot.Viewer);
            Assert.IsTrue(_guest.Snapshot.IsNewGame);
            Assert.AreEqual(_host.Snapshot.CurrentPlayer, _guest.Snapshot.CurrentPlayer);
            CollectionAssert.AreEqual(_host.Snapshot.View.Table.Market, _guest.Snapshot.View.Table.Market);
            CollectionAssert.AreEqual(_host.Snapshot.View.Table.GhostRow, _guest.Snapshot.View.Table.GhostRow);
        }

        [Test]
        public void GuestMove_IsAppliedByHostAndEchoed()
        {
            StartWithGuestToMove();
            var move = _guest.Snapshot.View.GetLegalMoves().First();
            _guest.SubmitLocalMove(move);
            Pump();
            Assert.AreEqual(1, _host.Snapshot.Ply);
            Assert.AreEqual(1, _guest.Snapshot.Ply);
            Assert.AreEqual(move, _guest.Snapshot.LastMove);
            Assert.AreEqual(OnlineMatch.HostPlayer, _guest.Snapshot.CurrentPlayer);
        }

        [Test]
        public void GuestMoveOutOfTurn_IsRejected()
        {
            StartWithHostToMove();
            string error = null;
            _host.ProtocolError += e => error = e;
            _guestLink.Send(CenturyNetProtocol.EncodeMove(CenturyMove.Play(2)));
            Pump();
            Assert.IsNotNull(error);
            Assert.AreEqual(0, _host.Snapshot.Ply);
        }

        [Test]
        public void IllegalGuestMove_IsRejectedAndResynced()
        {
            StartWithGuestToMove();
            int before = _guest.Snapshot.Ply;
            _guestLink.Send(CenturyNetProtocol.EncodeMove(CenturyMove.Claim(0))); // 黄だけでは呼べない
            Pump();
            Assert.AreEqual(before, _host.Snapshot.Ply);
            Assert.AreEqual(before, _guest.Snapshot.Ply);
        }

        [Test]
        public void Snapshots_DoNotContainDeckOrder()
        {
            StartWithHostToMove();
            var snapshot = _hostLink.SentLog.Last();
            var parts = snapshot.Split('|');
            Assert.AreEqual(16, parts.Length);
            // 山札は枚数だけ
            Assert.AreEqual(CardCatalog.MarketCardIds().Count() - CenturyLogic.MarketSize, int.Parse(parts[10]));
            Assert.AreEqual(CardCatalog.GhostCount - CenturyLogic.GhostRowSize, int.Parse(parts[11]));
        }

        [Test]
        public void Rematch_StartsNewGameWhenBothAgree()
        {
            PlayToEnd();
            Assert.IsTrue(_guest.Snapshot.Result.IsOver);
            _guest.RequestRematch();
            Pump();
            Assert.IsTrue(_host.OpponentWantsRematch);
            Assert.IsTrue(_host.Snapshot.Result.IsOver, "片方だけでは始まらない");
            _host.RequestRematch();
            Pump();
            Assert.IsTrue(_host.Snapshot.IsNewGame);
            Assert.IsTrue(_guest.Snapshot.IsNewGame);
            Assert.IsFalse(_guest.LocalWantsRematch);
        }

        // ---------- 補助 ----------

        void StartWithGuestToMove()
        {
            for (int i = 0; i < 20; i++)
            {
                _host.StartNewGame();
                Pump();
                if (_host.Snapshot.CurrentPlayer == OnlineMatch.GuestPlayer) return;
            }
            Assert.Fail("guest never moved first");
        }

        void StartWithHostToMove()
        {
            for (int i = 0; i < 20; i++)
            {
                _host.StartNewGame();
                Pump();
                if (_host.Snapshot.CurrentPlayer == OnlineMatch.HostPlayer) return;
            }
            Assert.Fail("host never moved first");
        }

        void PlayToEnd()
        {
            _host.StartNewGame();
            Pump();
            var cpu = new CenturyCpu(new Random(4));
            while (!_host.Snapshot.Result.IsOver)
            {
                var side = _host.Snapshot.CurrentPlayer == OnlineMatch.HostPlayer ? _host : _guest;
                side.SubmitLocalMove(cpu.ChooseMove(side.Snapshot.View));
                Pump();
            }
            Assert.AreEqual(_host.Snapshot.Result, _guest.Snapshot.Result);
        }
    }
}

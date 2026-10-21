using System;
using System.Linq;
using BoardGames.BattleLine.Online;
using NUnit.Framework;
using static BoardGames.BattleLine.Tests.TestBoard;

namespace BoardGames.BattleLine.Tests
{
    public class BattleLineOnlineTests
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
            var rules = new BattleLineRules();
            var s = Empty();
            Put(s, 0, 0, T(R, 1), T(R, 2));
            Put(s, 1, 0, T(B, 5), Card.LeaderA);
            Env(s, 1, 3, Card.Fog);
            Env(s, 0, 3, Card.Mud);
            Put(s, 0, 5, T(G, 7), T(G, 8), T(G, 9));
            Claim(s, 0, 5);
            Discard(s, Card.Scout, T(Y, 4));
            Hand(s, 0, T(P, 9), Card.Deserter);
            Hand(s, 1, T(P, 1), T(P, 2), Card.Traitor);
            s.SetTacticsPlayed(0, 1);
            s.SetTacticsPlayed(1, 2);
            s.SetLeaderPlayed(1, true);
            s = rules.ApplyMove(s, BattleLineMove.Place(T(P, 9), 7));

            var original = MatchSnapshot.For(s, 1);
            string encoded = BattleLineNetProtocol.EncodeSnapshot(original);
            Assert.IsTrue(BattleLineNetProtocol.TryDecodeSnapshot(encoded, out var decoded), encoded);
            Assert.AreEqual(encoded, BattleLineNetProtocol.EncodeSnapshot(decoded), "再エンコードしても同じ");
            Assert.AreEqual(original.Viewer, decoded.Viewer);
            Assert.AreEqual(original.Phase, decoded.Phase);
            Assert.AreEqual(original.LastMove, decoded.LastMove);
            CollectionAssert.AreEqual(original.View.Hand, decoded.View.Hand);
            Assert.AreEqual(1, decoded.View.OpponentHandTactics);
            Assert.IsTrue(decoded.View.Flags[3].HasFog && decoded.View.Flags[3].HasMud);
            Assert.AreEqual(0, decoded.View.Flags[5].Claimer);
            CollectionAssert.AreEquivalent(original.View.GetLegalMoves(), decoded.View.GetLegalMoves());
        }

        [Test]
        public void Move_RoundTrips()
        {
            foreach (var m in new[]
                     {
                         BattleLineMove.Place(T(R, 3), 4), BattleLineMove.UseScout(Card.Scout),
                         BattleLineMove.UseRedeploy(Card.Redeploy, 1, T(B, 2), -1), BattleLineMove.UseTraitor(Card.Traitor, 2, T(G, 9), 6),
                         BattleLineMove.UseDeserter(Card.Deserter, 0, Card.Fog), BattleLineMove.Pass,
                         BattleLineMove.Draw(DeckKind.Tactics), BattleLineMove.ReturnCard(Card.Fog),
                     })
            {
                Assert.IsTrue(BattleLineNetProtocol.TryDecodeMove(BattleLineNetProtocol.EncodeMove(m), out var decoded), m.ToString());
                Assert.AreEqual(m, decoded);
            }
        }

        [Test]
        public void MalformedMessages_AreRejected()
        {
            foreach (var bad in new[] { "", "S", "S|9|x", "M|1|1,2", "M|1|a,b,c,d,e,f", "M|1|0,99,0,-1,-1,0", "X|1", "S|1|0|0|0" })
            {
                Assert.IsFalse(BattleLineNetProtocol.TryDecodeSnapshot(bad, out _), bad);
                Assert.IsFalse(BattleLineNetProtocol.TryDecodeMove(bad, out _), bad);
            }
        }

        [Test]
        public void Snapshot_WithDuplicateCard_IsRejected()
        {
            _host.StartNewGame();
            Pump();
            string good = BattleLineNetProtocol.EncodeSnapshot(_guest.Snapshot);
            var parts = good.Split('|');
            parts[24] = _guest.Snapshot.View.Hand[0].ToString(); // 手札と同じカードを捨て札にも入れる
            Assert.IsFalse(BattleLineNetProtocol.TryDecodeSnapshot(string.Join("|", parts), out _));
        }

        // ---------- 対局の進行 ----------

        [Test]
        public void StartNewGame_GivesGuestItsOwnHandOnly()
        {
            _host.StartNewGame();
            Pump();
            Assert.IsNotNull(_guest.Snapshot);
            Assert.AreEqual(1, _guest.Snapshot.Viewer);
            Assert.IsTrue(_guest.Snapshot.IsNewGame);
            Assert.AreEqual(_host.Snapshot.CurrentPlayer, _guest.Snapshot.CurrentPlayer);
            Assert.AreEqual(7, _guest.Snapshot.View.Hand.Count);
            Assert.IsNull(_guest.Snapshot.View.OpponentHand);
            CollectionAssert.IsEmpty(_guest.Snapshot.View.Hand.Intersect(_host.Snapshot.View.Hand));
        }

        [Test]
        public void Guest_NeverReceivesHostHandBeforeGameOver()
        {
            var rng = new Random(5);
            for (int game = 0; game < 3; game++)
            {
                _hostLink.SentLog.Clear();
                _host.StartNewGame();
                Pump();
                PlayRandomGame(rng);

                foreach (var msg in _hostLink.SentLog)
                {
                    if (BattleLineNetProtocol.KindOf(msg) != NetMessageKind.Snapshot) continue;
                    Assert.IsTrue(BattleLineNetProtocol.TryDecodeSnapshot(msg, out var s));
                    if (s.Result.IsOver) continue;
                    Assert.IsNull(s.View.OpponentHand, "host hand leaked to guest");
                    if (s.LastMove?.Kind == MoveKind.ScoutReturn && s.View.LastMover == 0)
                        Assert.AreEqual(-1, s.LastMove.Value.Card, "returned card leaked");
                }

                Assert.IsTrue(_guest.Snapshot.Result.IsOver);
                Assert.IsNotNull(_guest.Snapshot.View.OpponentHand, "決着後は相手の手札を公開");
            }
        }

        [Test]
        public void FullGame_BothSidesAgreeOnResult()
        {
            _host.StartNewGame();
            Pump();
            PlayRandomGame(new Random(8));
            Assert.AreEqual(_host.Snapshot.Result.Status, _guest.Snapshot.Result.Status);
            Assert.AreEqual(_host.Snapshot.Result.Winner, _guest.Snapshot.Result.Winner);
            Assert.AreEqual(_host.Snapshot.EndReason, _guest.Snapshot.EndReason);
            Assert.AreEqual(_host.Snapshot.Step, _guest.Snapshot.Step);
        }

        [Test]
        public void Host_RejectsIllegalMoveFromTamperedGuestAndResyncs()
        {
            _host.StartNewGame();
            Pump();
            while (_host.Snapshot.CurrentPlayer != 1 || _host.Snapshot.Phase != TurnPhase.Play)
            {
                var side = _host.Snapshot.CurrentPlayer == 0 ? _host : _guest;
                side.SubmitLocalMove(side.Snapshot.View.GetLegalMoves()[0]);
                Pump();
            }
            int step = _host.Snapshot.Step;
            string error = null;
            _host.ProtocolError += e => error = e;
            int guestUpdates = 0;
            _guest.SnapshotChanged += _ => guestUpdates++;

            // 改造クライアントを想定: ホストの手札のカードを出す / ドローの段階でないのに引く
            int hostCard = _host.Snapshot.View.Hand[0];
            _guestLink.Send(BattleLineNetProtocol.EncodeMove(BattleLineMove.Place(hostCard, 0)));
            _guestLink.Send(BattleLineNetProtocol.EncodeMove(BattleLineMove.Draw(DeckKind.Troop)));
            Pump();

            Assert.AreEqual(step, _host.Snapshot.Step);
            Assert.IsNotNull(error);
            Assert.AreEqual(2, guestUpdates, "host should resend the current snapshot after each rejected move");
        }

        [Test]
        public void Rematch_StartsOnlyWhenBothRequest()
        {
            _host.StartNewGame();
            Pump();
            PlayRandomGame(new Random(3));

            _guest.RequestRematch();
            Pump();
            Assert.IsTrue(_host.OpponentWantsRematch);
            Assert.IsTrue(_host.Snapshot.Result.IsOver, "must not restart with only one request");

            _host.RequestRematch();
            Pump();
            Assert.IsFalse(_host.Snapshot.Result.IsOver);
            Assert.IsTrue(_guest.Snapshot.IsNewGame);
            Assert.IsFalse(_guest.LocalWantsRematch);
        }

        [Test]
        public void GuestRejectsSnapshotForWrongViewer()
        {
            _host.StartNewGame();
            Pump();
            string error = null;
            _guest.ProtocolError += e => error = e;
            _hostLink.Send(BattleLineNetProtocol.EncodeSnapshot(_host.Snapshot));
            Pump();
            Assert.IsNotNull(error);
            Assert.AreEqual(1, _guest.Snapshot.Viewer);
        }

        // ---------- CPU 戦 ----------

        [Test]
        public void LocalCpuMatch_PlaysToTheEnd()
        {
            var match = new LocalCpuMatch(new Random(4));
            var rng = new Random(4);
            match.StartNewGame();
            int guard = 0;
            while (!match.Snapshot.Result.IsOver && guard++ < 2000)
            {
                if (match.IsCpuTurn) match.PlayCpuTurn();
                else
                {
                    var moves = match.Snapshot.View.GetLegalMoves();
                    match.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                }
            }
            Assert.IsTrue(match.Snapshot.Result.IsOver);
            Assert.IsNotNull(match.Snapshot.View.OpponentHand);
        }

        // ---------- 補助 ----------

        void PlayRandomGame(Random rng)
        {
            int guard = 0;
            while (!_host.Snapshot.Result.IsOver && guard++ < 2000)
            {
                var side = _host.Snapshot.CurrentPlayer == 0 ? _host : _guest;
                var moves = side.Snapshot.View.GetLegalMoves();
                Assert.IsNotEmpty(moves);
                side.SubmitLocalMove(moves[rng.Next(moves.Count)]);
                Pump();
            }
            Assert.IsTrue(_host.Snapshot.Result.IsOver);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.GhostDerby.Online;
using NUnit.Framework;

namespace BoardGames.GhostDerby.Tests
{
    public class DerbyTests
    {
        readonly DerbyRules _rules = new DerbyRules();

        DerbyState PlayOut(int players, int seed, Action<DerbyState> onEach = null)
        {
            var s = DerbySetup.Create(players, seed);
            var cpus = Enumerable.Range(0, players).Select(i => new DerbyCpu(new Random(seed * 17 + i))).ToArray();
            int guard = 0;
            while (!s.Result.IsOver)
            {
                onEach?.Invoke(s);
                Assert.Less(++guard, 200, "対局が終わらない");
                var move = cpus[s.CurrentPlayer].ChooseMove(DerbyPlayerView.Create(s, s.CurrentPlayer));
                Assert.IsTrue(_rules.IsLegal(s, move), move.ToString());
                s = _rules.ApplyMove(s, move);
            }
            onEach?.Invoke(s);
            return s;
        }

        DerbyState ToTrickPhase(int players, int seed, int runner = 0, int amount = 1)
        {
            var s = DerbySetup.Create(players, seed);
            for (int i = 0; i < players; i++) s = _rules.ApplyMove(s, DerbyMove.Bet(runner, amount));
            return s;
        }

        [Test]
        public void Setup_DealsCoinsHandsAndAllOdds([Values(2, 3, 4, 5)] int players)
        {
            var s = DerbySetup.Create(players, 5);
            Assert.AreEqual(DerbyPhase.Bet, s.Phase);
            Assert.AreEqual(0, s.CurrentPlayer);
            Assert.IsTrue(s.Players.All(p => p.Coins == 10 && p.Hand.Count == 3));
            CollectionAssert.AreEquivalent(DerbyCards.OddsTable, s.Odds);
            Assert.AreEqual(30 - players * 3, s.DrawPile.Count);
        }

        [Test]
        public void Steps_LowOddsAreSteadyAndHighOddsAreSlowerOrSwingy()
        {
            int Sum(int odds) => Enumerable.Range(0, 5).Sum(i => DerbyCards.Steps(odds, i));
            int Min(int odds) => Enumerable.Range(0, 5).Min(i => DerbyCards.Steps(odds, i));
            Assert.AreEqual(11, Sum(2));
            Assert.AreEqual(2, Min(2), "2倍は必ず 2 マス以上進む");
            Assert.Greater(Sum(2), Sum(3));
            Assert.Greater(Sum(3), Sum(5));
            Assert.Greater(Sum(5), Sum(8));
            Assert.AreEqual(0, Min(5), "5倍は止まることがある");
            Assert.AreEqual(2, Enumerable.Range(0, 5).Count(i => DerbyCards.Steps(8, i) == 0), "8倍は休みが 2 枚");
        }

        [Test]
        public void Ranking_BreaksCoinTiesByHits()
        {
            var s = DerbySetup.Create(3, 1);
            s.Players[0].Coins = s.Players[1].Coins = 20;
            s.Players[2].Coins = 5;
            s.Players[1].Hits = 2;
            CollectionAssert.AreEqual(new[] { 1, 0, 2 }, DerbyRules.Ranking(s));
        }

        [Test]
        public void Bet_IsLimitedByRangeAndCoins()
        {
            var s = DerbySetup.Create(2, 1);
            Assert.IsFalse(_rules.IsLegal(s, DerbyMove.Bet(0, 0)));
            Assert.IsFalse(_rules.IsLegal(s, DerbyMove.Bet(0, 6)));
            Assert.IsFalse(_rules.IsLegal(s, DerbyMove.Bet(6, 1)));
            Assert.IsTrue(_rules.IsLegal(s, DerbyMove.Bet(5, 5)));
            s.Players[0].Coins = 2;
            Assert.IsFalse(_rules.IsLegal(s, DerbyMove.Bet(0, 3)));
        }

        [Test]
        public void Bets_AreHiddenUntilEveryoneDecided()
        {
            var s = DerbySetup.Create(3, 2);
            s = _rules.ApplyMove(s, DerbyMove.Bet(4, 5));
            var other = DerbyPlayerView.Create(s, 1);
            Assert.AreEqual(-1, other.BetRunner(0));
            Assert.AreEqual(0, other.BetAmount(0));
            Assert.AreEqual(10, other.Coins(0), "賭けた額がコインの差から分からない");
            Assert.IsTrue(other.HasDecided(0));
            Assert.AreEqual(4, DerbyPlayerView.Create(s, 0).BetRunner(0));

            s = _rules.ApplyMove(s, DerbyMove.Bet(1, 1));
            s = _rules.ApplyMove(s, DerbyMove.Bet(2, 1));
            Assert.AreEqual(DerbyPhase.Trick, s.Phase);
            Assert.AreEqual(4, DerbyPlayerView.Create(s, 1).BetRunner(0));
            Assert.AreEqual(5, s.Events.Count(e => e.Kind == EventKind.BetShown && e.Player == 0) * 5);
        }

        [Test]
        public void View_HidesDeckHandsAndTrickTargets()
        {
            var s = ToTrickPhase(3, 3);
            int card = s.Players[0].Hand[0];
            int target = DerbyCards.NeedsTarget(DerbyCards.TrickOf(card)) ? 2 : -1;
            s = _rules.ApplyMove(s, DerbyMove.Trick(card, target));
            var v = DerbyPlayerView.Create(s, 1);
            Assert.IsTrue(v.Masked.Deck.All(c => c == -1));
            Assert.IsTrue(v.Masked.DrawPile.All(c => c == -1));
            Assert.IsTrue(v.Masked.Players[0].Hand.All(c => c == -1));
            Assert.IsTrue(v.Masked.TrickTarget.All(x => x == -1));
            Assert.AreEqual(0, v.Masked.Seed);
            CollectionAssert.AreEqual(s.Players[1].Hand, v.MyHand);
        }

        [Test]
        public void Trick_RequiresCardInHandAndCorrectTarget()
        {
            var s = ToTrickPhase(2, 4);
            int notMine = s.Players[1].Hand[0];
            Assert.IsFalse(_rules.IsLegal(s, DerbyMove.Trick(notMine, 0)));
            int tail = DerbyLogic.FirstTrick + (int)TrickKind.Tailwind * DerbyLogic.TrickCopies;
            s.Players[0].Hand[0] = tail;
            Assert.IsTrue(_rules.IsLegal(s, DerbyMove.Trick(tail, -1)));
            Assert.IsFalse(_rules.IsLegal(s, DerbyMove.Trick(tail, 2)));
            int dash = DerbyLogic.FirstTrick;
            s.Players[0].Hand[1] = dash;
            Assert.IsFalse(_rules.IsLegal(s, DerbyMove.Trick(dash, -1)));
            Assert.IsTrue(_rules.IsLegal(s, DerbyMove.Trick(dash, 5)));
        }

        [Test]
        public void Payout_FirstPaysOddsSecondRefundsOthersLose()
        {
            Assert.AreEqual(15, DerbyRules.Payout(1, 3, 5, 1, 2));
            Assert.AreEqual(3, DerbyRules.Payout(2, 3, 5, 1, 2));
            Assert.AreEqual(0, DerbyRules.Payout(3, 3, 5, 1, 2));
            Assert.AreEqual(0, DerbyRules.Payout(-1, 0, 0, 1, 2));
        }

        [Test]
        public void ChaseOdds_FollowCurrentRank()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 5, 5, 5 }, Enumerable.Range(0, 6).Select(DerbyCards.ChaseOdds).ToArray());
            var s = DerbySetup.Create(2, 1);
            s.Position[0] = 8;
            s.Position[1] = 5;
            s.Position[2] = 5;
            s.Position[3] = 1;
            Assert.AreEqual(0, s.RankOf(0));
            Assert.AreEqual(1, s.RankOf(1));
            Assert.AreEqual(1, s.RankOf(2), "同じマスは同じ順位");
            Assert.AreEqual(3, s.RankOf(3));
        }

        [Test]
        public void Chase_HappensOncePerRaceAfterLeaderPassesLine([Values(2, 5)] int players)
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var s = PlayOut(players, seed);
                for (int race = 1; race <= 3; race++)
                {
                    var inRace = EventsOfRace(s, race);
                    Assert.LessOrEqual(inRace.Count(e => e.Kind == EventKind.ChaseStart), 1);
                    int chase = inRace.FindIndex(e => e.Kind == EventKind.ChaseStart);
                    if (chase < 0) continue;
                    var before = inRace.Take(chase).Where(e => e.Kind == EventKind.Moved).ToList();
                    Assert.IsTrue(before.Any(e => e.Amount2 > DerbyLogic.ChaseLine));
                    Assert.IsFalse(inRace.Take(chase).Any(e => e.Kind == EventKind.Finished));
                }
            }
        }

        static List<DerbyEvent> EventsOfRace(DerbyState s, int race)
        {
            int start = s.Events.FindIndex(e => e.Kind == EventKind.RaceStart && e.Amount == race);
            int end = s.Events.FindIndex(e => e.Kind == EventKind.RaceEnd && e.Amount == race);
            return s.Events.Skip(start).Take(end - start + 1).ToList();
        }

        [Test]
        public void Rescue_TopsUpLowPlayersAtRaceStart()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var s = PlayOut(3, seed, st =>
                {
                    if (st.Phase == DerbyPhase.Bet && st.Players.All(p => !p.Decided) && !st.Result.IsOver)
                        Assert.IsTrue(st.Players.All(p => p.Coins >= DerbyLogic.RescueCoins));
                });
                Assert.IsTrue(s.Players.All(p => p.Coins >= 0));
            }
        }

        [Test]
        public void FullGames_EndAfterThreeRacesWithConsistentCoins([Values(2, 3, 4, 5)] int players)
        {
            for (int seed = 0; seed < 150; seed++)
            {
                var s = PlayOut(players, seed, st =>
                {
                    var hands = st.Players.SelectMany(p => p.Hand).ToList();
                    var inDeck = st.Deck.Where(DerbyCards.IsTrick);
                    var all = hands.Concat(st.DrawPile).Concat(inDeck).ToList();
                    Assert.AreEqual(all.Count, all.Distinct().Count(), "いたずら札が重複している");
                });
                Assert.AreEqual(DerbyPhase.Over, s.Phase);
                Assert.AreEqual(3, s.Events.Count(e => e.Kind == EventKind.RaceEnd));
                Assert.AreEqual(1, s.Events.Count(e => e.Kind == EventKind.GameEnd));
                for (int p = 0; p < players; p++)
                {
                    int rescued = s.Events.Where(e => e.Kind == EventKind.Rescued && e.Player == p).Sum(e => e.Amount);
                    int net = s.Events.Where(e => e.Kind == EventKind.Payout && e.Player == p).Sum(e => e.Amount2);
                    Assert.AreEqual(10 + rescued + net, s.Players[p].Coins);
                }
                for (int race = 1; race <= 3; race++)
                {
                    var finished = EventsOfRace(s, race).Where(e => e.Kind == EventKind.Finished).ToList();
                    Assert.AreEqual(3, finished.Count);
                    Assert.AreEqual(3, finished.Select(e => e.Card).Distinct().Count());
                }
            }
        }

        [Test]
        public void SameSeedAndMoves_GiveSameGame()
        {
            string Run()
            {
                var s = PlayOut(4, 77);
                return string.Join(";", s.Events.Select(e => e.ToString()));
            }
            Assert.AreEqual(Run(), Run());
        }

        // ---------- 通信 ----------

        [Test]
        public void Snapshot_RoundTripsWithoutLeaks([Values(2, 3, 5)] int players)
        {
            int checks = 0;
            PlayOut(players, 10 + players, s =>
            {
                for (int viewer = 0; viewer < players; viewer++)
                {
                    var original = MatchSnapshot.For(s, viewer);
                    string text = DerbyNetProtocol.EncodeSnapshot(original);
                    Assert.IsTrue(DerbyNetProtocol.TryDecodeSnapshot(text, out var decoded), text);
                    Assert.AreEqual(text, DerbyNetProtocol.EncodeSnapshot(decoded), "文字列に戻すと同じ");
                    checks++;
                }
            });
            Assert.Greater(checks, players * 10);
        }

        [Test]
        public void Snapshot_RejectsAnotherPlayersHiddenBet()
        {
            var s = DerbySetup.Create(2, 1);
            s = _rules.ApplyMove(s, DerbyMove.Bet(3, 2));
            string text = DerbyNetProtocol.EncodeSnapshot(MatchSnapshot.For(s, 0));
            var parts = text.Split('|');
            var players = parts[18].Split(';');
            var other = players[1].Split(':');
            other[2] = "1";
            other[3] = "2";
            players[1] = string.Join(":", other);
            parts[18] = string.Join(";", players);
            s = _rules.ApplyMove(DerbySetup.Create(2, 1), DerbyMove.Bet(3, 2));
            Assert.IsFalse(DerbyNetProtocol.TryDecodeSnapshot(string.Join("|", parts), out _));
        }

        [Test]
        public void Move_RoundTrips()
        {
            foreach (var m in new[] { DerbyMove.Bet(5, 3), DerbyMove.Trick(40, 2), DerbyMove.Trick(55, -1), DerbyMove.Chase(1, 2), DerbyMove.Pass })
            {
                Assert.IsTrue(DerbyNetProtocol.TryDecodeMove(DerbyNetProtocol.EncodeMove(m), out var back));
                Assert.AreEqual(m, back);
            }
            Assert.IsFalse(DerbyNetProtocol.TryDecodeMove("M|1|9|0|0", out _));
            Assert.IsFalse(DerbyNetProtocol.TryDecodeMove("M|1|0|0", out _));
        }

        [Test]
        public void Table_TakeoverFinishesTheGame([Values(2, 5)] int players)
        {
            var table = new DerbyTable(players, 9);
            for (int seat = 1; seat < players; seat++) Assert.IsTrue(table.Takeover(seat));
            Assert.IsTrue(table.Takeover(0));
            Assert.IsTrue(table.IsOver);
            Assert.AreEqual(players, table.Ranks.Distinct().Count(), "CPU に代わった順に順位がつく");
        }

        [Test]
        public void Table_RejectsWrongSeatAndIllegalMove()
        {
            var table = new DerbyTable(3, 2);
            Assert.IsFalse(table.TryApply(1, DerbyMove.Bet(0, 1)));
            Assert.IsFalse(table.TryApply(0, DerbyMove.Bet(0, 9)));
            Assert.IsTrue(table.TryApply(0, DerbyMove.Bet(0, 1)));
        }
    }
}

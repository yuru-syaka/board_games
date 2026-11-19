using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.GhostGold.Tests
{
    /// <summary>
    /// テスト用の局面を作る(カード ID: 0..13 金貨 1..14 / 14..16 ポルターガイスト / 17..19 鬼火 / 20..22 呪いの鏡 /
    /// 23..25 底なし井戸 / 26..30 秘宝 1..5 夜目)。
    /// </summary>
    static class TestState
    {
        public const int Gold1 = 0, Gold2 = 1, Gold3 = 2, Gold5 = 4, Gold7 = 6, Gold10 = 9, Gold14 = 13;
        public const int Polter = 14, Polter2 = 15, Wisp = 17, Mirror = 20, Well = 23;
        public const int Relic1 = 26, Relic2 = 27;

        /// <summary>夜の途中の局面: 通路 path はめくり済み、deckTopFirst は上から順の山札。全員が探検中で、席 0 から決める。</summary>
        public static GoldState Make(int players, int[] path, int[] deckTopFirst, int night = 1)
        {
            var s = new GoldState(players) { Night = night, Phase = GoldPhase.Decide, CurrentPlayer = 0, Seed = 7 };
            foreach (var p in s.Players) p.Exploring = true;
            s.Path.AddRange(path);
            s.Deck.AddRange(deckTopFirst.Reverse());
            foreach (int c in path.Concat(deckTopFirst))
                if (GoldCards.KindOf(c) == CardKind.Relic)
                    s.Relics[GoldCards.RelicNight(c) - 1] = path.Contains(c) ? RelicStatus.OnPath : RelicStatus.InDeck;
            return s;
        }

        /// <summary>全員の決定を順に出す(席の番号の順)。</summary>
        public static GoldState Decide(GoldState s, params Choice[] choices)
        {
            var rules = new GoldRules();
            foreach (var c in choices) s = rules.ApplyMove(s, GoldMove.Create(c));
            return s;
        }
    }

    public class GoldRulesTests
    {
        const Choice Go = Choice.Go, Back = Choice.Return;
        readonly GoldRules _rules = new GoldRules();

        static GoldEvent Last(GoldState s, EventKind kind) => s.Events.Last(e => e.Kind == kind);

        [Test]
        public void Cards_Are31CardsOf3Kinds()
        {
            Assert.AreEqual(31, GoldLogic.CardCount);
            Assert.AreEqual(14, Enumerable.Range(0, 31).Count(c => GoldCards.KindOf(c) == CardKind.Treasure));
            Assert.AreEqual(12, Enumerable.Range(0, 31).Count(c => GoldCards.KindOf(c) == CardKind.Hazard));
            Assert.AreEqual(new[] { 6, 8, 10, 12, 14 }, Enumerable.Range(1, 5).Select(n => GoldCards.RelicValue(GoldCards.RelicCard(n))).ToArray());
            Assert.AreEqual(Enumerable.Range(1, 14).Sum(), Enumerable.Range(0, 14).Sum(GoldCards.TreasureOf));
            for (int k = 0; k < 4; k++)
                Assert.AreEqual(3, Enumerable.Range(0, 31).Count(c => GoldCards.HazardOf(c) == k));
        }

        [Test]
        public void Setup_RevealsTheFirstCardOfNight1()
        {
            var s = GoldSetup.Create(3, 123);
            Assert.AreEqual(1, s.Night);
            Assert.AreEqual(1, s.Path.Count);
            Assert.AreEqual(14 + 12 + 1, s.Deck.Count + s.Path.Count); // 金貨 14・怪異 12・1 夜目の秘宝
            Assert.AreEqual(0, s.CurrentPlayer);
            Assert.IsTrue(s.Players.All(p => p.Exploring && p.HasAmulet));
            Assert.AreEqual(RelicStatus.InDeck == s.Relics[0] || RelicStatus.OnPath == s.Relics[0], true);
        }

        [Test]
        public void Setup_IsTheSameForTheSameSeed()
        {
            var a = GoldSetup.Create(4, 99);
            var b = GoldSetup.Create(4, 99);
            CollectionAssert.AreEqual(a.Deck, b.Deck);
            CollectionAssert.AreEqual(a.Path, b.Path);
        }

        [Test]
        public void Treasure_IsSplitAmongExplorers_AndTheRestStaysOnThePath()
        {
            var s = TestState.Make(3, new[] { TestState.Gold1 }, new[] { TestState.Gold7, TestState.Gold2 });
            s = TestState.Decide(s, Go, Go, Go); // 金貨 7 → 3 人で 2 枚ずつ、余り 1
            Assert.AreEqual(new[] { 2, 2, 2 }, s.Players.Select(p => p.Carry).ToArray());
            Assert.AreEqual(1, s.Leftover);
            var e = Last(s, EventKind.Reveal);
            Assert.AreEqual(TestState.Gold7, e.Card);
            Assert.AreEqual(2, e.Amount);
            Assert.AreEqual(1, e.Amount2);
        }

        [Test]
        public void Decisions_AreTakenInSeatOrder_AndRevealedTogether()
        {
            var s = TestState.Make(3, new[] { TestState.Gold1 }, new[] { TestState.Gold7, TestState.Gold2 });
            s = _rules.ApplyMove(s, GoldMove.Return);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.IsFalse(s.Events.Any(e => e.Kind == EventKind.Chose));
            s = _rules.ApplyMove(s, GoldMove.Go);
            Assert.AreEqual(2, s.CurrentPlayer);
            s = _rules.ApplyMove(s, GoldMove.Go);
            Assert.AreEqual(3, s.Events.Count(e => e.Kind == EventKind.Chose));
            Assert.IsFalse(s.Players[0].Exploring);
            Assert.AreEqual(1, s.CurrentPlayer); // 次の 1 枚のあと、探検中の 1 番から
        }

        [Test]
        public void OtherPlayersDecisions_AreHiddenUntilEveryoneHasDecided()
        {
            var s = TestState.Make(3, new[] { TestState.Gold1 }, new[] { TestState.Gold7 });
            s = _rules.ApplyMove(s, GoldMove.Return);
            var other = GoldPlayerView.Create(s, 1);
            Assert.IsTrue(other.HasDecided(0));
            Assert.AreEqual(Choice.None, other.Masked.Players[0].Choice);
            Assert.AreEqual(Choice.Return, GoldPlayerView.Create(s, 0).MyChoice);
            Assert.IsTrue(other.Masked.Deck.All(c => c == -1));
        }

        [Test]
        public void ReturningAlone_TakesTheLeftoverAndTheRelics()
        {
            var s = TestState.Make(3, new[] { TestState.Gold3, TestState.Relic1 }, new[] { TestState.Gold5 });
            s.Leftover = 5;
            s.Players[0].Carry = 4;
            s = TestState.Decide(s, Back, Go, Go);
            var me = s.Players[0];
            Assert.IsFalse(me.Exploring);
            Assert.AreEqual(9, me.Safe); // 手持ち 4 + 余り 5
            CollectionAssert.AreEqual(new[] { TestState.Relic1 }, me.Relics);
            Assert.AreEqual(RelicStatus.Taken, s.Relics[0]);
            Assert.AreEqual(6 + 9, me.Score);
            // 金貨 5 は残りの 2 人で 2 枚ずつ、余り 1
            Assert.AreEqual(2, s.Players[1].Carry);
            Assert.AreEqual(1, s.Leftover);
        }

        [Test]
        public void ReturningTogether_SplitsTheLeftover_AndLeavesTheRelics()
        {
            var s = TestState.Make(3, new[] { TestState.Gold3, TestState.Relic1 }, new[] { TestState.Gold5 });
            s.Leftover = 5;
            s = TestState.Decide(s, Back, Back, Go);
            Assert.AreEqual(2, s.Players[0].Safe);
            Assert.AreEqual(2, s.Players[1].Safe);
            Assert.AreEqual(0, s.Players[0].Relics.Count);
            Assert.AreEqual(RelicStatus.OnPath, s.Relics[0]);
            // 金貨 5 は 1 人で全部。余りは 5 − 4 = 1 + 0
            Assert.AreEqual(5, s.Players[2].Carry);
            Assert.AreEqual(1, s.Leftover);
        }

        [Test]
        public void TheSameHazardTwice_ScaresEveryoneExploring_AndRemovesOneCopy()
        {
            var s = TestState.Make(3, new[] { TestState.Polter, TestState.Gold3 }, new[] { TestState.Polter2, TestState.Gold5 });
            s.Players[0].Carry = 6;
            s.Players[1].Carry = 6;
            s.Players[1].HasAmulet = false;
            s.Players[2].Carry = 6;
            s = TestState.Decide(s, Back, Go, Go);
            Assert.AreEqual(6, s.Players[0].Safe);  // 先に帰った人は守れた
            Assert.AreEqual(0, s.Players[1].Safe);  // お守りなし: 全部失う
            Assert.AreEqual(3, s.Players[2].Safe);  // お守りあり: 半分を金庫へ
            Assert.IsFalse(s.Players[2].HasAmulet);
            Assert.AreEqual(1, s.HazardRemoved[Hazard.Poltergeist]);
            Assert.AreEqual(NightEndReason.Scared, Last(s, EventKind.NightEnd).Amount2);
            Assert.AreEqual(6, s.Events.Single(e => e.Kind == EventKind.Lost && e.Player == 1).Amount);
            Assert.AreEqual(3, s.Events.Single(e => e.Kind == EventKind.Amulet && e.Player == 2).Amount);
            // 次の夜: 怪異が 1 枚減って、2 夜目の秘宝が入る
            Assert.AreEqual(2, s.Night);
            Assert.AreEqual(14 + 11 + 1, s.Deck.Count + s.Path.Count);
            Assert.AreEqual(RelicStatus.InDeck == s.Relics[1] || RelicStatus.OnPath == s.Relics[1], true);
        }

        [Test]
        public void TheAmulet_IsKeptWhenThereIsTooLittleToSave()
        {
            var s = TestState.Make(2, new[] { TestState.Wisp }, new[] { TestState.Wisp + 1 });
            s.Players[0].Carry = 1;
            s = TestState.Decide(s, Go, Go);
            Assert.IsTrue(s.Players[0].HasAmulet);
            Assert.AreEqual(0, s.Players[0].Safe);
        }

        [Test]
        public void DifferentHazards_DoNotScare()
        {
            var s = TestState.Make(2, new[] { TestState.Polter }, new[] { TestState.Wisp, TestState.Gold2 });
            s = TestState.Decide(s, Go, Go);
            Assert.AreEqual(1, s.Night);
            Assert.AreEqual(0, s.CurrentPlayer);
            Assert.AreEqual(2, s.Path.Count);
        }

        [Test]
        public void WhenEveryoneReturns_TheNightEnds_AndRelicsOnThePathAreLost()
        {
            var s = TestState.Make(2, new[] { TestState.Gold3, TestState.Relic1 }, new[] { TestState.Gold5 });
            s = TestState.Decide(s, Back, Back);
            Assert.AreEqual(RelicStatus.Lost, s.Relics[0]);
            Assert.AreEqual(NightEndReason.AllReturned, s.Events.Last(e => e.Kind == EventKind.NightEnd).Amount2);
            Assert.AreEqual(2, s.Night);
        }

        [Test]
        public void WhenTheDeckRunsOut_EveryoneExploringGoesHome()
        {
            var s = TestState.Make(2, new[] { TestState.Gold3 }, new int[0]);
            s.Players[0].Carry = 4;
            s.Players[1].Carry = 4;
            s.Leftover = 3;
            s = TestState.Decide(s, Go, Go);
            Assert.AreEqual(5, s.Players[0].Safe); // 4 + 余り 3 を 2 人で 1 枚
            Assert.AreEqual(5, s.Players[1].Safe);
            Assert.IsTrue(s.Events.Any(e => e.Kind == EventKind.DeckEmpty));
            Assert.AreEqual(2, s.Night);
        }

        [Test]
        public void UnrevealedRelics_StayInTheDeckForLaterNights()
        {
            var s = TestState.Make(2, new[] { TestState.Gold3 }, new[] { TestState.Gold5, TestState.Relic1 });
            s = TestState.Decide(s, Back, Back);
            Assert.AreEqual(2, s.Night);
            Assert.AreNotEqual(RelicStatus.Lost, s.Relics[0]);
            Assert.AreEqual(14 + 12 + 2, s.Deck.Count + s.Path.Count);
        }

        [Test]
        public void AfterTheFifthNight_TheHighestScoreWins()
        {
            var s = TestState.Make(3, new[] { TestState.Gold3 }, new int[0], night: 5);
            s.Players[0].Safe = 20;
            s.Players[1].Safe = 14;
            s.Players[1].Relics.Add(TestState.Relic1); // 14 + 6 = 20、秘宝が多い
            s.Players[2].Safe = 25;
            s.Players[2].Carry = 1;
            s = TestState.Decide(s, Back, Back, Go); // 2 番は探検を続けるが、山札が尽きて帰る(25 + 1 = 26)
            Assert.IsTrue(s.Result.IsOver);
            Assert.AreEqual(GoldPhase.Over, s.Phase);
            Assert.AreEqual(-1, s.CurrentPlayer);
            Assert.AreEqual(GameResult.WinFor(2), s.Result);
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, GoldRules.Ranking(s));
            Assert.AreEqual(0, _rules.GetLegalMoves(s).Count);
        }

        [Test]
        public void ATieOnScoreAndRelics_IsADraw()
        {
            var s = TestState.Make(2, new[] { TestState.Gold3 }, new int[0], night: 5);
            s.Players[0].Safe = 10;
            s.Players[1].Safe = 10;
            s = TestState.Decide(s, Back, Back);
            Assert.AreEqual(GameResult.Draw, s.Result);
            CollectionAssert.AreEqual(new[] { 0, 0 }, GoldRules.Ranking(s));
        }

        [Test]
        public void IllegalMoves_AreRejected()
        {
            var s = TestState.Make(2, new[] { TestState.Gold3 }, new[] { TestState.Gold5 });
            Assert.IsFalse(_rules.IsLegal(s, GoldMove.Create(Choice.None)));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, GoldMove.Create(Choice.None)));
            Assert.AreEqual(2, _rules.GetLegalMoves(s).Count);
        }

        [Test]
        public void ApplyMove_DoesNotChangeTheOldState()
        {
            var s = TestState.Make(2, new[] { TestState.Gold3 }, new[] { TestState.Gold7 });
            var before = s.Clone();
            _rules.ApplyMove(s, GoldMove.Go);
            Assert.AreEqual(before.CurrentPlayer, s.CurrentPlayer);
            Assert.AreEqual(before.Events.Count, s.Events.Count);
            Assert.IsFalse(s.Players[0].Decided);
        }

        [Test]
        public void View_CountsWhatIsLeftInTheDeck()
        {
            var s = TestState.Make(2, new[] { TestState.Polter, TestState.Gold14 }, new[] { TestState.Gold5 });
            var v = GoldPlayerView.Create(s, 0);
            Assert.AreEqual(13, v.TreasuresInDeck().Count);
            Assert.IsFalse(v.TreasuresInDeck().Contains(14));
            Assert.AreEqual(2, v.HazardsInDeck(Hazard.Poltergeist));
            Assert.AreEqual(3, v.HazardsInDeck(Hazard.Well));
        }
    }
}

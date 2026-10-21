using System.Linq;
using NUnit.Framework;
using static BoardGames.BattleLine.Tests.TestBoard;

namespace BoardGames.BattleLine.Tests
{
    public class TacticsTests
    {
        readonly BattleLineRules _rules = new BattleLineRules();

        [Test]
        public void Leader_PlayedIntoFormation()
        {
            var s = Empty();
            Hand(s, 0, Card.LeaderA);
            s = _rules.ApplyMove(s, BattleLineMove.Place(Card.LeaderA, 1));
            CollectionAssert.Contains(s.Flags[1].CardsOf(0), Card.LeaderA);
            Assert.IsTrue(s.LeaderPlayed(0));
            Assert.AreEqual(1, s.TacticsPlayed(0));
        }

        [Test]
        public void Fog_DecidesBySum()
        {
            var s = Empty();
            Put(s, 1, 0, T(B, 1), T(B, 2), T(B, 3));
            Put(s, 0, 0, T(R, 10), T(G, 9));
            Env(s, 1, 0, Card.Fog);
            Hand(s, 0, T(Y, 1));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(Y, 1), 0));
            Assert.AreEqual(0, s.Flags[0].Claimer, "霧: 20 > 6");
        }

        [Test]
        public void Fog_PlacedOnFlag()
        {
            var s = Empty();
            Hand(s, 0, T(R, 1), Card.Fog);
            s = _rules.ApplyMove(s, BattleLineMove.Place(Card.Fog, 6));
            Assert.IsTrue(s.Flags[6].HasFog);
            Assert.AreEqual(0, s.Flags[6].CardsOf(0).Count, "隊列の枚数には数えない");
            Assert.AreEqual(0, s.Flags[6].EnvironmentOwner(0));
        }

        [Test]
        public void Mud_RequiresFourCards()
        {
            var s = Empty();
            Put(s, 0, 0, T(R, 1), T(R, 2), T(R, 3));
            Env(s, 1, 0, Card.Mud);
            Assert.IsFalse(s.Flags[0].IsComplete(0));
            Hand(s, 0, T(R, 4));
            Assert.IsTrue(_rules.GetLegalMoves(s).Any(m => m.Kind == MoveKind.Place && m.Flag == 0), "4 枚目を置ける");
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 4), 0));
            Assert.IsTrue(s.Flags[0].IsComplete(0));
            Assert.AreEqual(4, s.Flags[0].CardsOf(0).Count);
        }

        [Test]
        public void Mud_PlayedResetsCompletion()
        {
            var s = Empty();
            Put(s, 1, 0, T(B, 1), T(B, 2), T(B, 3));
            Hand(s, 0, Card.Mud);
            s = _rules.ApplyMove(s, BattleLineMove.Place(Card.Mud, 0));
            Assert.AreEqual(-1, s.Flags[0].CompletedSeq(1));
            Assert.AreEqual(-1, s.Flags[0].Claimer);
        }

        [Test]
        public void Scout_DrawThreeReturnTwo()
        {
            var s = Fresh(0);
            Hand(s, 0, T(R, 1), T(R, 2), T(R, 3), T(R, 4), T(R, 5), T(R, 6), Card.Scout);
            int troopBefore = s.TroopDeck.Count, tacticsBefore = s.TacticsDeck.Count;

            s = _rules.ApplyMove(s, BattleLineMove.UseScout(Card.Scout));
            Assert.AreEqual(TurnPhase.ScoutDraw, s.Phase);
            Assert.AreEqual(3, s.PendingCount);
            s = _rules.ApplyMove(s, BattleLineMove.Draw(DeckKind.Troop));
            s = _rules.ApplyMove(s, BattleLineMove.Draw(DeckKind.Tactics));
            s = _rules.ApplyMove(s, BattleLineMove.Draw(DeckKind.Troop));
            Assert.AreEqual(TurnPhase.ScoutReturn, s.Phase);
            Assert.AreEqual(9, s.HandOf(0).Count);

            int tactic = s.HandOf(0).First(Card.IsTactic);
            s = _rules.ApplyMove(s, BattleLineMove.ReturnCard(tactic));
            s = _rules.ApplyMove(s, BattleLineMove.ReturnCard(T(R, 1)));
            Assert.AreEqual(1, s.CurrentPlayer, "偵察の後は通常のドローなしで手番終了");
            Assert.AreEqual(7, s.HandOf(0).Count);
            Assert.AreEqual(T(R, 1), s.TroopDeck.Last(), "山札の上に戻る");
            Assert.AreEqual(tactic, s.TacticsDeck.Last());
            Assert.AreEqual(troopBefore - 1, s.TroopDeck.Count);
            Assert.AreEqual(tacticsBefore, s.TacticsDeck.Count, "1 枚引いて 1 枚戻した");
            CollectionAssert.Contains(s.Discard, Card.Scout);
            Assert.AreEqual(70, CountAll(s));
        }

        [Test]
        public void Scout_NotAllowedWithEmptyDecks()
        {
            var s = Empty();
            Hand(s, 0, Card.Scout);
            s.Deck(DeckKind.Troop).Clear();
            s.Deck(DeckKind.Tactics).Clear();
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.Kind == MoveKind.Scout));
        }

        [Test]
        public void Redeploy_MovesOwnCard()
        {
            var s = Empty();
            Put(s, 0, 0, T(R, 5));
            Hand(s, 0, Card.Redeploy);
            s = _rules.ApplyMove(s, BattleLineMove.UseRedeploy(Card.Redeploy, 0, T(R, 5), 3));
            Assert.AreEqual(0, s.Flags[0].CardsOf(0).Count);
            CollectionAssert.Contains(s.Flags[3].CardsOf(0), T(R, 5));
            CollectionAssert.Contains(s.Discard, Card.Redeploy);
        }

        [Test]
        public void Redeploy_CanDiscard()
        {
            var s = Empty();
            Put(s, 0, 0, T(R, 5));
            Hand(s, 0, Card.Redeploy);
            s = _rules.ApplyMove(s, BattleLineMove.UseRedeploy(Card.Redeploy, 0, T(R, 5), -1));
            CollectionAssert.Contains(s.Discard, T(R, 5));
        }

        [Test]
        public void Redeploy_OnlyOwnCardsOnUnclaimedFlags()
        {
            var s = Empty();
            Put(s, 0, 0, T(R, 5));
            Put(s, 0, 1, T(R, 6));
            Put(s, 1, 2, T(B, 5));
            Claim(s, 1, 1);
            Hand(s, 0, Card.Redeploy);
            var targets = _rules.GetLegalMoves(s).Where(m => m.Kind == MoveKind.Redeploy).Select(m => m.Target).Distinct().ToList();
            CollectionAssert.AreEquivalent(new[] { T(R, 5) }, targets);
        }

        [Test]
        public void Deserter_DiscardsOpponentCard()
        {
            var s = Empty();
            Put(s, 1, 2, T(B, 5), T(B, 6));
            Hand(s, 0, Card.Deserter);
            s = _rules.ApplyMove(s, BattleLineMove.UseDeserter(Card.Deserter, 2, T(B, 6)));
            CollectionAssert.AreEqual(new[] { T(B, 5) }, s.Flags[2].CardsOf(1));
            CollectionAssert.Contains(s.Discard, T(B, 6));
            CollectionAssert.Contains(s.Discard, Card.Deserter);
        }

        [Test]
        public void Deserter_RemovingMudTrimsFourthCard()
        {
            var s = Empty();
            Env(s, 1, 0, Card.Mud);
            Put(s, 1, 0, T(B, 1), T(B, 2), T(B, 3), T(B, 4));
            Put(s, 0, 0, T(R, 1));
            Hand(s, 0, Card.Deserter);
            s = _rules.ApplyMove(s, BattleLineMove.UseDeserter(Card.Deserter, 0, Card.Mud));
            CollectionAssert.AreEqual(new[] { T(B, 1), T(B, 2), T(B, 3) }, s.Flags[0].CardsOf(1));
            CollectionAssert.Contains(s.Discard, T(B, 4));
            CollectionAssert.Contains(s.Discard, Card.Mud);
            Assert.AreEqual(70, CountAll(s));
        }

        [Test]
        public void Traitor_StealsTroopOnly()
        {
            var s = Empty();
            Put(s, 1, 2, T(B, 9), Card.LeaderB);
            Hand(s, 0, Card.Traitor);
            var targets = _rules.GetLegalMoves(s).Where(m => m.Kind == MoveKind.Traitor).Select(m => m.Target).Distinct().ToList();
            CollectionAssert.AreEquivalent(new[] { T(B, 9) }, targets, "戦術カードは奪えない");

            s = _rules.ApplyMove(s, BattleLineMove.UseTraitor(Card.Traitor, 2, T(B, 9), 2));
            CollectionAssert.Contains(s.Flags[2].CardsOf(0), T(B, 9));
            CollectionAssert.DoesNotContain(s.Flags[2].CardsOf(1), T(B, 9));
        }

        [Test]
        public void ClaimedFlag_IsNotTargetable()
        {
            var s = Empty();
            Put(s, 1, 2, T(B, 9));
            Claim(s, 1, 2);
            Hand(s, 0, Card.Deserter, Card.Traitor);
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.Kind == MoveKind.Deserter || m.Kind == MoveKind.Traitor));
        }

        [Test]
        public void ScoutReturn_IsHiddenFromOpponent()
        {
            var s = Fresh(0);
            Hand(s, 0, T(R, 1), T(R, 2), T(R, 3), T(R, 4), T(R, 5), T(R, 6), Card.Scout);
            s = _rules.ApplyMove(s, BattleLineMove.UseScout(Card.Scout));
            for (int i = 0; i < 3; i++) s = _rules.ApplyMove(s, BattleLineMove.Draw(DeckKind.Troop));
            s = _rules.ApplyMove(s, BattleLineMove.ReturnCard(T(R, 1)));
            var opponentView = BattleLinePlayerView.Create(s, 1);
            Assert.AreEqual(-1, opponentView.LastMove.Value.Card);
            Assert.AreEqual(DeckKind.Troop, opponentView.LastMove.Value.Deck);
            Assert.AreEqual(T(R, 1), BattleLinePlayerView.Create(s, 0).LastMove.Value.Card);
        }
    }
}

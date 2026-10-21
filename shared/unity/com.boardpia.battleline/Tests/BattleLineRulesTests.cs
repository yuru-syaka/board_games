using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;
using static BoardGames.BattleLine.Tests.TestBoard;

namespace BoardGames.BattleLine.Tests
{
    public class BattleLineRulesTests
    {
        readonly BattleLineRules _rules = new BattleLineRules();

        // ---------- 準備 ----------

        [Test]
        public void Setup_DealsSevenEach()
        {
            var s = BattleLineSetup.CreateRandom(new Random(1));
            Assert.AreEqual(7, s.HandOf(0).Count);
            Assert.AreEqual(7, s.HandOf(1).Count);
            Assert.IsTrue(s.HandOf(0).Concat(s.HandOf(1)).All(Card.IsTroop));
            Assert.AreEqual(46, s.TroopDeck.Count);
            Assert.AreEqual(10, s.TacticsDeck.Count);
            Assert.AreEqual(TurnPhase.Play, s.Phase);
            Assert.AreEqual(70, CountAll(s));
            Assert.AreEqual(GameStatus.Ongoing, _rules.GetResult(s).Status);
        }

        [Test]
        public void Setup_RejectsInvalidDecks()
        {
            var troops = Enumerable.Range(0, 60).ToList();
            var tactics = Enumerable.Range(60, 10).ToList();
            Assert.Throws<ArgumentException>(() => BattleLineSetup.Create(troops.Take(59).ToList(), tactics, 0));
            troops[0] = 1;
            Assert.Throws<ArgumentException>(() => BattleLineSetup.Create(troops, tactics, 0));
        }

        [Test]
        public void FirstPlayer_IsRandomOverGames()
        {
            var rng = new Random(3);
            var firsts = Enumerable.Range(0, 40).Select(_ => BattleLineSetup.CreateRandom(rng).CurrentPlayer).ToList();
            Assert.Contains(0, firsts);
            Assert.Contains(1, firsts);
        }

        // ---------- 合法手 ----------

        [Test]
        public void Troop_CanGoToAnyFlagWithSpace()
        {
            var s = Empty();
            Hand(s, 0, T(R, 5));
            Put(s, 0, 0, T(B, 1), T(B, 2), T(B, 3));
            Claim(s, 1, 8);
            var moves = _rules.GetLegalMoves(s);
            var flags = moves.Where(m => m.Kind == MoveKind.Place).Select(m => m.Flag).ToList();
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6, 7 }, flags, "満杯(0)と確保済み(8)には置けない");
            Assert.IsFalse(moves.Any(m => m.Kind == MoveKind.Pass), "部隊を出せるならパスできない");
        }

        [Test]
        public void Pass_OnlyWhenNoTroopPlayable()
        {
            var s = Empty();
            Hand(s, 0, Card.Fog);
            var moves = _rules.GetLegalMoves(s);
            Assert.IsTrue(moves.Any(m => m.Kind == MoveKind.Pass));
            Assert.IsTrue(moves.Any(m => m.Kind == MoveKind.Place && m.Card == Card.Fog), "戦術は出してもよい");
        }

        [Test]
        public void Tactics_LimitedByOpponentCount()
        {
            var s = Empty();
            Hand(s, 0, T(R, 5), Card.Fog);
            s.SetTacticsPlayed(0, 1);
            s.SetTacticsPlayed(1, 0);
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.Card == Card.Fog), "相手より多くは使えない");
            s.SetTacticsPlayed(1, 1);
            Assert.IsTrue(_rules.GetLegalMoves(s).Any(m => m.Card == Card.Fog), "同数なら使える");
        }

        [Test]
        public void Leader_OnlyOncePerPlayer()
        {
            var s = Empty();
            Hand(s, 0, T(R, 5), Card.LeaderB);
            s.SetLeaderPlayed(0, true);
            s.SetTacticsPlayed(1, 5);
            Assert.IsFalse(_rules.GetLegalMoves(s).Any(m => m.Card == Card.LeaderB));
        }

        [Test]
        public void IllegalMove_Throws()
        {
            var s = Empty();
            Hand(s, 0, T(R, 5));
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, BattleLineMove.Place(T(R, 6), 0)), "手札に無い");
            Assert.Throws<ArgumentException>(() => _rules.ApplyMove(s, BattleLineMove.Draw(DeckKind.Troop)), "出す段階でドローはできない");
        }

        // ---------- 手番の流れ ----------

        [Test]
        public void Turn_PlayThenDrawThenOpponent()
        {
            var s = Fresh(0);
            int card = s.HandOf(0)[0];
            s = _rules.ApplyMove(s, BattleLineMove.Place(card, 4));
            Assert.AreEqual(TurnPhase.Draw, s.Phase);
            Assert.AreEqual(0, s.CurrentPlayer);
            Assert.AreEqual(6, s.HandOf(0).Count);
            CollectionAssert.Contains(s.Flags[4].CardsOf(0), card);

            int top = s.TroopDeck[s.TroopDeck.Count - 1];
            s = _rules.ApplyMove(s, BattleLineMove.Draw(DeckKind.Troop));
            Assert.AreEqual(7, s.HandOf(0).Count);
            CollectionAssert.Contains(s.HandOf(0), top);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(TurnPhase.Play, s.Phase);
            Assert.AreEqual(70, CountAll(s));
        }

        [Test]
        public void Draw_OnlyFromNonEmptyDeck()
        {
            var s = Empty();
            Hand(s, 0, T(R, 5));
            s.Deck(DeckKind.Troop).Clear();
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 5), 0));
            var moves = _rules.GetLegalMoves(s);
            Assert.AreEqual(1, moves.Count);
            Assert.AreEqual(DeckKind.Tactics, moves[0].Deck);
        }

        [Test]
        public void NoDecks_TurnEndsWithoutDraw()
        {
            var s = Empty();
            Hand(s, 0, T(R, 5));
            s.Deck(DeckKind.Troop).Clear();
            s.Deck(DeckKind.Tactics).Clear();
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 5), 0));
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(TurnPhase.Play, s.Phase);
        }

        [Test]
        public void Pass_EndsTurnWithoutDraw()
        {
            var s = Empty();
            Hand(s, 0, Card.Fog);
            s = _rules.ApplyMove(s, BattleLineMove.Pass);
            Assert.AreEqual(1, s.CurrentPlayer);
            Assert.AreEqual(1, s.HandOf(0).Count);
        }

        [Test]
        public void TwoPasses_EndGameByClaimCount()
        {
            var s = Empty();
            Claim(s, 0, 0, 2);
            Claim(s, 1, 5);
            Hand(s, 0);
            Hand(s, 1);
            s = _rules.ApplyMove(s, BattleLineMove.Pass);
            Assert.IsFalse(s.Result.IsOver);
            s = _rules.ApplyMove(s, BattleLineMove.Pass);
            Assert.AreEqual(GameStatus.Win, s.Result.Status);
            Assert.AreEqual(0, s.Result.Winner);
            Assert.AreEqual(EndReason.Stalemate, s.EndReason);
        }

        // ---------- 確保 ----------

        [Test]
        public void Claim_BothCompleteStrongerWins()
        {
            var s = Empty();
            Put(s, 1, 3, T(B, 2), T(G, 2), T(Y, 2));
            Put(s, 0, 3, T(R, 4), T(R, 5));
            Hand(s, 0, T(R, 6));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 6), 3));
            Assert.AreEqual(0, s.Flags[3].Claimer, "同色連番 > 同数字");
        }

        [Test]
        public void Claim_NotWhenWeaker()
        {
            var s = Empty();
            Put(s, 1, 3, T(B, 2), T(G, 2), T(Y, 2));
            Put(s, 0, 3, T(R, 4), T(B, 9));
            Hand(s, 0, T(G, 1));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(G, 1), 3));
            Assert.AreEqual(-1, s.Flags[3].Claimer);
        }

        [Test]
        public void Claim_TieGoesToFirstCompleted()
        {
            var s = Empty();
            Put(s, 1, 3, T(B, 7), T(B, 8), T(B, 9));
            Put(s, 0, 3, T(R, 7), T(R, 8));
            Hand(s, 0, T(R, 9));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 9), 3));
            Assert.AreEqual(-1, s.Flags[3].Claimer, "相手が先に完成している");

            // 相手の手番: 相手は何か出せば確保できる
            s = _rules.ApplyMove(s, BattleLineMove.Draw(DeckKind.Troop));
            Hand(s, 1, T(G, 1));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(G, 1), 0));
            Assert.AreEqual(1, s.Flags[3].Claimer);
        }

        [Test]
        public void Claim_ByProof_TopWedge()
        {
            var s = Empty();
            Put(s, 1, 2, T(B, 1));
            Put(s, 0, 2, T(R, 8), T(R, 9));
            Hand(s, 0, T(R, 10));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 10), 2));
            Assert.AreEqual(0, s.Flags[2].Claimer, "8-9-10 の同色連番は、相手がどう足しても同点が最大で、同点は先に完成した側");
        }

        [Test]
        public void Claim_ByProof_UsesVisibleCards()
        {
            var s = Empty();
            // 相手: 青 4・5。青 3 と青 6 が見えていれば同色連番は作れない → 最強でも同数字は無理、同色(4+5+10=19)
            Put(s, 1, 2, T(B, 4), T(B, 5));
            Put(s, 0, 2, T(R, 1), T(B, 1), T(G, 1));
            Hand(s, 0, T(Y, 1));
            Put(s, 0, 7, T(B, 3));
            Put(s, 0, 8, T(B, 6));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(Y, 1), 0));
            Assert.AreEqual(0, s.Flags[2].Claimer, "同数字(1-1-1) > 同色");
        }

        [Test]
        public void Claim_ByProof_FailsWhenOpponentCanStillWin()
        {
            var s = Empty();
            Put(s, 1, 2, T(B, 4), T(B, 5));
            Put(s, 0, 2, T(R, 1), T(B, 1));
            Hand(s, 0, T(G, 1));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(G, 1), 2));
            Assert.AreEqual(-1, s.Flags[2].Claimer, "青 3 か青 6 で同色連番になりうる");
        }

        [Test]
        public void Claim_ByProof_OpponentCannotComplete()
        {
            var s = Empty();
            Put(s, 0, 4, T(R, 1), T(B, 2), T(G, 4));
            // 見えていない部隊カードが 1 枚も無ければ相手は隊列を完成できない
            Put(s, 1, 4, T(Y, 9), T(Y, 10));
            Hand(s, 0, T(P, 1));
            foreach (int c in s.TroopDeck.ToList()) Discard(s, c);
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(P, 1), 0));
            Assert.AreEqual(0, s.Flags[4].Claimer);
        }

        [Test]
        public void Claim_OnlyByMover()
        {
            var s = Empty(1);
            Put(s, 0, 2, T(R, 8), T(R, 9), T(R, 10));
            Hand(s, 1, T(B, 1));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(B, 1), 5));
            Assert.AreEqual(-1, s.Flags[2].Claimer, "相手の手番では確保されない");
        }

        // ---------- 勝敗 ----------

        [Test]
        public void Win_FiveFlags()
        {
            var s = Empty();
            Claim(s, 0, 0, 2, 4, 6);
            Put(s, 0, 8, T(R, 8), T(R, 9));
            Hand(s, 0, T(R, 10));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 10), 8));
            Assert.AreEqual(GameResult.WinFor(0).ToString(), s.Result.ToString());
            Assert.AreEqual(EndReason.FiveFlags, s.EndReason);
            Assert.AreEqual(0, _rules.GetLegalMoves(s).Count);
        }

        [Test]
        public void Win_Breakthrough()
        {
            var s = Empty();
            Claim(s, 0, 3, 5);
            Put(s, 0, 4, T(R, 8), T(R, 9));
            Hand(s, 0, T(R, 10));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 10), 4));
            Assert.AreEqual(0, s.Result.Winner);
            Assert.AreEqual(EndReason.Breakthrough, s.EndReason);
        }

        [Test]
        public void NotWin_TwoAdjacentPlusOther()
        {
            var s = Empty();
            Claim(s, 0, 3, 4);
            Put(s, 0, 8, T(R, 8), T(R, 9));
            Hand(s, 0, T(R, 10));
            s = _rules.ApplyMove(s, BattleLineMove.Place(T(R, 10), 8));
            Assert.IsFalse(s.Result.IsOver);
        }

        // ---------- 視点 ----------

        [Test]
        public void PlayerView_HidesOpponentHand()
        {
            var s = Fresh(0);
            var v = BattleLinePlayerView.Create(s, 0);
            CollectionAssert.AreEqual(s.HandOf(0), v.Hand);
            Assert.IsNull(v.OpponentHand);
            Assert.AreEqual(7, v.OpponentHandTroops);
            Assert.AreEqual(0, v.OpponentHandTactics);
            Assert.AreEqual(46, v.TroopDeckCount);
            Assert.AreEqual(0, BattleLinePlayerView.Create(s, 1).GetLegalMoves().Count, "手番でない視点は合法手なし");
        }

        [Test]
        public void PlayerView_LegalMovesMatchRules()
        {
            var s = Fresh(0);
            var v = BattleLinePlayerView.Create(s, 0);
            CollectionAssert.AreEquivalent(_rules.GetLegalMoves(s), v.GetLegalMoves());
        }
    }
}

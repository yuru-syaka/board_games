using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.BattleLine
{
    public sealed class BattleLineRules : IGameRules<BattleLineState, BattleLineMove>
    {
        public const int FlagsToWin = 5;
        public const int AdjacentToWin = 3;
        public const int ScoutDraws = 3;
        public const int ScoutReturns = 2;

        public IReadOnlyList<BattleLineMove> GetLegalMoves(BattleLineState state)
        {
            if (state.Result.IsOver) return Array.Empty<BattleLineMove>();
            int me = state.CurrentPlayer;
            return BattleLineLogic.GenerateMoves(state.Flags, state.HandOf(me), me, state.Phase,
                state.DeckCount(DeckKind.Troop), state.DeckCount(DeckKind.Tactics),
                state.TacticsPlayed(me), state.TacticsPlayed(1 - me), state.LeaderPlayed(me));
        }

        public bool IsLegal(BattleLineState state, BattleLineMove move)
        {
            foreach (var m in GetLegalMoves(state))
                if (m.Equals(move)) return true;
            return false;
        }

        public GameResult GetResult(BattleLineState state) => state.Result;

        public BattleLineState ApplyMove(BattleLineState state, BattleLineMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException($"Illegal move: {move}");

            var s = state.Clone();
            int me = s.CurrentPlayer;
            var hand = s.Hand(me);
            s.LastMove = move;
            s.LastMover = me;
            s.Step++;

            switch (move.Kind)
            {
                case MoveKind.Place:
                {
                    hand.Remove(move.Card);
                    var flag = s.FlagAt(move.Flag);
                    if (Card.IsEnvironment(move.Card)) flag.AddEnvironment(move.Card, me);
                    else flag.AddCard(me, move.Card);
                    if (Card.IsTactic(move.Card)) UseTactic(s, me, move.Card);
                    break;
                }
                case MoveKind.Scout:
                    hand.Remove(move.Card);
                    UseTactic(s, me, move.Card);
                    s.DiscardPile.Add(move.Card);
                    break;
                case MoveKind.Redeploy:
                {
                    hand.Remove(move.Card);
                    UseTactic(s, me, move.Card);
                    s.DiscardPile.Add(move.Card);
                    var from = s.FlagAt(move.Flag);
                    from.Remove(move.Target);
                    if (move.ToFlag < 0) s.DiscardPile.Add(move.Target);
                    else if (Card.IsEnvironment(move.Target)) s.FlagAt(move.ToFlag).AddEnvironment(move.Target, me);
                    else s.FlagAt(move.ToFlag).AddCard(me, move.Target);
                    if (move.Target == Card.Mud) TrimAfterMud(s, from);
                    break;
                }
                case MoveKind.Deserter:
                {
                    hand.Remove(move.Card);
                    UseTactic(s, me, move.Card);
                    s.DiscardPile.Add(move.Card);
                    var from = s.FlagAt(move.Flag);
                    from.Remove(move.Target);
                    s.DiscardPile.Add(move.Target);
                    if (move.Target == Card.Mud) TrimAfterMud(s, from);
                    break;
                }
                case MoveKind.Traitor:
                    hand.Remove(move.Card);
                    UseTactic(s, me, move.Card);
                    s.DiscardPile.Add(move.Card);
                    s.FlagAt(move.Flag).Remove(move.Target);
                    s.FlagAt(move.ToFlag).AddCard(me, move.Target);
                    break;
                case MoveKind.Pass:
                    break;
                case MoveKind.Draw:
                {
                    var deck = s.Deck(move.Deck);
                    hand.Add(deck[deck.Count - 1]);
                    deck.RemoveAt(deck.Count - 1);
                    if (s.Phase == TurnPhase.ScoutDraw) AfterScoutDraw(s);
                    else EndTurn(s);
                    return s;
                }
                case MoveKind.ScoutReturn:
                    hand.Remove(move.Card);
                    s.Deck(Card.DeckOf(move.Card)).Add(move.Card);
                    s.PendingCount--;
                    if (s.PendingCount <= 0) EndTurn(s);
                    return s;
            }

            // ---- カードを出した(またはパスした)後: 完成順の更新 → 確保 → 勝敗 → 次の段階 ----
            s.ConsecutivePasses = move.Kind == MoveKind.Pass ? s.ConsecutivePasses + 1 : 0;
            UpdateCompletion(s, me);
            foreach (var f in s.Flags)
                if (!f.IsClaimed && BattleLineLogic.CanClaim(s.Flags, s.Discard, me, f.Index))
                    s.FlagAt(f.Index).SetClaimer(me);

            var reason = BattleLineLogic.WinReason(s.Flags, me);
            if (reason != EndReason.None)
            {
                s.Result = GameResult.WinFor(me);
                s.EndReason = reason;
                return s;
            }
            if (s.ConsecutivePasses >= 2)
            {
                int mine = s.ClaimedCount(me), theirs = s.ClaimedCount(1 - me);
                s.Result = mine == theirs ? GameResult.Draw : GameResult.WinFor(mine > theirs ? me : 1 - me);
                s.EndReason = EndReason.Stalemate;
                return s;
            }

            int decks = s.DeckCount(DeckKind.Troop) + s.DeckCount(DeckKind.Tactics);
            if (move.Kind == MoveKind.Scout)
            {
                s.Phase = TurnPhase.ScoutDraw;
                s.PendingCount = Math.Min(ScoutDraws, decks);
            }
            else if (move.Kind == MoveKind.Pass || decks == 0)
            {
                EndTurn(s);
            }
            else
            {
                s.Phase = TurnPhase.Draw;
            }
            return s;
        }

        static void UseTactic(BattleLineState s, int player, int card)
        {
            s.AddTacticPlayed(player);
            if (Card.KindOf(card) == TacticKind.Leader) s.SetLeaderPlayed(player, true);
        }

        static void AfterScoutDraw(BattleLineState s)
        {
            s.PendingCount--;
            bool decksLeft = s.DeckCount(DeckKind.Troop) + s.DeckCount(DeckKind.Tactics) > 0;
            if (s.PendingCount > 0 && decksLeft) return;
            s.Phase = TurnPhase.ScoutReturn;
            s.PendingCount = Math.Min(ScoutReturns, s.HandOf(s.CurrentPlayer).Count);
            if (s.PendingCount <= 0) EndTurn(s);
        }

        static void EndTurn(BattleLineState s)
        {
            s.CurrentPlayer = 1 - s.CurrentPlayer;
            s.Phase = TurnPhase.Play;
            s.PendingCount = 0;
            s.Turn++;
        }

        /// <summary>泥沼が無くなったフラッグで、4 枚ある隊列は最後に置いたカードを捨てる。</summary>
        static void TrimAfterMud(BattleLineState s, Flag flag)
        {
            for (int p = 0; p < BattleLineState.Players; p++)
            {
                var cards = flag.CardsOf(p);
                while (cards.Count > flag.Required)
                {
                    int last = cards[cards.Count - 1];
                    flag.Remove(last);
                    s.DiscardPile.Add(last);
                }
            }
        }

        /// <summary>
        /// 隊列の完成順を更新する。新しく完成した隊列に通し番号を振り、未完成に戻った隊列は −1 にする
        /// (同時に完成したときは手番でない側を先とする)。
        /// </summary>
        static void UpdateCompletion(BattleLineState s, int mover)
        {
            foreach (var f in s.Flags)
            {
                if (f.IsClaimed) continue;
                foreach (int p in new[] { 1 - mover, mover })
                {
                    if (!f.IsComplete(p)) f.SetCompletedSeq(p, -1);
                    else if (f.CompletedSeq(p) < 0) f.SetCompletedSeq(p, ++s.Seq);
                }
            }
        }
    }

    /// <summary>完全な局面とプレイヤー視点の両方から使う、合法手・確保・勝敗の判定。</summary>
    public static class BattleLineLogic
    {
        public static List<BattleLineMove> GenerateMoves(IReadOnlyList<Flag> flags, IReadOnlyList<int> hand, int player,
            TurnPhase phase, int troopDeck, int tacticsDeck, int myTactics, int opponentTactics, bool leaderPlayed)
        {
            var moves = new List<BattleLineMove>();
            switch (phase)
            {
                case TurnPhase.Draw:
                case TurnPhase.ScoutDraw:
                    if (troopDeck > 0) moves.Add(BattleLineMove.Draw(DeckKind.Troop));
                    if (tacticsDeck > 0) moves.Add(BattleLineMove.Draw(DeckKind.Tactics));
                    return moves;
                case TurnPhase.ScoutReturn:
                    foreach (int c in hand) moves.Add(BattleLineMove.ReturnCard(c));
                    return moves;
            }

            int opp = 1 - player;
            bool canTactic = myTactics <= opponentTactics;
            bool troopPlayable = false;

            foreach (int card in hand)
            {
                if (Card.IsTroop(card))
                {
                    foreach (var f in flags)
                    {
                        if (!f.HasSpace(player)) continue;
                        moves.Add(BattleLineMove.Place(card, f.Index));
                        troopPlayable = true;
                    }
                    continue;
                }
                if (!canTactic) continue;

                switch (Card.KindOf(card))
                {
                    case TacticKind.Leader:
                    case TacticKind.Cavalry:
                    case TacticKind.Shield:
                        if (Card.KindOf(card) == TacticKind.Leader && leaderPlayed) break;
                        foreach (var f in flags)
                            if (f.HasSpace(player)) moves.Add(BattleLineMove.Place(card, f.Index));
                        break;
                    case TacticKind.Fog:
                    case TacticKind.Mud:
                        foreach (var f in flags)
                            if (!f.IsClaimed) moves.Add(BattleLineMove.Place(card, f.Index));
                        break;
                    case TacticKind.Scout:
                        if (troopDeck + tacticsDeck > 0) moves.Add(BattleLineMove.UseScout(card));
                        break;
                    case TacticKind.Redeploy:
                        foreach (var f in flags)
                        {
                            if (f.IsClaimed) continue;
                            foreach (int target in CardsOwnedBy(f, player))
                            {
                                moves.Add(BattleLineMove.UseRedeploy(card, f.Index, target, -1));
                                foreach (var g in flags)
                                {
                                    if (g.Index == f.Index || g.IsClaimed) continue;
                                    if (Card.IsEnvironment(target) || g.HasSpace(player))
                                        moves.Add(BattleLineMove.UseRedeploy(card, f.Index, target, g.Index));
                                }
                            }
                        }
                        break;
                    case TacticKind.Deserter:
                        foreach (var f in flags)
                        {
                            if (f.IsClaimed) continue;
                            foreach (int target in CardsOwnedBy(f, opp))
                                moves.Add(BattleLineMove.UseDeserter(card, f.Index, target));
                        }
                        break;
                    case TacticKind.Traitor:
                        foreach (var f in flags)
                        {
                            if (f.IsClaimed) continue;
                            foreach (int target in f.CardsOf(opp))
                            {
                                if (!Card.IsTroop(target)) continue;
                                foreach (var g in flags)
                                    if (g.HasSpace(player)) moves.Add(BattleLineMove.UseTraitor(card, f.Index, target, g.Index));
                            }
                        }
                        break;
                }
            }

            if (!troopPlayable) moves.Add(BattleLineMove.Pass);
            return moves;
        }

        /// <summary>フラッグにある player のカード(隊列 + その人が置いた環境戦術)。</summary>
        public static List<int> CardsOwnedBy(Flag flag, int player)
        {
            var list = new List<int>(flag.CardsOf(player));
            for (int i = 0; i < flag.Environment.Count; i++)
                if (flag.EnvironmentOwner(i) == player) list.Add(flag.Environment[i]);
            return list;
        }

        /// <summary>場(フラッグ上・捨て札)に見えていない部隊カード。確保の証明に使う。</summary>
        public static bool[] UnseenTroops(IReadOnlyList<Flag> flags, IReadOnlyList<int> discard)
        {
            var pool = new bool[Card.TroopCount];
            for (int i = 0; i < pool.Length; i++) pool[i] = true;
            foreach (var f in flags)
            for (int p = 0; p < BattleLineState.Players; p++)
            foreach (int c in f.CardsOf(p))
                if (Card.IsTroop(c)) pool[c] = false;
            foreach (int c in discard)
                if (Card.IsTroop(c)) pool[c] = false;
            return pool;
        }

        /// <summary>player がフラッグ flagIndex を確保できるか(両者完成で勝っている、または証明できる)。</summary>
        public static bool CanClaim(IReadOnlyList<Flag> flags, IReadOnlyList<int> discard, int player, int flagIndex)
        {
            var flag = flags[flagIndex];
            if (flag.IsClaimed || !flag.IsComplete(player)) return false;
            int opp = 1 - player;
            var mine = flag.Rank(player);

            if (flag.IsComplete(opp))
            {
                var theirs = flag.Rank(opp);
                if (mine.Score != theirs.Score) return mine.Score > theirs.Score;
                return flag.CompletedSeq(player) < flag.CompletedSeq(opp);
            }

            // 証明: 相手が見えていない部隊カードをどう足しても上回れない(同点は先に完成したこちらの勝ち)
            var pool = UnseenTroops(flags, discard);
            if (!Formation.TryBest(flag.CardsOf(opp), flag.Required, flag.HasFog, pool, out var best)) return true;
            return best.Score <= mine.Score;
        }

        public static EndReason WinReason(IReadOnlyList<Flag> flags, int player)
        {
            int run = 0, total = 0;
            bool breakthrough = false;
            foreach (var f in flags)
            {
                if (f.Claimer == player)
                {
                    total++;
                    run++;
                    if (run >= BattleLineRules.AdjacentToWin) breakthrough = true;
                }
                else
                {
                    run = 0;
                }
            }
            if (breakthrough) return EndReason.Breakthrough;
            return total >= BattleLineRules.FlagsToWin ? EndReason.FiveFlags : EndReason.None;
        }
    }
}

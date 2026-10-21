using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.BattleLine
{
    /// <summary>
    /// CPU。プレイヤー視点(自分の手札 + 公開情報)だけを見て、各合法手を 1 手だけシミュレートし評価値が最大の手を選ぶ。
    /// 相手の手札と山札の順番は見えないので、シミュレート用に見えていないカードをランダムに割り振った局面を使う
    /// (カードを出す段階の評価は公開情報と自分の手札しか使わないので、割り振りは結果に影響しない)。
    /// </summary>
    public sealed class BattleLineCpu : ICpuPlayer<BattleLinePlayerView, BattleLineMove>
    {
        const double ClaimValue = 40;
        const double OpenValue = 30;
        const double MissingPenalty = 9;
        const double HandTroopValue = 0.15;
        const double HandTacticValue = 2;
        const double GuileCost = 5;
        const double MoraleCost = 3;
        const double EnvironmentCost = 4;

        static readonly int[] TypeValue = { 0, 20, 30, 45, 60 };

        readonly Random _rng;
        readonly BattleLineRules _rules = new BattleLineRules();

        public BattleLineCpu(Random rng)
        {
            _rng = rng ?? new Random();
        }

        public BattleLineMove ChooseMove(BattleLinePlayerView view)
        {
            var moves = view.GetLegalMoves();
            if (moves.Count == 0) throw new InvalidOperationException("No legal moves");
            if (moves.Count == 1) return moves[0];

            switch (view.Phase)
            {
                case TurnPhase.Draw:
                case TurnPhase.ScoutDraw:
                    return ChooseDraw(view, moves);
                case TurnPhase.ScoutReturn:
                    return ChooseReturn(view);
                default:
                    return ChoosePlay(view, moves);
            }
        }

        // ---------- ドロー ----------

        BattleLineMove ChooseDraw(BattleLinePlayerView view, List<BattleLineMove> moves)
        {
            int me = view.Viewer;
            int tacticsInHand = 0;
            foreach (int c in view.Hand)
                if (Card.IsTactic(c)) tacticsInHand++;
            bool canUseTactics = view.TacticsPlayed(me) <= view.TacticsPlayed(1 - me);
            bool wantTactic = tacticsInHand == 0 && canUseTactics && _rng.NextDouble() < 0.3;
            var preferred = wantTactic ? DeckKind.Tactics : DeckKind.Troop;
            foreach (var m in moves)
                if (m.Deck == preferred) return m;
            return moves[0];
        }

        // ---------- 偵察で戻すカード ----------

        BattleLineMove ChooseReturn(BattleLinePlayerView view)
        {
            int me = view.Viewer;
            bool canUseTactics = view.TacticsPlayed(me) <= view.TacticsPlayed(1 - me);
            int worst = view.Hand[0];
            double worstScore = double.MaxValue;
            foreach (int c in view.Hand)
            {
                double score = Card.IsTroop(c) ? Card.ValueOf(c) + FitBonus(view, c) : canUseTactics ? 9 : 2;
                if (score < worstScore)
                {
                    worstScore = score;
                    worst = c;
                }
            }
            return BattleLineMove.ReturnCard(worst);
        }

        /// <summary>自分の隊列と色・数字がつながる部隊カードは手元に残したい。</summary>
        static double FitBonus(BattleLinePlayerView view, int card)
        {
            double best = 0;
            foreach (var f in view.Flags)
            {
                if (!f.HasSpace(view.Viewer)) continue;
                foreach (int c in f.CardsOf(view.Viewer))
                {
                    if (!Card.IsTroop(c)) continue;
                    bool sameColor = Card.ColorOf(c) == Card.ColorOf(card);
                    int gap = Math.Abs(Card.ValueOf(c) - Card.ValueOf(card));
                    if (sameColor && gap <= 2) best = Math.Max(best, 6);
                    else if (gap == 0) best = Math.Max(best, 4);
                    else if (sameColor) best = Math.Max(best, 2);
                }
            }
            return best;
        }

        // ---------- カードを出す ----------

        BattleLineMove ChoosePlay(BattleLinePlayerView view, List<BattleLineMove> moves)
        {
            int me = view.Viewer;
            var state = Determinize(view);
            var baseline = new double[BattleLineState.FlagCount];
            var ctx = new EvalContext(state, me);
            for (int i = 0; i < baseline.Length; i++) baseline[i] = ctx.OpenFlagValue(state.Flags[i]);

            BattleLineMove best = moves[0];
            double bestScore = double.MinValue;
            foreach (var m in moves)
            {
                var next = _rules.ApplyMove(state, m);
                double score = Evaluate(next, me, m, baseline) - Cost(m, view);
                score += _rng.NextDouble() * 0.5; // 同点の手をばらけさせる
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            return best;
        }

        static double Cost(BattleLineMove m, BattleLinePlayerView view)
        {
            if (m.Kind == MoveKind.Pass || !Card.IsTactic(m.Card)) return 0;
            if (m.Kind == MoveKind.Scout)
            {
                // 手札が弱いときだけ偵察で入れ替える価値がある
                int sum = 0, n = 0;
                foreach (int c in view.Hand)
                {
                    if (!Card.IsTroop(c)) continue;
                    sum += Card.ValueOf(c);
                    n++;
                }
                bool weak = n == 0 || sum < n * 5;
                return weak ? GuileCost - 8 : GuileCost + 4;
            }
            if (Card.IsGuile(m.Card)) return GuileCost;
            if (Card.IsEnvironment(m.Card)) return EnvironmentCost;
            return MoraleCost;
        }

        double Evaluate(BattleLineState s, int me, BattleLineMove move, double[] baseline)
        {
            if (s.Result.IsOver)
            {
                if (s.Result.Status == GameStatus.Draw) return 0;
                return s.Result.Winner == me ? 100000 : -100000;
            }

            var ctx = new EvalContext(s, me);
            double score = 0;
            foreach (var f in s.Flags)
            {
                if (f.Claimer == me) score += ClaimValue;
                else if (f.Claimer >= 0) score -= ClaimValue;
                else if (f.Index == move.Flag || f.Index == move.ToFlag) score += ctx.OpenFlagValue(f);
                else score += baseline[f.Index];
            }

            // 突破(隣り合う 3 本)と 5 本確保への近さ
            int opp = 1 - me;
            for (int i = 0; i + 2 < s.Flags.Count; i++)
            {
                int mine = 0, theirs = 0;
                for (int j = i; j < i + 3; j++)
                {
                    if (s.Flags[j].Claimer == me) mine++;
                    else if (s.Flags[j].Claimer == opp) theirs++;
                }
                if (theirs == 0) score += mine * mine * 4;
                if (mine == 0) score -= theirs * theirs * 4;
            }
            int myClaims = s.ClaimedCount(me), oppClaims = s.ClaimedCount(opp);
            score += (myClaims * myClaims - oppClaims * oppClaims) * 2;

            foreach (int c in s.HandOf(me))
                score += Card.IsTroop(c) ? Card.ValueOf(c) * HandTroopValue : HandTacticValue;
            return score;
        }

        /// <summary>評価の途中で使う、局面ごとのカードの見込み。</summary>
        sealed class EvalContext
        {
            readonly int _me;
            readonly bool[] _possibleForMe;
            readonly bool[] _handOnly;
            readonly bool[] _possibleForOpponent;

            public EvalContext(BattleLineState s, int me)
            {
                _me = me;
                // 相手が持っているかもしれない部隊カード = 場・捨て札・自分の手札に無いもの
                _possibleForOpponent = BattleLineLogic.UnseenTroops(s.Flags, s.Discard);
                _handOnly = new bool[Card.TroopCount];
                foreach (int c in s.HandOf(me))
                {
                    if (!Card.IsTroop(c)) continue;
                    _possibleForOpponent[c] = false;
                    _handOnly[c] = true;
                }
                _possibleForMe = (bool[])_possibleForOpponent.Clone();
                for (int i = 0; i < Card.TroopCount; i++)
                    if (_handOnly[i]) _possibleForMe[i] = true;
            }

            /// <summary>未確保のフラッグの見込み(自分が勝ちそうなら +、負けそうなら −)。</summary>
            public double OpenFlagValue(Flag f)
            {
                if (f.IsClaimed) return 0;
                int opp = 1 - _me;
                if (f.IsComplete(_me) && f.IsComplete(opp))
                {
                    var a = f.Rank(_me);
                    var b = f.Rank(opp);
                    bool win = a.Score != b.Score ? a.Score > b.Score : f.CompletedSeq(_me) < f.CompletedSeq(opp);
                    return win ? OpenValue : -OpenValue;
                }
                double mine = Strength(f, _me, true);
                double theirs = Strength(f, opp, false);
                double p = 1.0 / (1.0 + Math.Exp(-(mine - theirs) / 8.0));
                return (p - 0.5) * 2 * OpenValue;
            }

            double Strength(Flag f, int player, bool isMe)
            {
                var cards = f.CardsOf(player);
                int missing = Math.Max(0, f.Required - cards.Count);
                if (missing == 0) return Value(f.Rank(player));

                double best = -40;
                if (Formation.TryBest(cards, f.Required, f.HasFog, isMe ? _possibleForMe : _possibleForOpponent, out var possible))
                    best = Value(possible) - MissingPenalty * missing;
                if (isMe && Formation.TryBest(cards, f.Required, f.HasFog, _handOnly, out var certain))
                    best = Math.Max(best, Value(certain) - 2 * missing);
                return best;
            }

            static double Value(FormationRank r) => r.Fog ? r.Sum : TypeValue[(int)r.Type] + r.Sum;
        }

        /// <summary>見えていないカードをランダムに割り振った完全な局面を作る(シミュレート用)。</summary>
        BattleLineState Determinize(BattleLinePlayerView view)
        {
            int me = view.Viewer, opp = 1 - me;
            var s = new BattleLineState();
            for (int i = 0; i < view.Flags.Count; i++)
            {
                var src = view.Flags[i];
                var dst = s.FlagAt(i);
                for (int p = 0; p < BattleLineState.Players; p++)
                {
                    foreach (int c in src.CardsOf(p)) dst.AddCard(p, c);
                    dst.SetCompletedSeq(p, src.CompletedSeq(p));
                }
                for (int e = 0; e < src.Environment.Count; e++) dst.AddEnvironment(src.Environment[e], src.EnvironmentOwner(e));
                if (src.IsClaimed) dst.SetClaimer(src.Claimer);
            }
            s.Hand(me).AddRange(view.Hand);
            s.DiscardPile.AddRange(view.Discard);
            for (int p = 0; p < BattleLineState.Players; p++)
            {
                s.SetTacticsPlayed(p, view.TacticsPlayed(p));
                s.SetLeaderPlayed(p, view.LeaderPlayed(p));
            }

            var seen = new bool[Card.Total];
            foreach (var f in view.Flags)
            {
                for (int p = 0; p < BattleLineState.Players; p++)
                    foreach (int c in f.CardsOf(p)) seen[c] = true;
                foreach (int c in f.Environment) seen[c] = true;
            }
            foreach (int c in view.Hand) seen[c] = true;
            foreach (int c in view.Discard) seen[c] = true;

            var troops = new List<int>();
            var tactics = new List<int>();
            for (int c = 0; c < Card.Total; c++)
            {
                if (seen[c]) continue;
                (Card.IsTroop(c) ? troops : tactics).Add(c);
            }
            Shuffle(troops);
            Shuffle(tactics);
            Deal(troops, s.Hand(opp), view.OpponentHandTroops);
            Deal(tactics, s.Hand(opp), view.OpponentHandTactics);
            Deal(troops, s.Deck(DeckKind.Troop), view.TroopDeckCount);
            Deal(tactics, s.Deck(DeckKind.Tactics), view.TacticsDeckCount);

            s.CurrentPlayer = view.CurrentPlayer;
            s.Phase = view.Phase;
            s.PendingCount = view.PendingCount;
            s.Turn = view.Turn;
            s.ConsecutivePasses = view.ConsecutivePasses;
            // 新しく完成する隊列が、既存の完成順より後になるように
            int maxSeq = 0;
            foreach (var f in view.Flags)
                for (int p = 0; p < BattleLineState.Players; p++)
                    maxSeq = Math.Max(maxSeq, f.CompletedSeq(p));
            s.Seq = maxSeq;
            return s;
        }

        static void Deal(List<int> from, List<int> to, int count)
        {
            for (int i = 0; i < count && from.Count > 0; i++)
            {
                to.Add(from[from.Count - 1]);
                from.RemoveAt(from.Count - 1);
            }
        }

        void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

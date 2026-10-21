using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Quarto
{
    /// <summary>
    /// 探索型 CPU。「置く + 渡す」を 1 手番として、反復深化の αβ 探索で読む。
    /// - 置いてそろうなら必ず置く / 相手がすぐそろえられる駒は渡さない。
    /// - 探索はノード数の上限で打ち切る(時間ではなく数で区切るので結果は決定的)。終盤は読み切れる。
    /// - 「置く」を決めるときに渡す駒も決めておき、直後の「渡す」で使う。
    /// </summary>
    public sealed class QuartoCpu : ICpuPlayer<QuartoState, QuartoMove>
    {
        const int WinScore = 1000;
        const int Infinity = 1000000;
        /// <summary>1 回の判断で調べる局面数の上限(WebGL でも一瞬で終わる程度)。</summary>
        public const int DefaultNodeBudget = 60000;

        readonly Random _rng;
        readonly int _nodeBudget;
        int _nodes;
        bool _aborted;

        // 置いたときに決めた「渡す駒」
        int _plannedGive = -1;
        int _plannedPly = -1;
        int[] _plannedCells;

        public QuartoCpu(Random rng, int nodeBudget = DefaultNodeBudget)
        {
            _rng = rng ?? new Random();
            _nodeBudget = nodeBudget;
        }

        /// <summary>直近の判断で読み切れた深さ(手番数)。テスト・調整用。</summary>
        public int LastCompletedDepth { get; private set; }

        public QuartoMove ChooseMove(QuartoState state)
        {
            if (state.Result.IsOver) throw new InvalidOperationException("Game is over");
            return state.Phase == TurnPhase.Place ? ChoosePlace(state) : ChooseGive(state);
        }

        // =====================================================================
        // 置く
        // =====================================================================

        QuartoMove ChoosePlace(QuartoState state)
        {
            var cells = state.CopyCells();
            int hand = state.PieceInHand;
            int used = UsedMask(cells) | (1 << hand);
            var empties = Empties(cells);

            // 1. 置いてそろうなら置く
            foreach (int c in empties)
                if (QuartoBoard.PlacementWins(cells, c, hand))
                    return Remember(state, c, -1);

            // 最後の 1 マス: 置くしかない(渡す駒も無い)
            if (empties.Count == 1) return Remember(state, empties[0], -1);

            // 2. (置く, 渡す) の組を反復深化で評価する
            var candidates = new List<(int cell, int give)>();
            foreach (int c in empties)
                for (int g = 0; g < QuartoPiece.Count; g++)
                    if ((used & (1 << g)) == 0) candidates.Add((c, g));
            Shuffle(candidates);

            var best = SearchRoot(candidates, empties.Count, (cand, depth, alpha) =>
            {
                cells[cand.cell] = hand;
                int v = EvaluateGive(cells, cand.give, used | (1 << cand.give), depth, alpha);
                cells[cand.cell] = QuartoBoard.Empty;
                return v;
            });
            return Remember(state, best.cell, best.give);
        }

        QuartoMove Remember(QuartoState state, int cell, int give)
        {
            _plannedGive = give;
            _plannedPly = state.Ply + 1;
            _plannedCells = state.CopyCells();
            _plannedCells[cell] = state.PieceInHand;
            return QuartoMove.Place(QuartoBoard.X(cell), QuartoBoard.Y(cell));
        }

        // =====================================================================
        // 渡す
        // =====================================================================

        QuartoMove ChooseGive(QuartoState state)
        {
            var cells = state.CopyCells();
            if (_plannedGive >= 0 && _plannedPly == state.Ply && SameCells(cells, _plannedCells) && state.IsAvailable(_plannedGive))
            {
                int planned = _plannedGive;
                _plannedGive = -1;
                return QuartoMove.Give(planned);
            }
            _plannedGive = -1;

            int used = UsedMask(cells);
            var candidates = new List<int>();
            for (int g = 0; g < QuartoPiece.Count; g++)
                if ((used & (1 << g)) == 0) candidates.Add(g);
            Shuffle(candidates);

            int empties = Empties(cells).Count;
            int best = SearchRoot(candidates, empties, (g, depth, alpha) => EvaluateGive(cells, g, used | (1 << g), depth, alpha));
            return QuartoMove.Give(best);
        }

        // =====================================================================
        // 探索
        // =====================================================================

        /// <summary>
        /// 候補を深さ 1 から順に評価し、上限内に読み終えた最も深い結果で最善の候補を返す(同点は先に並んだもの = ランダム)。
        /// evaluate(候補, 深さ, alpha) は候補の評価値(alpha 以下なら上界でよい)。
        /// </summary>
        T SearchRoot<T>(List<T> candidates, int maxDepth, Func<T, int, int, int> evaluate)
        {
            _nodes = 0;
            T best = candidates[0];
            LastCompletedDepth = 0;
            var order = new List<T>(candidates);

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                _aborted = false;
                int bestScore = -Infinity;
                T iterBest = order[0];
                var scores = new Dictionary<int, int>();
                for (int i = 0; i < order.Count; i++)
                {
                    // 同点の候補も正確に比べられるよう、alpha を bestScore - 1 にする
                    int v = evaluate(order[i], depth, bestScore - 1);
                    if (_aborted) break;
                    scores[i] = v;
                    if (v > bestScore)
                    {
                        bestScore = v;
                        iterBest = order[i];
                    }
                }
                if (_aborted) break;

                best = iterBest;
                LastCompletedDepth = depth;
                // 決着が読めたら(勝ち・負けが確定)それ以上深く読まなくてよい
                if (Math.Abs(bestScore) >= WinScore) break;

                // 次の深さでは良かった候補から調べる(αβ の枝刈りが効きやすい)
                var sorted = new List<int>(scores.Keys);
                sorted.Sort((a, b) => scores[b].CompareTo(scores[a]));
                var next = new List<T>(order.Count);
                foreach (int i in sorted) next.Add(order[i]);
                order = next;
            }
            return best;
        }

        /// <summary>
        /// 自分が駒 give を渡した直後の局面の、自分から見た評価値。
        /// 相手がすぐそろえられる駒なら負け。
        /// </summary>
        int EvaluateGive(int[] cells, int give, int used, int depth, int alpha)
        {
            if (QuartoBoard.AnyPlacementWins(cells, give)) return -(WinScore + depth - 1);
            return -Solve(cells, give, used, depth - 1, -Infinity, -alpha);
        }

        /// <summary>
        /// 駒 hand を置く側から見た評価値(negamax)。depth は残りの手番数。
        /// 勝ち = WinScore + 残り深さ(早く勝つほど高い)、読み切れない・引き分け = 0。
        /// </summary>
        int Solve(int[] cells, int hand, int used, int depth, int alpha, int beta)
        {
            if (++_nodes > _nodeBudget)
            {
                _aborted = true;
                return 0;
            }

            int emptyCount = 0;
            for (int c = 0; c < QuartoBoard.CellCount; c++)
            {
                if (cells[c] != QuartoBoard.Empty) continue;
                emptyCount++;
                if (QuartoBoard.PlacementWins(cells, c, hand)) return WinScore + depth;
            }
            // 最後の 1 つを置いてもそろわない = 引き分け
            if (emptyCount <= 1 || depth <= 0) return 0;

            int best = -Infinity;
            for (int c = 0; c < QuartoBoard.CellCount; c++)
            {
                if (cells[c] != QuartoBoard.Empty) continue;
                cells[c] = hand;
                bool anySafe = false;
                for (int g = 0; g < QuartoPiece.Count; g++)
                {
                    if ((used & (1 << g)) != 0 || g == hand) continue;
                    if (QuartoBoard.AnyPlacementWins(cells, g)) continue;
                    anySafe = true;
                    int v = -Solve(cells, g, used | (1 << g), depth - 1, -beta, -alpha);
                    if (_aborted)
                    {
                        cells[c] = QuartoBoard.Empty;
                        return 0;
                    }
                    if (v > best) best = v;
                    if (best > alpha) alpha = best;
                    if (alpha >= beta)
                    {
                        cells[c] = QuartoBoard.Empty;
                        return best;
                    }
                }
                // どの駒を渡しても相手にそろえられる
                if (!anySafe)
                {
                    int lose = -(WinScore + depth - 1);
                    if (lose > best) best = lose;
                    if (best > alpha) alpha = best;
                }
                cells[c] = QuartoBoard.Empty;
                if (alpha >= beta) return best;
            }
            return best;
        }

        // =====================================================================
        // 補助
        // =====================================================================

        static int UsedMask(int[] cells)
        {
            int used = 0;
            foreach (int p in cells)
                if (p >= 0) used |= 1 << p;
            return used;
        }

        static List<int> Empties(int[] cells)
        {
            var list = new List<int>(QuartoBoard.CellCount);
            for (int c = 0; c < cells.Length; c++)
                if (cells[c] == QuartoBoard.Empty) list.Add(c);
            return list;
        }

        static bool SameCells(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

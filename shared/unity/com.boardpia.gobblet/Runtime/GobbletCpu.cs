using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Gobblet
{
    /// <summary>
    /// アルファベータ探索の CPU。完全情報ゲームなので局面をそのまま受け取る。
    /// 同じ評価の手は、探索前に手の順番をシャッフルすることでランダムに選ぶ。
    /// </summary>
    public sealed class GobbletCpu : ICpuPlayer<GobbletState, GobbletMove>
    {
        const int WinScore = 100000;

        readonly Random _rng;
        readonly int _depth;

        public GobbletCpu(Random rng, int depth = 3)
        {
            if (depth < 1) throw new ArgumentOutOfRangeException(nameof(depth));
            _rng = rng ?? new Random();
            _depth = depth;
        }

        public GobbletMove ChooseMove(GobbletState state)
        {
            if (state.Result.IsOver) throw new InvalidOperationException("game is over");
            var moves = new List<GobbletMove>();
            GobbletRules.AddMoves(state, state.CurrentPlayer, moves);
            if (moves.Count == 0) throw new InvalidOperationException("no legal moves");
            Shuffle(moves);

            GobbletMove best = moves[0];
            int alpha = -WinScore * 2;
            const int beta = WinScore * 2;
            foreach (var m in moves)
            {
                int score = -Negamax(GobbletRules.ApplyUnchecked(state, m), _depth - 1, -beta, -alpha);
                if (score > alpha)
                {
                    alpha = score;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>手番プレイヤーから見た評価値。早く勝つ(遅く負ける)ほど良い。</summary>
        int Negamax(GobbletState state, int depth, int alpha, int beta)
        {
            if (state.Result.IsOver)
            {
                if (state.Result.Status == GameStatus.Draw) return 0;
                int score = WinScore - state.Ply;
                return state.Result.Winner == state.CurrentPlayer ? score : -score;
            }
            if (depth == 0) return Evaluate(state, state.CurrentPlayer);

            var moves = new List<GobbletMove>();
            GobbletRules.AddMoves(state, state.CurrentPlayer, moves);
            foreach (var m in moves)
            {
                int score = -Negamax(GobbletRules.ApplyUnchecked(state, m), depth - 1, -beta, -alpha);
                if (score > alpha) alpha = score;
                if (alpha >= beta) break;
            }
            return alpha;
        }

        /// <summary>
        /// 静的評価: 相手にふさがれていない列の自分の駒数(2 つなら大きく加点)と、手持ちの大きい駒。
        /// 手番側は次に列を完成できるので、2 つ並んだ列の価値を少し高くする。
        /// </summary>
        internal static int Evaluate(GobbletState state, int player)
        {
            int opponent = 1 - player;
            int score = 0;
            foreach (var line in GobbletState.Lines)
            {
                int mine = 0, theirs = 0;
                foreach (int c in line)
                {
                    var top = state.Top(c);
                    if (!top.HasValue) continue;
                    if (top.Value.Owner == player) mine++;
                    else theirs++;
                }
                if (theirs == 0) score += mine == 2 ? 40 : mine == 1 ? 4 : 0;
                if (mine == 0) score -= theirs == 2 ? 30 : theirs == 1 ? 4 : 0;
            }

            // 盤上に見えている駒は大きいほど取られにくい。手持ちの大は切り札として残す価値がある
            for (int c = 0; c < GobbletState.CellCount; c++)
            {
                var top = state.Top(c);
                if (!top.HasValue) continue;
                int v = (int)top.Value.Size + 1;
                score += top.Value.Owner == player ? v : -v;
            }
            score += 3 * (state.HandCount(player, PieceSize.Large) - state.HandCount(opponent, PieceSize.Large));
            return score;
        }

        void Shuffle(List<GobbletMove> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

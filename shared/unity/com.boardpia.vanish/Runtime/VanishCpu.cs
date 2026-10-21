using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Vanish
{
    /// <summary>
    /// アルファベータ探索の CPU。完全情報ゲームなので局面をそのまま受け取る。
    /// 同じ評価の手は、探索前に手の順番をシャッフルすることでランダムに選ぶ。
    /// </summary>
    public sealed class VanishCpu : ICpuPlayer<VanishState, VanishMove>
    {
        const int WinScore = 100000;

        readonly Random _rng;
        readonly int _depth;

        public VanishCpu(Random rng, int depth = 6)
        {
            if (depth < 1) throw new ArgumentOutOfRangeException(nameof(depth));
            _rng = rng ?? new Random();
            _depth = depth;
        }

        public VanishMove ChooseMove(VanishState state)
        {
            if (state.Result.IsOver) throw new InvalidOperationException("game is over");
            var moves = new List<VanishMove>();
            VanishRules.AddMoves(state, moves);
            if (moves.Count == 0) throw new InvalidOperationException("no legal moves");
            Shuffle(moves);

            VanishMove best = moves[0];
            int alpha = -WinScore * 2;
            const int beta = WinScore * 2;
            foreach (var m in moves)
            {
                int score = -Negamax(VanishRules.ApplyUnchecked(state, m), _depth - 1, -beta, -alpha);
                if (score > alpha)
                {
                    alpha = score;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>手番プレイヤーから見た評価値。早く勝つ(遅く負ける)ほど良い。</summary>
        int Negamax(VanishState state, int depth, int alpha, int beta)
        {
            if (state.Result.IsOver)
            {
                if (state.Result.Status == GameStatus.Draw) return 0;
                int score = WinScore - state.Ply;
                return state.Result.Winner == state.CurrentPlayer ? score : -score;
            }
            if (depth == 0) return Evaluate(state, state.CurrentPlayer);

            var moves = new List<VanishMove>();
            VanishRules.AddMoves(state, moves);
            foreach (var m in moves)
            {
                int score = -Negamax(VanishRules.ApplyUnchecked(state, m), depth - 1, -beta, -alpha);
                if (score > alpha) alpha = score;
                if (alpha >= beta) break;
            }
            return alpha;
        }

        /// <summary>
        /// 静的評価: 次の自分の手で消える駒を除いた「残る駒」で、相手にふさがれていない列の駒数を数える。
        /// 中央のマスは 4 列に関わるので少し加点する。
        /// </summary>
        internal static int Evaluate(VanishState state, int player)
        {
            int opponent = 1 - player;
            int myVanish = state.NextToVanish(player);
            int theirVanish = state.NextToVanish(opponent);
            int score = 0;
            foreach (var line in VanishState.Lines)
            {
                int mine = 0, theirs = 0;
                foreach (int c in line)
                {
                    int owner = state.At(c);
                    if (owner == player && c != myVanish) mine++;
                    else if (owner == opponent && c != theirVanish) theirs++;
                }
                if (theirs == 0) score += mine == 2 ? 40 : mine == 1 ? 4 : 0;
                if (mine == 0) score -= theirs == 2 ? 30 : theirs == 1 ? 4 : 0;
            }
            int center = state.At(4);
            if (center == player && myVanish != 4) score += 6;
            else if (center == opponent && theirVanish != 4) score -= 6;
            return score;
        }

        void Shuffle(List<VanishMove> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostReversa
{
    /// <summary>
    /// CPU。見えていない情報(相手が捨てた色・相手の裏向きの駒の色・相手の手駒の内訳)を、視点と矛盾しないように
    /// 何通りか推測し(決定化)、それぞれで「自分の手 → 相手の応手」の 2 手先まで読んで、平均の評価が最も高い手を選ぶ。
    /// 渡されるのはプレイヤー視点(ReversaPlayerView)だけなので、相手の非公開情報は使わない。
    /// </summary>
    public sealed class ReversaCpu : ICpuPlayer<ReversaPlayerView, ReversaMove>
    {
        const int Samples = 8;
        const double Win = 1000.0;

        readonly Random _rng;
        readonly ReversaRules _rules = new ReversaRules();

        public ReversaCpu(Random rng)
        {
            _rng = rng ?? new Random();
        }

        public ReversaMove ChooseMove(ReversaPlayerView view)
        {
            var moves = view.GetLegalMoves();
            if (moves.Count == 0) throw new InvalidOperationException("No legal moves for the CPU");
            if (moves.Count == 1) return moves[0];
            if (view.Phase == GamePhase.Discard) return moves[_rng.Next(moves.Count)];

            var totals = new double[moves.Count];
            for (int s = 0; s < Samples; s++)
            {
                var world = Determinize(view);
                for (int i = 0; i < moves.Count; i++) totals[i] += EvaluateMove(world, moves[i], view.Viewer);
            }

            int best = 0;
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < moves.Count; i++)
            {
                double score = totals[i] / Samples + _rng.NextDouble() * 0.5; // 同点はランダムに
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return moves[best];
        }

        /// <summary>自分の手を指してから相手の応手まで読んだ評価(相手は自分にとって悪い手を選びやすいとみる)。</summary>
        double EvaluateMove(ReversaState world, ReversaMove move, int me)
        {
            var s1 = _rules.ApplyMove(world, move);
            if (s1.Result.IsOver) return Terminal(s1.Result, me);

            var replies = _rules.GetLegalMoves(s1);
            double worst = double.PositiveInfinity, sum = 0;
            foreach (var r in replies)
            {
                var s2 = _rules.ApplyMove(s1, r);
                double v = s2.Result.IsOver ? Terminal(s2.Result, me) : Heuristic(s2, me);
                if (v < worst) worst = v;
                sum += v;
            }
            return worst * 0.7 + sum / replies.Count * 0.3;
        }

        static double Terminal(GameResult result, int me) =>
            result.Status == GameStatus.Draw ? 0 : result.Winner == me ? Win : -Win;

        /// <summary>勝てる色(捨てなかった色)の表の数の差。裏で控えている自分の色の駒も少しだけ評価する。</summary>
        static double Heuristic(ReversaState s, int me)
        {
            int opp = 1 - me;
            var myColor = WinningColor(s.DiscardOf(me));
            var oppColor = WinningColor(s.DiscardOf(opp));
            int myUp = s.FaceUpCount(me, myColor);
            int oppUp = s.FaceUpCount(opp, oppColor);
            int myDown = 0;
            foreach (var t in s.Cells)
                if (t.HasValue && !t.Value.FaceUp && t.Value.Owner == me && t.Value.Kind == myColor) myDown++;
            return 12.0 * myUp * myUp - 14.0 * oppUp * oppUp + 2.0 * myDown;
        }

        static TileKind WinningColor(TileKind discarded) => discarded == TileKind.Blue ? TileKind.Red : TileKind.Blue;

        /// <summary>視点と矛盾しない完全情報の局面を 1 つ作る。</summary>
        public ReversaState Determinize(ReversaPlayerView view)
        {
            int me = view.Viewer, opp = 1 - me;
            int upBlue = view.FaceUpCount(opp, TileKind.Blue);
            int upRed = view.FaceUpCount(opp, TileKind.Red);

            // 相手が捨てた色の候補: その色の表の駒が 2 体以下なら捨てた可能性がある
            var candidates = new List<TileKind>(2);
            if (upBlue <= ReversaBoard.PiecesPerColor - 1) candidates.Add(TileKind.Blue);
            if (upRed <= ReversaBoard.PiecesPerColor - 1) candidates.Add(TileKind.Red);
            var oppDiscard = candidates.Count == 0 ? TileKind.Blue : candidates[_rng.Next(candidates.Count)];

            int poolBlue = Math.Max(0, ReversaBoard.PiecesPerColor - (oppDiscard == TileKind.Blue ? 1 : 0) - upBlue);
            int poolRed = Math.Max(0, ReversaBoard.PiecesPerColor - (oppDiscard == TileKind.Red ? 1 : 0) - upRed);
            var pool = new List<TileKind>(poolBlue + poolRed);
            for (int i = 0; i < poolBlue; i++) pool.Add(TileKind.Blue);
            for (int i = 0; i < poolRed; i++) pool.Add(TileKind.Red);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            int next = 0;
            var cells = new Tile?[ReversaBoard.CellCount];
            for (int i = 0; i < cells.Length; i++)
            {
                var t = view.Cells[i];
                if (t.HasValue && t.Value.Kind == TileKind.Unknown)
                    t = t.Value.WithKind(next < pool.Count ? pool[next++] : TileKind.Blue);
                cells[i] = t;
            }

            var blue = new int[2];
            var red = new int[2];
            blue[me] = view.HandOf(me, TileKind.Blue);
            red[me] = view.HandOf(me, TileKind.Red);
            int oppHand = view.HandCount(opp);
            for (int k = 0; k < oppHand; k++)
            {
                var kind = next < pool.Count ? pool[next++] : TileKind.Blue;
                if (kind == TileKind.Blue) blue[opp]++;
                else red[opp]++;
            }

            var discards = new TileKind[2];
            discards[me] = view.DiscardOf(me);
            discards[opp] = oppDiscard;
            return ReversaSetup.CreateCustom(cells, blue, red, discards, view.FirstPlayer, view.CurrentPlayer, view.Ply);
        }
    }
}

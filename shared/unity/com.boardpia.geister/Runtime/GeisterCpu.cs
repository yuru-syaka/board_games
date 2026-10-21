using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Geister
{
    /// <summary>
    /// ヒューリスティック CPU。受け取るのは PlayerView だけなので、相手の駒の種類は推定で扱う。
    /// 相手駒の「善玉らしさ」は、こちらの陣地へ向かって前進した回数から見積もる。
    /// </summary>
    public sealed class GeisterCpu : ICpuPlayer<GeisterPlayerView, GeisterMove>
    {
        const double WinScore = 100000;
        const double GoodValue = 120;
        const double EvilCaptureCost = 70;
        const double OpponentCaptureChance = 0.6;

        readonly Random _rng;
        readonly Dictionary<int, int> _advanceCount = new Dictionary<int, int>();
        int _lastSeenPly = -1;

        public GeisterCpu(Random rng)
        {
            _rng = rng ?? new Random();
        }

        public GeisterMove ChooseMove(GeisterPlayerView view)
        {
            var moves = view.GetLegalMoves();
            if (moves.Count == 0) throw new InvalidOperationException("No legal moves for CPU");

            ObserveOpponent(view);

            foreach (var m in moves)
                if (m.IsEscape) return m;

            var goodProb = EstimateGoodProbabilities(view);
            double bestScore = double.NegativeInfinity;
            var best = moves[0];
            foreach (var m in moves)
            {
                double score = Evaluate(view, m, goodProb) + _rng.NextDouble() * 4.0;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>直前の相手の手を見て、前進した駒を記録する。</summary>
        void ObserveOpponent(GeisterPlayerView view)
        {
            if (view.Ply == _lastSeenPly || view.LastMove == null) return;
            _lastSeenPly = view.Ply;

            var last = view.LastMove.Value;
            int idx = GeisterBoard.FindById(view.Ghosts, last.GhostId);
            if (idx < 0 || view.Ghosts[idx].Owner == view.Viewer || last.IsEscape) return;

            int opponent = 1 - view.Viewer;
            int before = GeisterBoard.DistanceToEscape(opponent, last.FromX, last.FromY);
            int after = GeisterBoard.DistanceToEscape(opponent, last.ToX, last.ToY);
            if (after < before)
            {
                _advanceCount.TryGetValue(last.GhostId, out int n);
                _advanceCount[last.GhostId] = n + 1;
            }
        }

        /// <summary>盤上の相手駒それぞれが善玉である確率の推定値(Id → 確率)。</summary>
        internal Dictionary<int, double> EstimateGoodProbabilities(GeisterPlayerView view)
        {
            int opponent = 1 - view.Viewer;
            int goodLeft = GeisterBoard.GoodPerPlayer - view.CapturedCount(opponent, GhostType.Good);
            var weights = new Dictionary<int, double>();
            double total = 0;
            foreach (var g in view.Ghosts)
            {
                if (g.Owner != opponent || !g.IsOnBoard) continue;
                _advanceCount.TryGetValue(g.Id, out int adv);
                double w = 1.0 + 0.3 * Math.Min(adv, 5);
                // 脱出口の近くにいる駒は善玉の可能性を高めに見る
                if (GeisterBoard.DistanceToEscape(opponent, g.X, g.Y) <= 1) w += 0.5;
                weights[g.Id] = w;
                total += w;
            }

            var probs = new Dictionary<int, double>();
            foreach (var kv in weights)
                probs[kv.Key] = total > 0 ? Math.Min(0.97, goodLeft * kv.Value / total) : 0;
            return probs;
        }

        double Evaluate(GeisterPlayerView view, GeisterMove move, Dictionary<int, double> goodProb)
        {
            int me = view.Viewer;
            int opp = 1 - me;
            double score = 0;

            // 手を適用した後の盤面(相手の種類は Unknown のまま)
            var ghosts = new Ghost[view.Ghosts.Count];
            for (int i = 0; i < ghosts.Length; i++) ghosts[i] = view.Ghosts[i];

            int capturedEnemyGood = view.CapturedCount(opp, GhostType.Good);
            int capturedEnemyEvil = view.CapturedCount(opp, GhostType.Evil);
            int myGoodLeft = GeisterBoard.GoodPerPlayer - view.CapturedCount(me, GhostType.Good);
            int myEvilLeft = GeisterBoard.EvilPerPlayer - view.CapturedCount(me, GhostType.Evil);

            int target = GeisterBoard.FindAt(ghosts, move.ToX, move.ToY);
            if (target >= 0)
            {
                double p = goodProb.TryGetValue(ghosts[target].Id, out var pg) ? pg : 0.5;
                double gain = capturedEnemyGood == GeisterBoard.GoodPerPlayer - 1 ? WinScore : GoodValue;
                double loss = capturedEnemyEvil == GeisterBoard.EvilPerPlayer - 1 ? WinScore : EvilCaptureCost;
                score += p * gain - (1 - p) * loss;
                ghosts[target] = ghosts[target].WithStatus(GhostStatus.Captured);
            }
            int moverIdx = GeisterBoard.FindById(ghosts, move.GhostId);
            ghosts[moverIdx] = ghosts[moverIdx].MovedTo(move.ToX, move.ToY);

            // 相手の脱出の脅威
            foreach (var e in ghosts)
            {
                if (e.Owner != opp || !e.IsOnBoard) continue;
                double p = goodProb.TryGetValue(e.Id, out var pg) ? pg : 0.5;
                int d = GeisterBoard.DistanceToEscape(opp, e.X, e.Y);
                if (d == 0)
                {
                    score -= p * WinScore * 0.9;
                }
                else if (d == 1)
                {
                    bool guarded = CornerGuarded(ghosts, me, e.X, e.Y);
                    score -= p * (guarded ? 40 : 600);
                }
                else
                {
                    score -= p * (6 - d) * 6;
                }
            }

            // 相手に取られる危険(相手はこちらの種類を知らないので、種類に関係なく一定確率で取ってくると仮定)
            double threatTotal = 0;
            int threatened = 0;
            foreach (var mine in ghosts)
            {
                if (mine.Owner != me || !mine.IsOnBoard) continue;
                if (!AdjacentToOwner(ghosts, opp, mine.X, mine.Y)) continue;
                threatened++;
                if (mine.Type == GhostType.Good)
                    threatTotal -= myGoodLeft == 1 ? WinScore * 0.5 : GoodValue;
                else
                    threatTotal += myEvilLeft == 1 ? WinScore * 0.3 : 45;
            }
            if (threatened > 0) score += OpponentCaptureChance * threatTotal / threatened;

            // 自分の善玉の前進と脱出準備
            foreach (var mine in ghosts)
            {
                if (mine.Owner != me || !mine.IsOnBoard) continue;
                int d = GeisterBoard.DistanceToEscape(me, mine.X, mine.Y);
                if (mine.Type == GhostType.Good)
                {
                    score += (10 - d) * 5;
                    if (d == 0)
                        score += AdjacentToOwner(ghosts, opp, mine.X, mine.Y) ? 300 : 5000;
                }
                else
                {
                    // 悪玉は自陣の角を守りつつ前線で囮になる
                    int guardDist = DistanceToOwnCorner(me, mine.X, mine.Y);
                    if (guardDist == 1) score += 12;
                    score += (10 - d) * 1.5;
                }
            }

            return score;
        }

        static bool AdjacentToOwner(IReadOnlyList<Ghost> ghosts, int owner, int x, int y)
        {
            foreach (var g in ghosts)
                if (g.Owner == owner && g.IsOnBoard && GeisterBoard.IsAdjacent(g.X, g.Y, x, y)) return true;
            return false;
        }

        /// <summary>(x, y) の隣にある「me の自陣の角」を me の駒が見張っているか。</summary>
        static bool CornerGuarded(IReadOnlyList<Ghost> ghosts, int me, int x, int y)
        {
            int row = me == 0 ? 0 : GeisterBoard.Size - 1;
            foreach (int cx in new[] { 0, GeisterBoard.Size - 1 })
            {
                if (!GeisterBoard.IsAdjacent(cx, row, x, y)) continue;
                if (!AdjacentToOwner(ghosts, me, cx, row) && GeisterBoard.FindAt(ghosts, cx, row) < 0) return false;
            }
            return true;
        }

        static int DistanceToOwnCorner(int me, int x, int y) => GeisterBoard.DistanceToEscape(1 - me, x, y);
    }
}

using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostSecret
{
    /// <summary>
    /// CPU。見えている情報(自分の手札・全員の出したカード・表向きのカード・自分が知った手札)から、
    /// ほかの人 1 人ずつが持っていそうな数字の確率を数え、手ごとに点数をつけていちばん良い手を選ぶ。
    /// </summary>
    public sealed class SecretCpu : ICpuPlayer<SecretPlayerView, SecretMove>
    {
        readonly Random _rng;

        public SecretCpu(Random rng)
        {
            _rng = rng ?? new Random();
        }

        public SecretMove ChooseMove(SecretPlayerView v)
        {
            var moves = v.GetLegalMoves();
            if (moves.Count == 0) throw new InvalidOperationException("no legal moves");
            if (moves.Count == 1) return moves[0];

            switch (v.Phase)
            {
                case SecretPhase.Fortune:
                    // 次に引く人へ弱いカードが行くように、数字の小さいカードを上にする
                    return SecretMove.FortuneOrder(SecretCards.ValueOf(v.Peek[1]) < SecretCards.ValueOf(v.Peek[0]));
                case SecretPhase.Lamp:
                    {
                        int mine = v.MyHand[0], theirs = v.KnownHandOf(v.PendingTarget);
                        return SecretMove.LampSwap(theirs >= 0 && SecretCards.ValueOf(theirs) > SecretCards.ValueOf(mine));
                    }
            }

            var odds = new Dictionary<int, double[]>();
            SecretMove best = moves[0];
            double bestScore = double.NegativeInfinity;
            foreach (var m in moves)
            {
                double score = Score(v, m, odds) + _rng.NextDouble() * 0.05;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            return best;
        }

        double Score(SecretPlayerView v, SecretMove m, Dictionary<int, double[]> odds)
        {
            int me = v.Viewer;
            int keep = -1;
            foreach (int c in v.MyHand)
                if (c != m.Card)
                {
                    keep = c;
                    break;
                }
            int kv = keep >= 0 ? SecretCards.ValueOf(keep) : 0;
            // 山札が少ないほど、残すカードの強さが大事
            double keepWeight = v.DeckCount <= 3 ? 1.5 : 0.4;
            double score = kv * keepWeight;
            if (kv == Ghost.Curse) score -= 2; // ノロイを残すと最後に弱い

            switch (SecretCards.ValueOf(m.Card))
            {
                case Ghost.Secret:
                    return -1000;

                case Ghost.Whisper:
                    if (m.Target < 0) return score;
                    return score + 10 * Odds(v, m.Target, odds)[m.Guess] - 0.5;

                case Ghost.Compare:
                    {
                        if (m.Target < 0) return score;
                        var o = Odds(v, m.Target, odds);
                        double win = 0, lose = 0;
                        for (int x = 0; x < Ghost.Kinds; x++)
                        {
                            if (x < kv) win += o[x];
                            else if (x > kv) lose += o[x];
                        }
                        return score + 9 * (win - lose);
                    }

                case Ghost.Blow:
                    {
                        if (m.Target == me)
                        {
                            if (kv == Ghost.Secret || kv == Ghost.Curse) return -1000;
                            return score - kv * keepWeight + ExpectedUnseen(v) * keepWeight - 0.5;
                        }
                        var o = Odds(v, m.Target, odds);
                        return score + 12 * o[Ghost.Secret] - 20 * o[Ghost.Curse] + 0.3 * Expected(o);
                    }

                case Ghost.Lamp:
                    {
                        if (m.Target < 0) return score;
                        var o = Odds(v, m.Target, odds);
                        double gain = 0;
                        for (int x = 0; x < Ghost.Kinds; x++) gain += o[x] * Math.Max(0, x - kv);
                        return score + 1 + gain * keepWeight;
                    }

                case Ghost.Hide:
                    return score + 2.5 + kv * 0.2;

                case Ghost.Rumor:
                    {
                        int from = PreviousParticipant(v);
                        if (from < 0) return score;
                        double incoming = Expected(Odds(v, from, odds));
                        return score + (incoming - kv) * keepWeight;
                    }

                case Ghost.Fortune:
                    return score + 1.2;
            }
            return score;
        }

        /// <summary>ウワサで自分に手札を渡してくる人(自分の前の、カクレでない残っている人)。</summary>
        static int PreviousParticipant(SecretPlayerView v)
        {
            int n = v.PlayerCount;
            for (int i = 1; i < n; i++)
            {
                int q = (v.Viewer - i + n) % n;
                if (!v.IsOut(q) && !v.IsProtected(q)) return q;
            }
            return -1;
        }

        static double Expected(double[] o)
        {
            double e = 0;
            for (int x = 0; x < Ghost.Kinds; x++) e += o[x] * x;
            return e;
        }

        static double ExpectedUnseen(SecretPlayerView v)
        {
            var counts = v.UnseenCounts();
            double total = 0, sum = 0;
            for (int x = 0; x < Ghost.Kinds; x++)
            {
                total += counts[x];
                sum += counts[x] * x;
            }
            return total > 0 ? sum / total : 0;
        }

        /// <summary>p が今それぞれの数字を持っている確率(知っていれば確定。知らなければ、まだ見えていないカードの枚数の割合)。</summary>
        static double[] Odds(SecretPlayerView v, int p, Dictionary<int, double[]> cache)
        {
            if (cache.TryGetValue(p, out var cached)) return cached;
            var o = new double[Ghost.Kinds];
            int known = v.KnownHandOf(p);
            if (known >= 0)
            {
                o[SecretCards.ValueOf(known)] = 1;
            }
            else
            {
                var counts = v.UnseenCounts();
                // ほかの人の手札で、知っているものは除く
                for (int q = 0; q < v.PlayerCount; q++)
                {
                    int k = v.KnownHandOf(q);
                    if (q != p && k >= 0) counts[SecretCards.ValueOf(k)]--;
                }
                double total = 0;
                for (int x = 0; x < Ghost.Kinds; x++) total += Math.Max(0, counts[x]);
                for (int x = 0; x < Ghost.Kinds; x++) o[x] = total > 0 ? Math.Max(0, counts[x]) / total : 0;
            }
            cache[p] = o;
            return o;
        }
    }
}

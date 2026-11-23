using System;
using BoardGames.Core;

namespace BoardGames.GhostDerby
{
    /// <summary>
    /// CPU。見えている情報(DerbyPlayerView)から、残りの走る札で何度もレースを試して 1 着・2 着の確率を見積もり、
    /// 賭けの期待値で決める。CPU ごとに、大穴の好き嫌い・賭ける強さ・追い賭けのしやすさが違う。
    /// </summary>
    public sealed class DerbyCpu : ICpuPlayer<DerbyPlayerView, DerbyMove>
    {
        const int Trials = 48;

        readonly Random _rng;
        /// <summary>高い配当をどれだけ好むか。</summary>
        readonly double _longshot;
        /// <summary>持ちコインのうち賭けに回す割合。</summary>
        readonly double _boldness;
        /// <summary>追い賭けをする期待値の下限。</summary>
        readonly double _chaseBar;

        public DerbyCpu(Random rng)
        {
            _rng = rng ?? new Random();
            _longshot = -0.04 + _rng.NextDouble() * 0.12;
            _boldness = 0.2 + _rng.NextDouble() * 0.3;
            _chaseBar = 0.05 + _rng.NextDouble() * 0.45;
        }

        public DerbyMove ChooseMove(DerbyPlayerView v)
        {
            if (v.GetLegalMoves().Count == 0) throw new InvalidOperationException("no legal moves");
            switch (v.Phase)
            {
                case DerbyPhase.Bet: return ChooseBet(v);
                case DerbyPhase.Trick: return ChooseTrick(v);
                default: return ChooseChase(v);
            }
        }

        DerbyMove ChooseBet(DerbyPlayerView v)
        {
            Estimate(v, out var p1, out var p2);
            int best = 0;
            double bestScore = double.MinValue, bestEv = 0;
            for (int r = 0; r < DerbyLogic.Runners; r++)
            {
                double ev = p1[r] * v.OddsOf(r) + p2[r] - 1;
                double score = ev + _longshot * v.OddsOf(r) + (_rng.NextDouble() - 0.5) * 0.35;
                if (score <= bestScore) continue;
                bestScore = score;
                best = r;
                bestEv = ev;
            }
            int coins = v.Coins(v.Viewer);
            double frac = _boldness * (1 + Math.Max(-0.5, Math.Min(1, bestEv)));
            if (v.Race == DerbyLogic.Races && IsBehind(v)) frac += 0.25;
            int amount = Math.Max(1, Math.Min(Math.Min(DerbyLogic.MaxBet, coins), (int)Math.Round(coins * frac)));
            return DerbyMove.Bet(best, amount);
        }

        DerbyMove ChooseTrick(DerbyPlayerView v)
        {
            Estimate(v, out var p1, out var _);
            int mine = v.BetRunner(v.Viewer);
            int rival = -1;
            for (int r = 0; r < DerbyLogic.Runners; r++)
                if (r != mine && (rival < 0 || p1[r] > p1[rival])) rival = r;

            DerbyMove best = default;
            double bestScore = double.MinValue;
            foreach (int c in v.MyHand)
            {
                DerbyMove move;
                double score;
                switch (DerbyCards.TrickOf(c))
                {
                    case TrickKind.Dash: move = DerbyMove.Trick(c, mine); score = 3; break;
                    case TrickKind.Stumble: move = DerbyMove.Trick(c, rival); score = 2 + p1[rival]; break;
                    case TrickKind.Freeze: move = DerbyMove.Trick(c, rival); score = 2.2 + p1[rival]; break;
                    case TrickKind.Swap: move = DerbyMove.Trick(c, mine); score = 1.5 + (1 - p1[mine]); break;
                    default: move = DerbyMove.Trick(c, -1); score = v.OddsOf(mine) >= 5 ? 2.4 : 1; break;
                }
                score += _rng.NextDouble() * 0.6;
                if (score <= bestScore) continue;
                bestScore = score;
                best = move;
            }
            return best;
        }

        DerbyMove ChooseChase(DerbyPlayerView v)
        {
            Estimate(v, out var p1, out var p2);
            int best = -1;
            double bestEv = double.MinValue;
            for (int r = 0; r < DerbyLogic.Runners; r++)
            {
                if (v.IsFinished(r)) continue;
                double ev = p1[r] * v.ChaseOddsOf(r) + p2[r] - 1 + (_rng.NextDouble() - 0.5) * 0.2;
                if (ev <= bestEv) continue;
                bestEv = ev;
                best = r;
            }
            int coins = v.Coins(v.Viewer);
            if (best < 0 || coins <= 0 || bestEv < _chaseBar) return DerbyMove.Pass;
            int amount = Math.Max(1, Math.Min(Math.Min(DerbyLogic.MaxChase, coins), (int)Math.Round(1 + bestEv * 4 * _boldness)));
            return DerbyMove.Chase(best, amount);
        }

        static bool IsBehind(DerbyPlayerView v)
        {
            int mine = v.Coins(v.Viewer);
            for (int p = 0; p < v.PlayerCount; p++)
                if (p != v.Viewer && v.Coins(p) > mine) return true;
            return false;
        }

        /// <summary>見えている情報だけでレースを Trials 回試し、1 着・2 着の確率を出す(伏せられたいたずら札は数えない)。</summary>
        void Estimate(DerbyPlayerView v, out double[] first, out double[] second)
        {
            first = new double[DerbyLogic.Runners];
            second = new double[DerbyLogic.Runners];
            var seen = new bool[DerbyLogic.RunCardCount];
            foreach (int c in v.Seen) seen[c] = true;

            var pos = new int[DerbyLogic.Runners];
            var frozen = new int[DerbyLogic.Runners];
            var done = new int[DerbyLogic.Finishers];
            var deck = new int[DerbyLogic.RunCardCount];

            for (int t = 0; t < Trials; t++)
            {
                int finished = v.Finish.Count;
                for (int i = 0; i < finished; i++) done[i] = v.Finish[i];
                for (int r = 0; r < DerbyLogic.Runners; r++)
                {
                    pos[r] = v.PositionOf(r);
                    frozen[r] = v.FrozenOf(r);
                }
                int n = 0;
                for (int c = 0; c < DerbyLogic.RunCardCount; c++)
                    if (!seen[c]) deck[n++] = c;

                while (finished < DerbyLogic.Finishers)
                {
                    if (n == 0)
                        for (int c = 0; c < DerbyLogic.RunCardCount; c++) deck[n++] = c;
                    int k = _rng.Next(n);
                    int card = deck[k];
                    deck[k] = deck[--n];
                    int r = DerbyCards.RunnerOf(card);
                    if (pos[r] >= DerbyLogic.Goal) continue;
                    if (frozen[r] > 0)
                    {
                        frozen[r]--;
                        continue;
                    }
                    pos[r] += DerbyCards.Steps(v.OddsOf(r), DerbyCards.RunIndex(card));
                    if (pos[r] >= DerbyLogic.Goal) done[finished++] = r;
                }
                first[done[0]] += 1.0 / Trials;
                if (finished >= 2) second[done[1]] += 1.0 / Trials;
            }
        }
    }
}

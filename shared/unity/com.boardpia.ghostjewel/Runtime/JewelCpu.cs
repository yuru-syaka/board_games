using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostJewel
{
    /// <summary>
    /// CPU。見えている情報(JewelPlayerView)だけで手を選ぶ。
    ///   1. 買えるカードがあれば、点・館の主への近さ・割引の役立ち具合で選んで買う
    ///   2. 無ければ「早く買えて価値の高い」カードを目標にし、足りない色の宝石を取る
    ///   3. 宝石が取れない・持ちすぎのときは、目標のカードを予約する
    /// 宝石を返すときは、目標に要らない色から返す。
    /// </summary>
    public sealed class JewelCpu : ICpuPlayer<JewelPlayerView, JewelMove>
    {
        readonly Random _rng;

        public JewelCpu(Random rng)
        {
            _rng = rng ?? new Random();
        }

        public JewelMove ChooseMove(JewelPlayerView v)
        {
            var legal = v.GetLegalMoves();
            if (legal.Count == 0) return JewelMove.Pass();
            if (legal.Count == 1) return legal[0];

            var me = v.Me;
            var targets = RankTargets(v);
            var need = NeedOf(me, targets);

            if (v.Phase == JewelPhase.Return) return ChooseReturn(legal, me, need);

            // 1. 買う
            JewelMove? bestBuy = null;
            double bestBuyScore = double.MinValue;
            foreach (var m in legal)
            {
                if (m.Kind != MoveKind.Buy) continue;
                double score = BuyScore(v, m.A) + _rng.NextDouble() * 0.1;
                if (score > bestBuyScore)
                {
                    bestBuyScore = score;
                    bestBuy = m;
                }
            }
            // 点のつかない安いカードは、目標のカードに役立つときだけ買う(序盤は割引を集める)
            if (bestBuy.HasValue && (bestBuyScore >= 2.0 || targets.Count == 0)) return bestBuy.Value;

            // 2. 目標のための宝石を取る
            var take = ChooseTake(v, legal, need);
            if (take.HasValue && me.GemTotal <= JewelLogic.MaxGems - 2) return take.Value;

            // 3. 予約(目標が場にあれば、それを確保してゴールドも得る)
            if (targets.Count > 0)
            {
                foreach (var t in targets)
                {
                    if (!v.FindInMarket(t, out _, out _)) continue;
                    var reserve = JewelMove.Reserve(t);
                    if (Contains(legal, reserve) && JewelRules.Shortfall(me, t) > 1) return reserve;
                    break;
                }
            }
            if (bestBuy.HasValue) return bestBuy.Value;
            if (take.HasValue) return take.Value;
            foreach (var m in legal)
                if (m.Kind == MoveKind.Reserve || m.Kind == MoveKind.ReserveDeck) return m;
            return legal[_rng.Next(legal.Count)];
        }

        static bool Contains(IReadOnlyList<JewelMove> list, JewelMove move)
        {
            foreach (var m in list)
                if (m.Equals(move)) return true;
            return false;
        }

        /// <summary>買う価値。点 + 館の主に近づく + ほかの目標の割引になる。</summary>
        double BuyScore(JewelPlayerView v, int card)
        {
            var info = JewelCards.Get(card);
            double score = info.Points * 3.0;
            score += NobleHelp(v, info.Bonus) * 1.5;
            score += ColorDemand(v, info.Bonus) * 0.35;
            if (v.Me.Reserved.Contains(card)) score += 1.0; // 予約を片付ける
            if (v.FinalRound) score += info.Points * 5.0;
            // ゴールドを使いすぎる買い物は少し減点
            score -= JewelRules.Payment(v.Me, card)[Gem.Gold] * 0.4;
            return score;
        }

        /// <summary>その色の割引が、まだ来ていない館の主の条件をいくつ埋めるか。</summary>
        static int NobleHelp(JewelPlayerView v, int color)
        {
            int help = 0;
            foreach (int n in v.Nobles)
            {
                var need = JewelCards.GetNoble(n).Need;
                if (need[color] > v.Me.Bonus[color]) help++;
            }
            return help;
        }

        /// <summary>場と自分の予約のカードが、その色をどれだけ必要としているか(割引の役立ち具合)。</summary>
        static int ColorDemand(JewelPlayerView v, int color)
        {
            int demand = 0;
            foreach (int card in Visible(v))
                demand += Math.Max(0, JewelCards.Get(card).Cost[color] - v.Me.Bonus[color]) > 0 ? 1 : 0;
            return demand;
        }

        static List<int> Visible(JewelPlayerView v)
        {
            var list = new List<int>();
            for (int t = 0; t < JewelLogic.Tiers; t++)
                for (int s = 0; s < JewelLogic.MarketSize; s++)
                    if (v.MarketAt(t, s) >= 0) list.Add(v.MarketAt(t, s));
            foreach (int c in v.Me.Reserved)
                if (c >= 0) list.Add(c);
            return list;
        }

        /// <summary>目標にするカードを、よい順に並べる(価値 ÷ 買えるまでの手番の見込み)。</summary>
        List<int> RankTargets(JewelPlayerView v)
        {
            var me = v.Me;
            var scored = new List<(int card, double score)>();
            foreach (int card in Visible(v))
            {
                var info = JewelCards.Get(card);
                int missing = Math.Max(0, JewelRules.Shortfall(me, card) - me.Gems[Gem.Gold]);
                if (missing == 0) continue; // もう買える
                double value = info.Points * 2.0 + 1.0 + NobleHelp(v, info.Bonus) * 0.8;
                double turns = 1.0 + missing / 2.5;
                double score = value / turns + _rng.NextDouble() * 0.05;
                if (me.Reserved.Contains(card)) score += 0.3;
                scored.Add((card, score));
            }
            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var result = new List<int>();
            foreach (var s in scored) result.Add(s.card);
            return result;
        }

        /// <summary>色ごとの「欲しさ」。いちばんの目標を強く、次の目標を弱く数える。</summary>
        static double[] NeedOf(JewelPlayer me, List<int> targets)
        {
            var need = new double[Gem.Colors];
            for (int i = 0; i < targets.Count && i < 3; i++)
            {
                double weight = i == 0 ? 3.0 : i == 1 ? 1.2 : 0.5;
                var cost = JewelCards.Get(targets[i]).Cost;
                for (int c = 0; c < Gem.Colors; c++)
                    need[c] += Math.Max(0, cost[c] - me.Bonus[c] - me.Gems[c]) * weight;
            }
            return need;
        }

        JewelMove? ChooseTake(JewelPlayerView v, IReadOnlyList<JewelMove> legal, double[] need)
        {
            JewelMove? best = null;
            double bestScore = 0.0;
            var me = v.Me;
            foreach (var m in legal)
            {
                double score;
                if (m.Kind == MoveKind.TakeGems)
                {
                    score = 0;
                    for (int c = 0; c < Gem.Colors; c++)
                        if ((m.A & (1 << c)) != 0) score += need[c] > 0 ? need[c] : 0.05;
                }
                else if (m.Kind == MoveKind.TakeTwo)
                {
                    // 2 個とも目標に要るときだけ選ぶ
                    if (TargetMissing(v, m.A) < 2) continue;
                    score = need[m.A] * 1.1;
                }
                else continue;
                score += _rng.NextDouble() * 0.01;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>いちばんの目標で、その色があといくつ足りないか。</summary>
        int TargetMissing(JewelPlayerView v, int color)
        {
            var targets = RankTargets(v);
            if (targets.Count == 0) return 0;
            var cost = JewelCards.Get(targets[0]).Cost;
            return cost[color] - v.Me.Bonus[color] - v.Me.Gems[color];
        }

        static JewelMove ChooseReturn(IReadOnlyList<JewelMove> legal, JewelPlayer me, double[] need)
        {
            JewelMove best = legal[0];
            double bestScore = double.MaxValue;
            foreach (var m in legal)
            {
                if (m.Kind != MoveKind.Return) continue;
                // ゴールドは最後まで残す。要らない色・多く持っている色から返す
                double score = m.A == Gem.Gold ? 1000.0 : need[m.A] * 2.0 - me.Gems[m.A];
                if (score < bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }
            return best;
        }
    }
}

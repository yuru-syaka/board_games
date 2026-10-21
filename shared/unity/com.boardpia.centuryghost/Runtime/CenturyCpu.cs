using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.CenturyGhost
{
    /// <summary>
    /// 1 手先読みのヒューリスティック CPU。各合法手を適用した後の自分の状態を評価し、最も良い手を選ぶ。
    /// 評価 = 得点(おばけ・コイン) + 魂の価値 + 並んでいるおばけへの近さ + 手札・持ちカードの価値。
    /// </summary>
    public sealed class CenturyCpu : ICpuPlayer<CenturyPlayerView, CenturyMove>
    {
        static readonly double[] RankWorth = { 0.5, 1.0, 1.5, 2.0 };

        readonly Random _rng;

        public CenturyCpu(Random rng = null)
        {
            _rng = rng ?? new Random();
        }

        public CenturyMove ChooseMove(CenturyPlayerView view)
        {
            var me = view.Players[view.CurrentPlayer];
            var opp = view.Players[1 - view.CurrentPlayer];
            bool endgame = view.FinalRound || opp.Ghosts.Count >= CenturyLogic.GhostsToEnd - 1 ||
                           me.Ghosts.Count >= CenturyLogic.GhostsToEnd - 1 || view.Ply >= CenturyLogic.MaxPlies - 10;

            CenturyMove best = null;
            double bestScore = double.NegativeInfinity;
            foreach (var move in view.GetLegalMoves())
            {
                var (p, t) = CenturyLogic.Resolve(me, view.Table, move);
                double score = Evaluate(p, t, endgame, view.FinalRound) + _rng.NextDouble() * 0.05;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }

        internal static double Evaluate(PlayerData p, Table t, bool endgame, bool lastTurn)
        {
            double v = 3.0 * (p.GhostPoints + p.CoinPoints);

            // 最終手番では残った魂は点数としての価値しかない
            if (lastTurn) return v + p.Spirits.Score;

            v += Worth(p.Spirits);
            v += Progress(p.Spirits, t.GhostRow);

            double future = endgame ? 0.2 : 1.0;
            foreach (int id in p.Hand) v += 0.35 * future * Usefulness(CardCatalog.Action(id), p.Spirits);
            int owned = p.CardCount;
            foreach (int id in p.Hand.Concat(p.Played))
                v += future * (owned <= 8 ? 1.0 : 0.3) * BaseValue(CardCatalog.Action(id));
            return v;
        }

        static double Worth(Spirits s)
        {
            double w = 0;
            for (int r = 0; r < Spirits.Ranks; r++) w += s[r] * RankWorth[r];
            return w;
        }

        /// <summary>並んでいるおばけにどれだけ近いか(足りない魂をランクで重み付けした距離から)。</summary>
        static double Progress(Spirits s, IReadOnlyList<int> row)
        {
            double best = 0, second = 0;
            foreach (int id in row)
            {
                var g = CardCatalog.Ghost(id);
                int deficit = 0;
                for (int r = 0; r < Spirits.Ranks; r++) deficit += Math.Max(0, g.Cost[r] - s[r]) * (r + 1);
                double p = g.Points / (1.0 + 0.5 * deficit);
                if (p > best)
                {
                    second = best;
                    best = p;
                }
                else if (p > second)
                {
                    second = p;
                }
            }
            return best + 0.3 * second;
        }

        /// <summary>今の魂でそのカードを使ったときの得(魂の価値の増分)。</summary>
        static double Usefulness(ActionCard card, Spirits s)
        {
            switch (card.Kind)
            {
                case CardKind.Produce:
                    return Worth(card.Output);
                case CardKind.Trade:
                    int n = Math.Min(s.CountOf(card.Input), 3);
                    return n * Math.Max(0, Worth(card.Output) - Worth(card.Input) + 0.3);
                default:
                    int upgradable = s.Total - s[Spirits.TopRank];
                    return Math.Min(card.Upgrades, upgradable) * 0.5;
            }
        }

        static double BaseValue(ActionCard card)
        {
            switch (card.Kind)
            {
                case CardKind.Produce: return 0.6 * Worth(card.Output);
                case CardKind.Trade: return 0.6 * (Worth(card.Output) - Worth(card.Input) + 0.8);
                default: return 0.5 * card.Upgrades;
            }
        }
    }
}

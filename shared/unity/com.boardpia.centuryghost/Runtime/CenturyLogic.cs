using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardGames.CenturyGhost
{
    /// <summary>
    /// 定数と、完全な状態(CenturyState)と視点(CenturyPlayerView)の両方から使う共通処理。
    /// 合法手と手の効果は公開情報(自分の PlayerData と Table)だけで決まるので、CPU の先読みにも使える。
    /// </summary>
    public static class CenturyLogic
    {
        public const int MaxSpirits = 10;
        public const int MarketSize = 6;
        public const int GhostRowSize = 5;
        public const int GhostsToEnd = 4;
        public const int MaxPlies = 200;
        public const int GoldValue = 3;
        public const int SilverValue = 1;
        public const int InitialGold = 4;
        public const int InitialSilver = 4;

        // ---------- 合法手 ----------

        public static List<CenturyMove> GenerateMoves(PlayerData p, Table t)
        {
            var moves = new List<CenturyMove>();
            foreach (int id in p.Hand)
            {
                var card = CardCatalog.Action(id);
                switch (card.Kind)
                {
                    case CardKind.Produce:
                        moves.Add(CenturyMove.Play(id));
                        break;
                    case CardKind.Trade:
                        int max = p.Spirits.CountOf(card.Input);
                        for (int k = 1; k <= max; k++) moves.Add(CenturyMove.Trade(id, k));
                        break;
                    case CardKind.Upgrade:
                        foreach (var seq in UpgradeSequences(p.Spirits, card.Upgrades))
                            moves.Add(CenturyMove.Upgrade(id, seq));
                        break;
                }
            }
            for (int i = 0; i < t.Market.Count; i++)
                if (p.Spirits.Total >= i) moves.Add(CenturyMove.Acquire(i));
            for (int i = 0; i < t.GhostRow.Count; i++)
                if (p.Spirits.Contains(CardCatalog.Ghost(t.GhostRow[i]).Cost)) moves.Add(CenturyMove.Claim(i));
            if (p.Played.Count > 0) moves.Add(CenturyMove.Rest());
            return moves;
        }

        /// <summary>最大 n 回の強化でできる、結果の異なる強化の並び(1 回以上)。</summary>
        public static List<int[]> UpgradeSequences(Spirits s, int n)
        {
            var result = new List<int[]>();
            var seen = new HashSet<Spirits> { s };
            var frontier = new List<(Spirits, int[])> { (s, new int[0]) };
            for (int step = 0; step < n; step++)
            {
                var next = new List<(Spirits, int[])>();
                foreach (var (cur, seq) in frontier)
                {
                    for (int r = 0; r < Spirits.TopRank; r++)
                    {
                        if (cur[r] == 0) continue;
                        var up = cur.With(r, -1).With(r + 1, 1);
                        if (!seen.Add(up)) continue;
                        var nseq = seq.Concat(new[] { r }).ToArray();
                        result.Add(nseq);
                        next.Add((up, nseq));
                    }
                }
                frontier = next;
            }
            return result;
        }

        public static bool IsLegal(PlayerData p, Table t, CenturyMove m)
        {
            if (m == null) return false;
            switch (m.Kind)
            {
                case MoveKind.Play:
                    if (!p.Hand.Contains(m.Target)) return false;
                    var card = CardCatalog.Action(m.Target);
                    switch (card.Kind)
                    {
                        case CardKind.Produce:
                            return m.Times == 0 && m.Upgrades.Count == 0;
                        case CardKind.Trade:
                            return m.Upgrades.Count == 0 && m.Times >= 1 && m.Times <= p.Spirits.CountOf(card.Input);
                        default:
                            return m.Times == 0 && m.Upgrades.Count >= 1 && m.Upgrades.Count <= card.Upgrades &&
                                   TryUpgrade(p.Spirits, m.Upgrades, out _);
                    }
                case MoveKind.Acquire:
                    return m.Times == 0 && m.Upgrades.Count == 0 && m.Target >= 0 && m.Target < t.Market.Count &&
                           p.Spirits.Total >= m.Target;
                case MoveKind.Claim:
                    return m.Times == 0 && m.Upgrades.Count == 0 && m.Target >= 0 && m.Target < t.GhostRow.Count &&
                           p.Spirits.Contains(CardCatalog.Ghost(t.GhostRow[m.Target]).Cost);
                case MoveKind.Rest:
                    return m.Target == 0 && m.Times == 0 && m.Upgrades.Count == 0 && p.Played.Count > 0;
                default:
                    return false;
            }
        }

        public static bool TryUpgrade(Spirits s, IReadOnlyList<int> ranks, out Spirits result)
        {
            result = s;
            foreach (int r in ranks)
            {
                if (r < 0 || r >= Spirits.TopRank || result[r] == 0) return false;
                result = result.With(r, -1).With(r + 1, 1);
            }
            return true;
        }

        /// <summary>魂が上限を超えていたら、ランクの低いものから捨てる。</summary>
        public static Spirits Overflow(Spirits s, out Spirits discarded)
        {
            int over = s.Total - MaxSpirits;
            discarded = over > 0 ? s.Lowest(over) : Spirits.Empty;
            return s.Minus(discarded);
        }

        // ---------- 手の効果 ----------

        /// <summary>
        /// 手を適用した結果のプレイヤーと場。山札からの補充はしない(取ったカードの分だけ列が短くなる)。
        /// 合法性は呼び出し側で確認しておくこと。
        /// </summary>
        public static (PlayerData player, Table table) Resolve(PlayerData p, Table t, CenturyMove m)
        {
            var spirits = p.Spirits;
            var hand = p.Hand.ToList();
            var played = p.Played.ToList();
            var ghosts = p.Ghosts.ToList();
            int gold = p.Gold, silver = p.Silver;
            var market = t.Market.ToList();
            var marketSpirits = t.MarketSpirits.ToList();
            var ghostRow = t.GhostRow.ToList();
            int goldLeft = t.GoldLeft, silverLeft = t.SilverLeft;

            switch (m.Kind)
            {
                case MoveKind.Play:
                {
                    var card = CardCatalog.Action(m.Target);
                    hand.Remove(m.Target);
                    played.Add(m.Target);
                    if (card.Kind == CardKind.Produce)
                        spirits = spirits.Plus(card.Output);
                    else if (card.Kind == CardKind.Trade)
                        spirits = spirits.Minus(card.Input.Times(m.Times)).Plus(card.Output.Times(m.Times));
                    else if (!TryUpgrade(spirits, m.Upgrades, out spirits))
                        throw new ArgumentException("Invalid upgrade " + m);
                    break;
                }
                case MoveKind.Acquire:
                {
                    int i = m.Target;
                    var pay = spirits.Lowest(i);
                    spirits = spirits.Minus(pay);
                    for (int j = 0; j < i; j++) marketSpirits[j] = marketSpirits[j].Plus(Spirits.One(pay.RankAt(j)));
                    spirits = spirits.Plus(marketSpirits[i]);
                    hand.Add(market[i]);
                    market.RemoveAt(i);
                    marketSpirits.RemoveAt(i);
                    break;
                }
                case MoveKind.Claim:
                {
                    int i = m.Target;
                    var bonus = t.BonusAt(i);
                    if (bonus.gold) { gold++; goldLeft--; }
                    if (bonus.silver) { silver++; silverLeft--; }
                    spirits = spirits.Minus(CardCatalog.Ghost(ghostRow[i]).Cost);
                    ghosts.Add(ghostRow[i]);
                    ghostRow.RemoveAt(i);
                    break;
                }
                case MoveKind.Rest:
                    hand.AddRange(played);
                    played.Clear();
                    break;
            }

            spirits = Overflow(spirits, out _);
            return (new PlayerData(spirits, hand, played, ghosts, gold, silver),
                new Table(market, marketSpirits, ghostRow, goldLeft, silverLeft));
        }
    }
}

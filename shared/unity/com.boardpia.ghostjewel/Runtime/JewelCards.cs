using System.Collections.Generic;

namespace BoardGames.GhostJewel
{
    /// <summary>宝石の種類。0..4 が色の宝石、5 がゴールド(どの色の代わりにもなる)。</summary>
    public static class Gem
    {
        public const int Aqua = 0;
        public const int Rose = 1;
        public const int Topaz = 2;
        public const int Amethyst = 3;
        public const int Emerald = 4;
        public const int Gold = 5;

        /// <summary>色の宝石の種類の数(ゴールドを除く)。</summary>
        public const int Colors = 5;

        /// <summary>ゴールドを含めた種類の数。</summary>
        public const int All = 6;

        static readonly string[] Names = { "アクア", "ローズ", "トパーズ", "アメジスト", "エメラルド", "ゴールド" };

        public static string Name(int gem) => Names[gem];
    }

    /// <summary>ゲーム全体の決まりの数。</summary>
    public static class JewelLogic
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 4;
        /// <summary>この点数に届いた人が出たら、その周の終わりで終局。</summary>
        public const int WinPoints = 15;
        /// <summary>手元に持てる宝石の数(ゴールドを含む)。</summary>
        public const int MaxGems = 10;
        /// <summary>予約しておけるカードの数。</summary>
        public const int MaxReserved = 3;
        public const int Tiers = 3;
        /// <summary>各段に表向きで並ぶカードの数。</summary>
        public const int MarketSize = 4;
        public const int GoldCount = 5;
        /// <summary>同じ色を 2 個取るには、その山に 4 個以上残っている必要がある。</summary>
        public const int TakeTwoMinimum = 4;
        /// <summary>だれも手を打てずに進まなくなったときの打ち切り(全員の手番の合計)。</summary>
        public const int MaxPlies = 600;

        public static bool IsValidPlayerCount(int n) => n >= MinPlayers && n <= MaxPlayers;

        /// <summary>色の宝石 1 色あたりの数(人数で変わる)。</summary>
        public static int GemsPerColor(int players) => players == 2 ? 4 : players == 3 ? 5 : 7;

        /// <summary>館の主の数(人数 + 1)。</summary>
        public static int NobleCount(int players) => players + 1;
    }

    /// <summary>宝石カード 1 枚。買うと、その色の宝石 1 個ぶんの割引(ボーナス)がずっと続く。</summary>
    public sealed class JewelCard
    {
        public int Id { get; }
        /// <summary>段(0..2 = Ⅰ..Ⅲ)。</summary>
        public int Tier { get; }
        /// <summary>このカードがくれる割引の色(0..4)。</summary>
        public int Bonus { get; }
        public int Points { get; }
        /// <summary>色ごとの値段(長さ 5)。</summary>
        public IReadOnlyList<int> Cost { get; }

        public JewelCard(int id, int tier, int bonus, int points, int[] cost)
        {
            Id = id;
            Tier = tier;
            Bonus = bonus;
            Points = points;
            Cost = cost;
        }

        public int TotalCost
        {
            get
            {
                int n = 0;
                foreach (var c in Cost) n += c;
                return n;
            }
        }
    }

    /// <summary>館の主。割引(買ったカードの色)が条件を満たすと、手番の終わりに訪れて 3 点をくれる。</summary>
    public sealed class JewelNoble
    {
        public int Id { get; }
        public int Points => 3;
        /// <summary>色ごとに必要なカードの枚数(長さ 5)。</summary>
        public IReadOnlyList<int> Need { get; }

        public JewelNoble(int id, int[] need)
        {
            Id = id;
            Need = need;
        }
    }

    /// <summary>
    /// カードと館の主の一覧。どの色も同じ形の値段になるよう、色をずらしながら作る
    /// (値段の「形」は、自分の色から何色ずれた宝石をいくつ払うか)。
    /// </summary>
    public static class JewelCards
    {
        /// <summary>値段の形。Offsets[i] 色ずれた宝石を Amounts[i] 個。</summary>
        readonly struct Pattern
        {
            public readonly int Points;
            public readonly int[] Offsets;
            public readonly int[] Amounts;

            public Pattern(int points, int[] offsets, int[] amounts)
            {
                Points = points;
                Offsets = offsets;
                Amounts = amounts;
            }
        }

        static Pattern P(int points, params int[] offsetAmountPairs)
        {
            var offsets = new int[offsetAmountPairs.Length / 2];
            var amounts = new int[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = offsetAmountPairs[i * 2];
                amounts[i] = offsetAmountPairs[i * 2 + 1];
            }
            return new Pattern(points, offsets, amounts);
        }

        // Ⅰ: 1 色 8 枚(全 40 枚)。安くて点はほぼ無い。割引を集める段
        static readonly Pattern[] Tier1 =
        {
            P(0, 1, 1, 2, 1, 3, 1),
            P(0, 1, 2, 3, 1),
            P(0, 2, 3),
            P(0, 1, 1, 2, 1, 3, 1, 4, 1),
            P(0, 1, 2, 2, 2),
            P(0, 0, 1, 2, 2, 4, 1),
            P(0, 3, 2, 4, 1, 1, 1),
            P(1, 3, 4),
        };

        // Ⅱ: 1 色 6 枚(全 30 枚)。1〜3 点
        static readonly Pattern[] Tier2 =
        {
            P(1, 1, 2, 2, 2, 3, 1),
            P(1, 0, 2, 2, 3),
            P(2, 3, 5),
            P(2, 1, 4, 4, 2),
            P(2, 2, 1, 3, 4, 4, 2),
            P(3, 0, 6),
        };

        // Ⅲ: 1 色 4 枚(全 20 枚)。高くて点が大きい
        static readonly Pattern[] Tier3 =
        {
            P(3, 1, 3, 2, 3, 3, 3, 4, 4),
            P(4, 2, 7),
            P(4, 1, 3, 2, 6, 3, 3),
            P(5, 2, 7, 0, 3),
        };

        static readonly List<JewelCard> AllCards = Build();
        static readonly List<JewelNoble> AllNobles = BuildNobles();

        public static IReadOnlyList<JewelCard> All => AllCards;
        public static IReadOnlyList<JewelNoble> Nobles => AllNobles;

        public static JewelCard Get(int id) => AllCards[id];
        public static JewelNoble GetNoble(int id) => AllNobles[id];

        static List<JewelCard> Build()
        {
            var list = new List<JewelCard>();
            var tiers = new[] { Tier1, Tier2, Tier3 };
            for (int t = 0; t < tiers.Length; t++)
            {
                for (int color = 0; color < Gem.Colors; color++)
                {
                    foreach (var p in tiers[t])
                    {
                        var cost = new int[Gem.Colors];
                        for (int i = 0; i < p.Offsets.Length; i++)
                            cost[(color + p.Offsets[i]) % Gem.Colors] += p.Amounts[i];
                        list.Add(new JewelCard(list.Count, t, color, p.Points, cost));
                    }
                }
            }
            return list;
        }

        static List<JewelNoble> BuildNobles()
        {
            var list = new List<JewelNoble>();
            // となり合う 2 色を 4 枚ずつ
            for (int c = 0; c < Gem.Colors; c++)
            {
                var need = new int[Gem.Colors];
                need[c] = 4;
                need[(c + 1) % Gem.Colors] = 4;
                list.Add(new JewelNoble(list.Count, need));
            }
            // 1 色おきの 3 色を 3 枚ずつ
            for (int c = 0; c < Gem.Colors; c++)
            {
                var need = new int[Gem.Colors];
                need[c] = 3;
                need[(c + 2) % Gem.Colors] = 3;
                need[(c + 4) % Gem.Colors] = 3;
                list.Add(new JewelNoble(list.Count, need));
            }
            return list;
        }

        /// <summary>段ごとのカード ID(並びは ID 順)。</summary>
        public static List<int> TierIds(int tier)
        {
            var ids = new List<int>();
            foreach (var c in AllCards)
                if (c.Tier == tier) ids.Add(c.Id);
            return ids;
        }
    }
}

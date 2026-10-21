using System;
using System.Collections.Generic;

namespace BoardGames.CenturyGhost
{
    public enum CardKind
    {
        /// <summary>召集: Output を受け取る。</summary>
        Produce,
        /// <summary>強化: 魂を 1 ランク上げる、を最大 Upgrades 回。</summary>
        Upgrade,
        /// <summary>交換: Input を払って Output を受け取る(何回でも)。</summary>
        Trade,
    }

    /// <summary>術カード(不変)。Id は CardCatalog 内で一意。</summary>
    public sealed class ActionCard
    {
        public int Id { get; }
        public CardKind Kind { get; }
        public Spirits Input { get; }
        public Spirits Output { get; }
        public int Upgrades { get; }

        ActionCard(int id, CardKind kind, Spirits input, Spirits output, int upgrades)
        {
            Id = id;
            Kind = kind;
            Input = input;
            Output = output;
            Upgrades = upgrades;
        }

        public static ActionCard Produce(int id, string output) =>
            new ActionCard(id, CardKind.Produce, Spirits.Empty, Spirits.Parse(output), 0);

        public static ActionCard Upgrade(int id, int n) => new ActionCard(id, CardKind.Upgrade, Spirits.Empty, Spirits.Empty, n);

        public static ActionCard Trade(int id, string input, string output) =>
            new ActionCard(id, CardKind.Trade, Spirits.Parse(input), Spirits.Parse(output), 0);

        public override string ToString()
        {
            switch (Kind)
            {
                case CardKind.Produce: return $"#{Id} +{Output}";
                case CardKind.Upgrade: return $"#{Id} Up{Upgrades}";
                default: return $"#{Id} {Input}>{Output}";
            }
        }
    }

    /// <summary>おばけカード(不変)。</summary>
    public sealed class GhostCard
    {
        public int Id { get; }
        public string Name { get; }
        public Spirits Cost { get; }
        public int Points { get; }

        public GhostCard(int id, string name, string cost, int points)
        {
            Id = id;
            Name = name;
            Cost = Spirits.Parse(cost);
            Points = points;
        }

        public override string ToString() => $"G{Id} {Cost}={Points}";
    }

    /// <summary>全カードの定義。カードは Id で参照する。</summary>
    public static class CardCatalog
    {
        /// <summary>初期手札(各プレイヤーが同じ内容の別カードを持つ)。</summary>
        public static readonly int[][] StartingHands = { new[] { 0, 1 }, new[] { 2, 3 } };

        static readonly ActionCard[] Actions =
        {
            // 初期手札
            ActionCard.Produce(0, "YY"),
            ActionCard.Upgrade(1, 2),
            ActionCard.Produce(2, "YY"),
            ActionCard.Upgrade(3, 2),

            // 市場(10..30)
            ActionCard.Produce(10, "YYY"),
            ActionCard.Produce(11, "YYC"),
            ActionCard.Produce(12, "CC"),
            ActionCard.Produce(13, "YM"),
            ActionCard.Produce(14, "M"),
            ActionCard.Produce(15, "YYYY"),
            ActionCard.Produce(16, "V"),
            ActionCard.Upgrade(17, 3),
            ActionCard.Trade(18, "YY", "C"),
            ActionCard.Trade(19, "YYY", "M"),
            ActionCard.Trade(20, "YYYY", "V"),
            ActionCard.Trade(21, "YYY", "CC"),
            ActionCard.Trade(22, "C", "YYY"),
            ActionCard.Trade(23, "CC", "M"),
            ActionCard.Trade(24, "CC", "YV"),
            ActionCard.Trade(25, "CCC", "MV"),
            ActionCard.Trade(26, "M", "CC"),
            ActionCard.Trade(27, "MM", "CCV"),
            ActionCard.Trade(28, "V", "MC"),
            ActionCard.Trade(29, "YYC", "V"),
            ActionCard.Trade(30, "YYYYY", "VV"),
        };

        static readonly GhostCard[] Ghosts =
        {
            new GhostCard(0, "ちびおばけ", "YYCC", 6),
            new GhostCard(1, "ふわふわ", "YYYCC", 7),
            new GhostCard(2, "ひとつめ", "YYMM", 8),
            new GhostCard(3, "ねぼすけ", "YYCCC", 8),
            new GhostCard(4, "からかさ", "YYYMM", 9),
            new GhostCard(5, "ちょうちん", "CCMM", 10),
            new GhostCard(6, "しろぬの", "YYVV", 10),
            new GhostCard(7, "なないろ", "YCMV", 11),
            new GhostCard(8, "ろくろ", "YYYVV", 11),
            new GhostCard(9, "おおなめ", "CCCMM", 12),
            new GhostCard(10, "ぬりかべ", "CCVV", 12),
            new GhostCard(11, "ももいろ", "MMMM", 12),
            new GhostCard(12, "かげぼうし", "YYMMV", 12),
            new GhostCard(13, "うみぼうず", "CCMMM", 13),
            new GhostCard(14, "きつねび", "MMVV", 14),
            new GhostCard(15, "がしゃどくろ", "CCCVV", 14),
            new GhostCard(16, "ゆきおんな", "CMMVV", 16),
            new GhostCard(17, "やみのおう", "VVVV", 16),
            new GhostCard(18, "つきよの", "MMMVV", 17),
            new GhostCard(19, "おおおばけ", "VVVVV", 20),
        };

        static readonly Dictionary<int, ActionCard> ActionById = new Dictionary<int, ActionCard>();

        static CardCatalog()
        {
            foreach (var a in Actions) ActionById.Add(a.Id, a);
        }

        public static ActionCard Action(int id) =>
            ActionById.TryGetValue(id, out var a) ? a : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown card");

        public static bool IsAction(int id) => ActionById.ContainsKey(id);

        public static GhostCard Ghost(int id) =>
            id >= 0 && id < Ghosts.Length ? Ghosts[id] : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown ghost");

        public static bool IsGhost(int id) => id >= 0 && id < Ghosts.Length;

        public static IEnumerable<int> MarketCardIds()
        {
            foreach (var a in Actions)
                if (a.Id >= 10) yield return a.Id;
        }

        public static int GhostCount => Ghosts.Length;
    }
}

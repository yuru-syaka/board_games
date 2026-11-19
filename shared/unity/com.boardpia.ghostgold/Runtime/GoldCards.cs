namespace BoardGames.GhostGold
{
    /// <summary>カードの種類。</summary>
    public enum CardKind
    {
        /// <summary>金貨の部屋(金貨 1〜14 枚)。</summary>
        Treasure,
        /// <summary>怪異(4 種類)。同じ種類が通路に 2 枚そろうと、探検中の人はその夜の金貨を失う。</summary>
        Hazard,
        /// <summary>秘宝(夜ごとに 1 枚、山札に入る)。</summary>
        Relic,
    }

    /// <summary>決まった数。</summary>
    public static class GoldLogic
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 5;
        public const int Nights = 5;

        public const int TreasureCount = 14;
        public const int HazardKinds = 4;
        public const int HazardCopies = 3;
        public const int RelicCount = Nights;
        public const int CardCount = TreasureCount + HazardKinds * HazardCopies + RelicCount; // 31

        public const int FirstHazard = TreasureCount;                           // 14
        public const int FirstRelic = FirstHazard + HazardKinds * HazardCopies; // 26

        public static bool IsValidPlayerCount(int n) => n >= MinPlayers && n <= MaxPlayers;
    }

    /// <summary>怪異の種類(Hazard の番号 0..3)。</summary>
    public static class Hazard
    {
        public const int Poltergeist = 0;
        public const int WillOWisp = 1;
        public const int CursedMirror = 2;
        public const int Well = 3;
    }

    /// <summary>カード ID(0..30)の表。</summary>
    public static class GoldCards
    {
        public static bool IsCard(int id) => id >= 0 && id < GoldLogic.CardCount;

        public static CardKind KindOf(int id) =>
            id < GoldLogic.FirstHazard ? CardKind.Treasure : id < GoldLogic.FirstRelic ? CardKind.Hazard : CardKind.Relic;

        /// <summary>金貨の部屋の金貨の枚数(1〜14)。ほかの種類は 0。</summary>
        public static int TreasureOf(int id) => KindOf(id) == CardKind.Treasure ? id + 1 : 0;

        /// <summary>怪異の種類(0..3)。ほかの種類は -1。</summary>
        public static int HazardOf(int id) => KindOf(id) == CardKind.Hazard ? (id - GoldLogic.FirstHazard) / GoldLogic.HazardCopies : -1;

        /// <summary>怪異の種類 kind の、copy 枚目(0..2)のカード ID。</summary>
        public static int HazardCard(int kind, int copy) => GoldLogic.FirstHazard + kind * GoldLogic.HazardCopies + copy;

        /// <summary>秘宝が入る夜(1..5)。ほかの種類は 0。</summary>
        public static int RelicNight(int id) => KindOf(id) == CardKind.Relic ? id - GoldLogic.FirstRelic + 1 : 0;

        /// <summary>n 夜目(1..5)の秘宝のカード ID。</summary>
        public static int RelicCard(int night) => GoldLogic.FirstRelic + night - 1;

        /// <summary>秘宝の値打ち(金貨の枚数。6・8・10・12・14)。ほかの種類は 0。</summary>
        public static int RelicValue(int id) => KindOf(id) == CardKind.Relic ? 4 + 2 * RelicNight(id) : 0;

        public static string HazardName(int kind)
        {
            switch (kind)
            {
                case Hazard.Poltergeist: return "ポルターガイスト";
                case Hazard.WillOWisp: return "鬼火";
                case Hazard.CursedMirror: return "呪いの鏡";
                case Hazard.Well: return "底なし井戸";
                default: return "?";
            }
        }

        public static string HazardEffect(int kind)
        {
            switch (kind)
            {
                case Hazard.Poltergeist: return "家具が飛びまわる";
                case Hazard.WillOWisp: return "青い火がまとわりつく";
                case Hazard.CursedMirror: return "鏡の中から手がのびる";
                case Hazard.Well: return "床がぬけて井戸が口をあける";
                default: return "";
            }
        }

        /// <summary>画面に出す名前。</summary>
        public static string NameOf(int id)
        {
            if (!IsCard(id)) return "?";
            switch (KindOf(id))
            {
                case CardKind.Treasure: return $"金貨 {TreasureOf(id)}";
                case CardKind.Hazard: return HazardName(HazardOf(id));
                default: return $"秘宝（{RelicValue(id)}）";
            }
        }
    }
}

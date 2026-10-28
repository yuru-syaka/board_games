namespace BoardGames.GhostJewel
{
    public enum EventKind
    {
        /// <summary>対局が始まった(Player = 最初の手番)。</summary>
        GameStart,
        /// <summary>Player が宝石を取った(Gems[色] = 個数)。</summary>
        TookGems,
        /// <summary>Player がカードを予約した(Card = カード。山札からで本人以外には -1。Tier・Slot = 場所。Slot = -1 は山札。Amount = もらったゴールド)。</summary>
        Reserved,
        /// <summary>Player がカードを買った(Card。Slot = 場所、-1 は予約から。Gems[種類] = 払った宝石)。</summary>
        Bought,
        /// <summary>場の Tier・Slot に Card が補充された(山札が尽きたら Card = -1)。</summary>
        Refilled,
        /// <summary>Player が宝石を 1 個返した(Gem)。</summary>
        Returned,
        /// <summary>館の主 Noble が Player を訪れた。</summary>
        NobleVisit,
        /// <summary>Player が目標の点に届き、最終ラウンドに入った(Amount = 点)。</summary>
        FinalRound,
        /// <summary>Player が何もできずに手番を飛ばした。</summary>
        Passed,
        /// <summary>対局が終わった。</summary>
        GameEnd,
    }

    /// <summary>画面に見せる出来事(公開情報)。CPU の手もこれを順に演出する。</summary>
    public readonly struct JewelEvent
    {
        public readonly int Seq;
        public readonly EventKind Kind;
        public readonly int Player;
        public readonly int Card;
        public readonly int Tier;
        public readonly int Slot;
        public readonly int Gem;
        public readonly int Noble;
        public readonly int Amount;
        /// <summary>宝石の種類ごとの個数(TookGems・Bought のとき。長さ 6。それ以外は null)。</summary>
        public readonly int[] Gems;

        public JewelEvent(int seq, EventKind kind, int player, int card = -1, int tier = -1, int slot = -1, int gem = -1,
            int noble = -1, int amount = 0, int[] gems = null)
        {
            Seq = seq;
            Kind = kind;
            Player = player;
            Card = card;
            Tier = tier;
            Slot = slot;
            Gem = gem;
            Noble = noble;
            Amount = amount;
            Gems = gems;
        }

        public JewelEvent WithCard(int card) => new JewelEvent(Seq, Kind, Player, card, Tier, Slot, Gem, Noble, Amount, Gems);

        public override string ToString() => $"#{Seq} {Kind} p{Player} c{Card} t{Tier} s{Slot} g{Gem} n{Noble} a{Amount}";
    }
}

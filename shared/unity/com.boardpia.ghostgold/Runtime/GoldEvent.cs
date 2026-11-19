namespace BoardGames.GhostGold
{
    public enum EventKind
    {
        /// <summary>夜が始まった(Amount = 何夜目 1..5、Amount2 = 山札の枚数)。</summary>
        NightStart,
        /// <summary>Card をめくった。金貨なら Amount = 1 人ぶんの分け前、Amount2 = 通路に残った余り。</summary>
        Reveal,
        /// <summary>同じ怪異が 2 枚そろった(Card = 2 枚目、Amount = 怪異の種類)。</summary>
        Scared,
        /// <summary>Player がおどかされて、その夜の手持ち Amount 枚を失った。</summary>
        Lost,
        /// <summary>Player がお守りを使い、手持ちの半分 Amount 枚を金庫に入れた。</summary>
        Amulet,
        /// <summary>Player が「進む・帰る」を決めた(中身は Chose で公開するまで出さない)。</summary>
        Decided,
        /// <summary>全員が決めたので公開した。Player の決定 = Amount(Choice)。</summary>
        Chose,
        /// <summary>Player が帰った。Amount = 金庫に入れた金貨(手持ち + 分け前)、Amount2 = そのうち通路の余りからの分け前。</summary>
        Returned,
        /// <summary>Player が通路の秘宝 Card をもらった(1 人だけで帰ったとき)。</summary>
        RelicTaken,
        /// <summary>夜の終わりに、通路に残った秘宝 Card がなくなった。</summary>
        RelicLost,
        /// <summary>おどかされた怪異 Card が 1 枚、次の夜から取り除かれた。</summary>
        HazardRemoved,
        /// <summary>山札が尽きたので、探検中の人は全員帰る。</summary>
        DeckEmpty,
        /// <summary>夜が終わった(Amount = 何夜目、Amount2 = 理由 NightEndReason)。</summary>
        NightEnd,
        /// <summary>対局が終わった。</summary>
        GameEnd,
    }

    /// <summary>夜が終わった理由(EventKind.NightEnd の Amount2)。</summary>
    public static class NightEndReason
    {
        /// <summary>全員が帰った。</summary>
        public const int AllReturned = 0;
        /// <summary>同じ怪異が 2 枚そろった。</summary>
        public const int Scared = 1;
        /// <summary>山札が尽きた。</summary>
        public const int DeckEmpty = 2;
    }

    /// <summary>画面に見せる出来事。このゲームの出来事はすべて全員に見える(決めた中身は Chose まで出さない)。</summary>
    public readonly struct GoldEvent
    {
        public readonly int Seq;
        public readonly EventKind Kind;
        public readonly int Player;
        public readonly int Card;
        public readonly int Amount;
        public readonly int Amount2;

        public GoldEvent(int seq, EventKind kind, int player = -1, int card = -1, int amount = 0, int amount2 = 0)
        {
            Seq = seq;
            Kind = kind;
            Player = player;
            Card = card;
            Amount = amount;
            Amount2 = amount2;
        }

        public override string ToString() => $"#{Seq} {Kind} p{Player} c{Card} a{Amount}/{Amount2}";
    }
}

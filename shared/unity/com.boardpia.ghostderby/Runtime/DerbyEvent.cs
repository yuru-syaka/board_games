namespace BoardGames.GhostDerby
{
    public enum EventKind
    {
        /// <summary>レース開始(Amount = 何レース目)。</summary>
        RaceStart,
        /// <summary>Player のコインを補充した(Amount = 増えた枚数)。</summary>
        Rescued,
        /// <summary>Player が決めた(中身は公開まで出さない)。</summary>
        Decided,
        /// <summary>先賭けの公開(Player、Card = おばけ、Amount = コイン)。</summary>
        BetShown,
        /// <summary>全員のいたずら札が山札に入り、走り始めた(Amount = 山札の枚数)。</summary>
        RunStart,
        /// <summary>走る札をめくった(Card、Amount = 歩数)。</summary>
        RunFlip,
        /// <summary>いたずら札をめくった(Player = 出した人、Card、Amount = 相手のおばけ)。</summary>
        TrickFlip,
        /// <summary>おばけが動いた(Card = おばけ、Amount = 元のマス、Amount2 = 今のマス)。</summary>
        Moved,
        /// <summary>金縛りがかかった(Card = おばけ)。</summary>
        Frozen,
        /// <summary>金縛りで 1 回休んだ(Card = おばけ)。</summary>
        Skipped,
        /// <summary>ゴールした(Card = おばけ、Amount = 着順 1..3)。</summary>
        Finished,
        /// <summary>山札が尽きたので、走る札を混ぜ直した。</summary>
        Reshuffled,
        /// <summary>追い賭けが始まった。</summary>
        ChaseStart,
        /// <summary>追い賭けの公開(Player、Card = おばけ。パスは -1、Amount = コイン、Amount2 = 配当)。</summary>
        ChaseShown,
        /// <summary>払い戻し(Player、Amount = 受け取ったコイン、Amount2 = このレースの増減)。</summary>
        Payout,
        /// <summary>レース終了(Amount = 何レース目)。</summary>
        RaceEnd,
        /// <summary>対局終了。</summary>
        GameEnd,
    }

    /// <summary>画面に見せる出来事。すべて全員に見える内容だけを入れる。</summary>
    public readonly struct DerbyEvent
    {
        public readonly int Seq;
        public readonly EventKind Kind;
        public readonly int Player;
        public readonly int Card;
        public readonly int Amount;
        public readonly int Amount2;

        public DerbyEvent(int seq, EventKind kind, int player, int card, int amount, int amount2)
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

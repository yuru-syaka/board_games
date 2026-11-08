namespace BoardGames.GhostSecret
{
    public enum EventKind
    {
        /// <summary>ラウンドが始まった(Player = 最初の手番、Amount = ラウンドの番号 1..)。</summary>
        RoundStart,
        /// <summary>Player が山札から 1 枚引いた(Card は本人だけに見える)。</summary>
        Drew,
        /// <summary>Player が Card を出した(Target・Guess は選んだ人と数字)。</summary>
        Played,
        /// <summary>ササヤキが当たった(Player が Target の数字を当てた)。</summary>
        GuessHit,
        /// <summary>ササヤキが外れた。Target が Player の手札 Card を見た(Card は 2 人だけに見える)。</summary>
        GuessMiss,
        /// <summary>Player が山札の上を見た(Card・Card2 は本人だけに見える。Amount = 見た枚数)。</summary>
        FortuneLook,
        /// <summary>Player が見たカードを戻した(Amount = 1 なら上下を入れ替えた)。</summary>
        FortuneSet,
        /// <summary>Player が手札 Card を Target に渡した(ウワサ。Card は 2 人だけに見える)。</summary>
        Passed,
        /// <summary>Player と Target が手札を比べた(Card = Player の手札、Card2 = Target の手札。2 人だけに見える)。</summary>
        Compared,
        /// <summary>Player がカクレで守られた。</summary>
        Hidden,
        /// <summary>Player のフキトバシで、Target が手札 Card を捨てた(全員に見える)。</summary>
        Blown,
        /// <summary>Player が Target の手札 Card を見た(アカリ。Card は Player だけに見える)。</summary>
        LampLook,
        /// <summary>Player が Target と手札を交換した(Amount = 1)/しなかった(Amount = 0)。</summary>
        LampSwap,
        /// <summary>選べる人がいなかったので、Player のカードの効果は何も起きなかった。</summary>
        NoEffect,
        /// <summary>Player が脱落した(Card = 手放した手札。全員に見える。Amount = 理由 EliminationReason)。</summary>
        Eliminated,
        /// <summary>山札が尽きて、Player が手札 Card を公開した(Amount = 出したカードの数字の合計)。</summary>
        Showdown,
        /// <summary>Player がラウンドに勝ち、秘密のかけらをもらった(Amount = かけらの数)。</summary>
        RoundWin,
        /// <summary>対局が終わった。</summary>
        GameEnd,
    }

    /// <summary>脱落の理由(EventKind.Eliminated の Amount)。</summary>
    public static class EliminationReason
    {
        public const int Guessed = 0;
        public const int Compared = 1;
        public const int SecretLost = 2;
        public const int Cursed = 3;
    }

    /// <summary>画面に見せる出来事。Card・Card2 は Viewers(席のビット。-1 は全員)の人だけに見える。</summary>
    public readonly struct SecretEvent
    {
        public const int Everyone = -1;

        public readonly int Seq;
        public readonly EventKind Kind;
        public readonly int Player;
        public readonly int Target;
        public readonly int Card;
        public readonly int Card2;
        public readonly int Guess;
        public readonly int Amount;
        public readonly int Viewers;

        public SecretEvent(int seq, EventKind kind, int player, int target = -1, int card = -1, int card2 = -1, int guess = -1,
            int amount = 0, int viewers = Everyone)
        {
            Seq = seq;
            Kind = kind;
            Player = player;
            Target = target;
            Card = card;
            Card2 = card2;
            Guess = guess;
            Amount = amount;
            Viewers = viewers;
        }

        public bool VisibleTo(int viewer) => Viewers == Everyone || (viewer >= 0 && (Viewers & (1 << viewer)) != 0);

        /// <summary>viewer に見せない部分(Card・Card2)を -1 にしたもの。</summary>
        public SecretEvent MaskedFor(int viewer) =>
            VisibleTo(viewer) ? this : new SecretEvent(Seq, Kind, Player, Target, -1, -1, Guess, Amount, Viewers);

        public static int Only(params int[] seats)
        {
            int mask = 0;
            foreach (int s in seats)
                if (s >= 0) mask |= 1 << s;
            return mask;
        }

        public override string ToString() => $"#{Seq} {Kind} p{Player} t{Target} c{Card}/{Card2} g{Guess} a{Amount} v{Viewers}";
    }
}

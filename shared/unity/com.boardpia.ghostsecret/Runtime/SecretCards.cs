using System;
using System.Collections.Generic;

namespace BoardGames.GhostSecret
{
    /// <summary>カードの数字(種類)。数字が大きいほど、ラウンドの終わりに強い。</summary>
    public static class Ghost
    {
        /// <summary>自分からは出せない。ほかの人のフキトバシで捨てさせられたら、フキトバシを出した人が脱落。</summary>
        public const int Curse = 0;
        /// <summary>ほかの人 1 人と数字を言い、当たればその人が脱落。外れたらその人に自分の手札を見られる。</summary>
        public const int Whisper = 1;
        /// <summary>山札の上 2 枚を見て、好きな順番で戻す。</summary>
        public const int Fortune = 2;
        /// <summary>残っている全員が、手札を次の人に渡す(カクレの人は飛ばす)。</summary>
        public const int Rumor = 3;
        /// <summary>ほかの人 1 人と手札を比べ、小さい方が脱落。</summary>
        public const int Compare = 4;
        /// <summary>次の自分の番まで、ほかの人の効果を受けない。</summary>
        public const int Hide = 5;
        /// <summary>だれか 1 人(自分も可)の手札を捨てさせ、引き直させる。</summary>
        public const int Blow = 6;
        /// <summary>ほかの人 1 人の手札を見て、交換してもよい。</summary>
        public const int Lamp = 7;
        /// <summary>手放すと脱落する。</summary>
        public const int Secret = 8;

        public const int Kinds = 9;
    }

    /// <summary>決まった数。</summary>
    public static class SecretLogic
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 4;
        public const int CardCount = 18;
        /// <summary>2 人のときに、表向きで脇に置く枚数。</summary>
        public const int FaceUpAsideForTwo = 2;
        /// <summary>この数のラウンドで決着しなければ、かけらの多い人の勝ち。</summary>
        public const int MaxRounds = 30;

        public static bool IsValidPlayerCount(int n) => n >= MinPlayers && n <= MaxPlayers;

        /// <summary>勝つのに必要な「秘密のかけら」の数(2 人 4 個、3〜4 人 3 個)。</summary>
        public static int TokensToWin(int players) => players == 2 ? 4 : 3;
    }

    /// <summary>カードの表。カード ID は 0..17 で、数字の小さい順に並ぶ。</summary>
    public static class SecretCards
    {
        static readonly int[] CountByKind = { 1, 5, 2, 2, 2, 2, 2, 1, 1 };
        static readonly string[] Names = { "ノロイ", "ササヤキ", "ウラナイ", "ウワサ", "クラベ", "カクレ", "フキトバシ", "アカリ", "ヒミツ" };
        static readonly string[] Effects =
        {
            "自分からは出せない。ほかの人のフキトバシで捨てさせられたら、フキトバシを出した人が脱落。",
            "ほかの人1人と数字(1以外)を選ぶ。当たればその人は脱落。外れたら、その人にあなたの手札を見られる。",
            "山札の上から2枚を見て、好きな順番で山札の上に戻す。",
            "残っている全員が、手札を次の人に渡す(カクレの人は飛ばす)。",
            "ほかの人1人とこっそり手札を比べ、数字が小さい方が脱落。",
            "次の自分の番が来るまで、ほかの人のカードの効果を受けない。",
            "だれか1人(自分でもよい)の手札を捨てさせ、山札から引き直させる。",
            "ほかの人1人の手札を見る。そのあと、手札を交換してもよい。",
            "出したり捨てさせられたりして手放すと、脱落する。",
        };

        static readonly int[] KindOfCard = BuildKinds();

        static int[] BuildKinds()
        {
            var kinds = new List<int>();
            for (int k = 0; k < Ghost.Kinds; k++)
                for (int i = 0; i < CountByKind[k]; i++) kinds.Add(k);
            return kinds.ToArray();
        }

        public static int Count => KindOfCard.Length;

        /// <summary>カードの数字(種類)。</summary>
        public static int ValueOf(int card)
        {
            if (card < 0 || card >= KindOfCard.Length) throw new ArgumentOutOfRangeException(nameof(card));
            return KindOfCard[card];
        }

        public static bool IsCard(int card) => card >= 0 && card < KindOfCard.Length;

        /// <summary>その数字のカードの枚数。</summary>
        public static int CountOf(int value) => CountByKind[value];

        public static string NameOf(int value) => Names[value];

        public static string EffectOf(int value) => Effects[value];

        /// <summary>ササヤキで言える数字か(1 は言えない)。</summary>
        public static bool IsGuessable(int value) => value >= 0 && value < Ghost.Kinds && value != Ghost.Whisper;

        /// <summary>出したときに相手を選ぶカードか。</summary>
        public static bool NeedsTarget(int value) =>
            value == Ghost.Whisper || value == Ghost.Compare || value == Ghost.Blow || value == Ghost.Lamp;
    }
}

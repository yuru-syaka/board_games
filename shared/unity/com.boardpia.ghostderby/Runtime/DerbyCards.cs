using System;

namespace BoardGames.GhostDerby
{
    /// <summary>ゲームの決まった数(docs/games/ghostderby.md)。</summary>
    public static class DerbyLogic
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 5;
        public const int Races = 3;
        public const int Runners = 6;
        /// <summary>ゴールのマス。</summary>
        public const int Goal = 12;
        /// <summary>先頭がこのマスを越えたら追い賭け。</summary>
        public const int ChaseLine = 6;
        /// <summary>ゴールした 3 体がそろうとレースが終わる。</summary>
        public const int Finishers = 3;
        public const int StartCoins = 10;
        /// <summary>レース開始時に、これより少ない人はここまで補充する。</summary>
        public const int RescueCoins = 3;
        public const int HandSize = 3;
        public const int MaxBet = 5;
        public const int MaxChase = 3;
        public const int RunCardsPerRunner = 5;
        public const int RunCardCount = Runners * RunCardsPerRunner;
        public const int TrickKinds = 5;
        public const int TrickCopies = 6;
        public const int TrickCount = TrickKinds * TrickCopies;
        public const int FirstTrick = RunCardCount;
        public const int CardCount = RunCardCount + TrickCount;

        public static bool IsValidPlayerCount(int n) => n >= MinPlayers && n <= MaxPlayers;
        public static bool IsRunner(int r) => r >= 0 && r < Runners;
    }

    public enum TrickKind
    {
        /// <summary>ダッシュ: 選んだおばけが 3 マス進む。</summary>
        Dash,
        /// <summary>つまずき: 選んだおばけが 2 マス戻る。</summary>
        Stumble,
        /// <summary>入れ替え: 選んだおばけが、すぐ前のおばけと場所を入れ替える。</summary>
        Swap,
        /// <summary>金縛り: 選んだおばけの、次の 1 回の移動を休みにする。</summary>
        Freeze,
        /// <summary>追い風: いちばん後ろのおばけが 2 マス進む(相手は選ばない)。</summary>
        Tailwind,
    }

    /// <summary>カード ID の読み方。0..29 は走る札、30..59 はいたずら札。</summary>
    public static class DerbyCards
    {
        /// <summary>配当の表。レースごとに 6 体へランダムに配る。</summary>
        public static readonly int[] OddsTable = { 2, 2, 3, 3, 5, 8 };

        static readonly string[] RunnerNames = { "ヒュー", "モモ", "ルナ", "ソラ", "ドロン", "ポポ" };
        static readonly string[] TrickNames = { "ダッシュ", "つまずき", "入れ替え", "金縛り", "追い風" };
        static readonly int[][] StepTable =
        {
            new[] { 2, 2, 2, 2, 3 }, // 2倍: 安定して進む
            new[] { 1, 1, 2, 2, 3 }, // 3倍
            new[] { 0, 1, 1, 2, 4 }, // 5倍: 当たりはずれが大きい
            new[] { 0, 0, 1, 1, 5 }, // 8倍: 休みが多いが、5 マスの一発がある
        };

        public static bool IsCard(int c) => c >= 0 && c < DerbyLogic.CardCount;
        public static bool IsRun(int c) => c >= 0 && c < DerbyLogic.RunCardCount;
        public static bool IsTrick(int c) => c >= DerbyLogic.FirstTrick && c < DerbyLogic.CardCount;

        public static int RunnerOf(int card) => IsRun(card) ? card / DerbyLogic.RunCardsPerRunner : -1;
        public static int RunIndex(int card) => card % DerbyLogic.RunCardsPerRunner;
        public static int RunCard(int runner, int index) => runner * DerbyLogic.RunCardsPerRunner + index;

        public static TrickKind TrickOf(int card)
        {
            if (!IsTrick(card)) throw new ArgumentOutOfRangeException(nameof(card));
            return (TrickKind)((card - DerbyLogic.FirstTrick) / DerbyLogic.TrickCopies);
        }

        public static bool NeedsTarget(TrickKind kind) => kind != TrickKind.Tailwind;

        /// <summary>走る札の歩数。配当が低いおばけほど勝ちやすい(高い配当に賭けるだけで得にならないように、いたずら札ありの CPU 対局で、同じ倍率に賭け続けても得になりすぎないように調整した)。</summary>
        public static int Steps(int odds, int index)
        {
            int row = odds <= 2 ? 0 : odds == 3 ? 1 : odds == 5 ? 2 : 3;
            return StepTable[row][index];
        }

        /// <summary>追い賭けの配当(今の順位 0 = 先頭)。</summary>
        public static int ChaseOdds(int rank) => rank <= 0 ? 1 : rank == 1 ? 2 : rank == 2 ? 3 : 5;

        public static string RunnerName(int runner) => RunnerNames[runner];
        public static string TrickName(TrickKind kind) => TrickNames[(int)kind];
    }
}

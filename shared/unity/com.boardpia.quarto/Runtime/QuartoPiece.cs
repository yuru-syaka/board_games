using System;

namespace BoardGames.Quarto
{
    /// <summary>駒の特徴。駒 Id(0..15)のビットがそのまま特徴を表す(ビットが 0 なら反対側の特徴)。</summary>
    [Flags]
    public enum QuartoTrait
    {
        None = 0,
        /// <summary>大きい(0 = 小さい)。</summary>
        Tall = 1,
        /// <summary>ピンク(0 = 青)。</summary>
        Pink = 2,
        /// <summary>しかく頭(0 = まる頭)。</summary>
        Square = 4,
        /// <summary>すけすけ(0 = ぬりつぶし)。</summary>
        Hollow = 8,
    }

    /// <summary>駒 Id と特徴の対応。16 体は 4 ビットの全組み合わせ。</summary>
    public static class QuartoPiece
    {
        public const int Count = 16;
        public const int TraitCount = 4;
        public const int AllBits = 0xF;

        /// <summary>4 つの特徴を順に並べたもの(表示や判定のループ用)。</summary>
        public static readonly QuartoTrait[] Traits = { QuartoTrait.Tall, QuartoTrait.Pink, QuartoTrait.Square, QuartoTrait.Hollow };

        public static bool IsValid(int piece) => piece >= 0 && piece < Count;

        public static bool Has(int piece, QuartoTrait trait) => (piece & (int)trait) != 0;

        public static int Create(bool tall, bool pink, bool square, bool hollow) =>
            (tall ? 1 : 0) | (pink ? 2 : 0) | (square ? 4 : 0) | (hollow ? 8 : 0);
    }
}

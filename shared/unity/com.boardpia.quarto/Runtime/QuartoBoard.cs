using System.Collections.Generic;

namespace BoardGames.Quarto
{
    /// <summary>
    /// 盤の定数とライン判定。盤は長さ 16 の int 配列(マス番号 = y * 4 + x、空きは Empty、それ以外は駒 Id)。
    /// </summary>
    public static class QuartoBoard
    {
        public const int Size = 4;
        public const int CellCount = Size * Size;
        public const int Empty = -1;

        /// <summary>
        /// 10 本のライン(マス番号 4 つずつ)。0..3 = 横(y=0..3)、4..7 = 縦(x=0..3)、8 = 斜め(左下→右上)、9 = 斜め(左上→右下)。
        /// </summary>
        public static readonly int[][] Lines = BuildLines();

        /// <summary>各マスを通るラインの番号。</summary>
        public static readonly int[][] LinesThroughCell = BuildLinesThroughCell();

        public static int Index(int x, int y) => y * Size + x;
        public static int X(int cell) => cell % Size;
        public static int Y(int cell) => cell / Size;
        public static bool InBounds(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size;

        /// <summary>4 つの駒が共有している特徴(1 側)と、共有している特徴(0 側)のビット。</summary>
        public static void SharedBits(int a, int b, int c, int d, out int ones, out int zeros)
        {
            ones = a & b & c & d;
            zeros = ~(a | b | c | d) & QuartoPiece.AllBits;
        }

        public static bool SharesTrait(int a, int b, int c, int d)
        {
            SharedBits(a, b, c, d, out int ones, out int zeros);
            return (ones | zeros) != 0;
        }

        /// <summary>ラインが 4 つ埋まっていて、特徴を共有しているか。</summary>
        public static bool IsWinningLine(IReadOnlyList<int> cells, int line)
        {
            var l = Lines[line];
            int a = cells[l[0]], b = cells[l[1]], c = cells[l[2]], d = cells[l[3]];
            if (a < 0 || b < 0 || c < 0 || d < 0) return false;
            return SharesTrait(a, b, c, d);
        }

        /// <summary>そろっているライン番号の一覧(昇順)。</summary>
        public static List<int> FindWinningLines(IReadOnlyList<int> cells)
        {
            var result = new List<int>();
            for (int i = 0; i < Lines.Length; i++)
                if (IsWinningLine(cells, i)) result.Add(i);
            return result;
        }

        /// <summary>空きマス cell に piece を置くとそろうか(盤は書き換えない)。</summary>
        public static bool PlacementWins(int[] cells, int cell, int piece)
        {
            foreach (int line in LinesThroughCell[cell])
            {
                int ones = piece, any = piece, filled = 0;
                foreach (int c in Lines[line])
                {
                    if (c == cell) continue;
                    int p = cells[c];
                    if (p < 0) break;
                    ones &= p;
                    any |= p;
                    filled++;
                }
                if (filled == 3 && ((ones | (~any & QuartoPiece.AllBits)) != 0)) return true;
            }
            return false;
        }

        /// <summary>piece をどこかの空きマスに置けばそろうか(= この駒を渡すと相手がすぐ勝てる)。</summary>
        public static bool AnyPlacementWins(int[] cells, int piece)
        {
            for (int c = 0; c < CellCount; c++)
                if (cells[c] < 0 && PlacementWins(cells, c, piece)) return true;
            return false;
        }

        static int[][] BuildLines()
        {
            var lines = new List<int[]>();
            for (int y = 0; y < Size; y++) lines.Add(new[] { Index(0, y), Index(1, y), Index(2, y), Index(3, y) });
            for (int x = 0; x < Size; x++) lines.Add(new[] { Index(x, 0), Index(x, 1), Index(x, 2), Index(x, 3) });
            lines.Add(new[] { Index(0, 0), Index(1, 1), Index(2, 2), Index(3, 3) });
            lines.Add(new[] { Index(0, 3), Index(1, 2), Index(2, 1), Index(3, 0) });
            return lines.ToArray();
        }

        static int[][] BuildLinesThroughCell()
        {
            var lines = BuildLines();
            var result = new int[CellCount][];
            for (int c = 0; c < CellCount; c++)
            {
                var list = new List<int>();
                for (int i = 0; i < lines.Length; i++)
                    if (System.Array.IndexOf(lines[i], c) >= 0) list.Add(i);
                result[c] = list.ToArray();
            }
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Vanish
{
    public enum EndReason
    {
        None,
        /// <summary>自分の駒で 1 列そろえた。</summary>
        Line,
        TurnLimit,
    }

    /// <summary>
    /// 局面(不変)。各プレイヤーの盤上の駒を「置いた順(古い順)のマス番号」で持ち、盤面はそこから求める。
    /// </summary>
    public sealed class VanishState
    {
        public const int Size = 3;
        public const int CellCount = Size * Size;
        /// <summary>1 人が盤上に置いておける駒の数。</summary>
        public const int MaxPieces = 3;
        public const int MaxPlies = 100;
        public const int Empty = -1;

        /// <summary>縦・横・斜めの 8 列(マス番号)。</summary>
        public static readonly int[][] Lines =
        {
            new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
            new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
            new[] { 0, 4, 8 }, new[] { 2, 4, 6 },
        };

        readonly int[][] _order;
        readonly int[] _cells;

        public int CurrentPlayer { get; }
        public int Ply { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }
        public VanishMove? LastMove { get; }
        /// <summary>直前の手で消えた駒のマス(消えていなければ Empty)。</summary>
        public int LastVanished { get; }
        /// <summary>決着した列のマス番号(列での決着でなければ null)。</summary>
        public IReadOnlyList<int> WinningLine { get; }

        internal VanishState(int[][] order, int currentPlayer, int ply, GameResult result, EndReason reason,
            VanishMove? lastMove, int lastVanished, int[] winningLine)
        {
            _order = order;
            _cells = new int[CellCount];
            for (int c = 0; c < CellCount; c++) _cells[c] = Empty;
            for (int p = 0; p < 2; p++)
                foreach (int c in order[p])
                    _cells[c] = p;
            CurrentPlayer = currentPlayer;
            Ply = ply;
            Result = result;
            EndReason = reason;
            LastMove = lastMove;
            LastVanished = lastVanished;
            WinningLine = winningLine;
        }

        public static VanishState Initial(int firstPlayer)
        {
            if (firstPlayer != 0 && firstPlayer != 1) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            var order = new[] { Array.Empty<int>(), Array.Empty<int>() };
            return new VanishState(order, firstPlayer, 0, GameResult.Ongoing, EndReason.None, null, Empty, null);
        }

        /// <summary>
        /// 任意の盤面から対局中の局面を作る(テスト・検証用)。order0 / order1 は各プレイヤーの駒のマスを古い順に並べたもの。
        /// 駒が 4 体以上・マスの重複・範囲外は例外。
        /// </summary>
        public static VanishState FromOrder(IReadOnlyList<int> order0, IReadOnlyList<int> order1, int currentPlayer, int ply = 0)
        {
            if (currentPlayer != 0 && currentPlayer != 1) throw new ArgumentOutOfRangeException(nameof(currentPlayer));
            var order = new int[2][];
            var used = new bool[CellCount];
            var src = new[] { order0 ?? Array.Empty<int>(), order1 ?? Array.Empty<int>() };
            for (int p = 0; p < 2; p++)
            {
                if (src[p].Count > MaxPieces) throw new ArgumentException($"player {p} has too many pieces");
                order[p] = new int[src[p].Count];
                for (int i = 0; i < src[p].Count; i++)
                {
                    int c = src[p][i];
                    if (c < 0 || c >= CellCount) throw new ArgumentException($"cell {c} out of range");
                    if (used[c]) throw new ArgumentException($"cell {c} used twice");
                    used[c] = true;
                    order[p][i] = c;
                }
            }
            return new VanishState(order, currentPlayer, ply, GameResult.Ongoing, EndReason.None, null, Empty, null);
        }

        /// <summary>マスの駒の持ち主(空なら Empty)。</summary>
        public int At(int cell) => _cells[cell];

        public bool IsEmpty(int cell) => _cells[cell] == Empty;

        /// <summary>player の盤上の駒のマス(古い順)。</summary>
        public IReadOnlyList<int> PiecesOf(int player) => _order[player];

        public int PieceCount(int player) => _order[player].Length;

        /// <summary>player が次に置いたときに消える駒のマス(3 体そろっていなければ Empty)。</summary>
        public int NextToVanish(int player) => _order[player].Length >= MaxPieces ? _order[player][0] : Empty;

        /// <summary>player の駒がそろった列を探す(無ければ null)。</summary>
        public int[] FindLine(int player) => FindLine(_cells, player);

        internal static int[] FindLine(int[] cells, int player)
        {
            foreach (var line in Lines)
                if (cells[line[0]] == player && cells[line[1]] == player && cells[line[2]] == player)
                    return line;
            return null;
        }

        /// <summary>player の駒の配列(古い順)。不変として扱い、書き換えない。</summary>
        internal int[] OrderOf(int player) => _order[player];

        public static int CellOf(int x, int y) => y * Size + x;
        public static int XOf(int cell) => cell % Size;
        public static int YOf(int cell) => cell / Size;
    }
}

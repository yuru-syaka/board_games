using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Gobblet
{
    public enum EndReason
    {
        None,
        /// <summary>自分の駒で 1 列そろえた。</summary>
        Line,
        /// <summary>駒を動かしたら下から相手の列が現れた(動かした側の負け)。</summary>
        RevealedLine,
        NoMoves,
        TurnLimit,
    }

    /// <summary>
    /// 局面(不変)。各マスは下→上の駒の積み重ね。手持ちは「大きさごとの持ち数 − 盤上の数」で求める。
    /// </summary>
    public sealed class GobbletState
    {
        public const int Size = 3;
        public const int CellCount = Size * Size;
        public const int PiecesPerSize = 2;
        public const int MaxPlies = 80;

        /// <summary>縦・横・斜めの 8 列(マス番号)。</summary>
        public static readonly int[][] Lines =
        {
            new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
            new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
            new[] { 0, 4, 8 }, new[] { 2, 4, 6 },
        };

        readonly Piece[][] _stacks;
        readonly int[] _hand;

        public int CurrentPlayer { get; }
        public int Ply { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }
        public GobbletMove? LastMove { get; }
        /// <summary>決着した列のマス番号(列での決着でなければ null)。</summary>
        public IReadOnlyList<int> WinningLine { get; }

        internal GobbletState(Piece[][] stacks, int currentPlayer, int ply, GameResult result, EndReason reason,
            GobbletMove? lastMove, int[] winningLine)
        {
            _stacks = stacks;
            _hand = CountHands(stacks);
            CurrentPlayer = currentPlayer;
            Ply = ply;
            Result = result;
            EndReason = reason;
            LastMove = lastMove;
            WinningLine = winningLine;
        }

        public static GobbletState Initial(int firstPlayer)
        {
            if (firstPlayer != 0 && firstPlayer != 1) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            var stacks = new Piece[CellCount][];
            for (int i = 0; i < CellCount; i++) stacks[i] = Array.Empty<Piece>();
            return new GobbletState(stacks, firstPlayer, 0, GameResult.Ongoing, EndReason.None, null, null);
        }

        /// <summary>
        /// 任意の盤面から対局中の局面を作る(テスト・検証用)。stacks[cell] は下→上(null は空マス)。
        /// 下ほど小さい順になっていない盤面や、駒数が持ち数を超える盤面は例外。
        /// </summary>
        public static GobbletState FromBoard(IReadOnlyList<Piece>[] stacks, int currentPlayer, int ply = 0)
        {
            if (stacks == null || stacks.Length != CellCount) throw new ArgumentException("stacks must have 9 cells");
            var copy = new Piece[CellCount][];
            for (int c = 0; c < CellCount; c++)
            {
                var s = stacks[c] ?? Array.Empty<Piece>();
                copy[c] = new Piece[s.Count];
                for (int i = 0; i < s.Count; i++)
                {
                    if (i > 0 && s[i].Size <= s[i - 1].Size)
                        throw new ArgumentException($"cell {c}: a piece must be larger than the one below");
                    copy[c][i] = s[i];
                }
            }
            var state = new GobbletState(copy, currentPlayer, ply, GameResult.Ongoing, EndReason.None, null, null);
            for (int p = 0; p < 2; p++)
            for (int z = 0; z < 3; z++)
                if (state.HandCount(p, (PieceSize)z) < 0) throw new ArgumentException("too many pieces on board");
            return state;
        }

        static int[] CountHands(Piece[][] stacks)
        {
            var hand = new int[6];
            for (int i = 0; i < hand.Length; i++) hand[i] = PiecesPerSize;
            foreach (var s in stacks)
            foreach (var p in s)
                hand[p.Owner * 3 + (int)p.Size]--;
            return hand;
        }

        public int HandCount(int player, PieceSize size) => _hand[player * 3 + (int)size];

        public IReadOnlyList<Piece> GetStack(int cell) => _stacks[cell];

        public Piece? Top(int cell)
        {
            var s = _stacks[cell];
            return s.Length == 0 ? (Piece?)null : s[s.Length - 1];
        }

        /// <summary>その大きさの駒を cell に置けるか(空か、一番上がより小さい)。</summary>
        public bool CanCover(PieceSize size, int cell)
        {
            var s = _stacks[cell];
            return s.Length == 0 || s[s.Length - 1].Size < size;
        }

        /// <summary>player の駒が一番上にそろった列を探す(無ければ null)。</summary>
        public int[] FindLine(int player) => FindLine(_stacks, player);

        internal static int[] FindLine(Piece[][] stacks, int player)
        {
            foreach (var line in Lines)
            {
                bool all = true;
                foreach (int c in line)
                {
                    var s = stacks[c];
                    if (s.Length == 0 || s[s.Length - 1].Owner != player)
                    {
                        all = false;
                        break;
                    }
                }
                if (all) return line;
            }
            return null;
        }

        internal Piece[][] CopyStacks()
        {
            var copy = new Piece[CellCount][];
            for (int i = 0; i < CellCount; i++) copy[i] = _stacks[i];
            return copy;
        }

        public static int CellOf(int x, int y) => y * Size + x;
        public static int XOf(int cell) => cell % Size;
        public static int YOf(int cell) => cell / Size;
    }
}

using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostReversa
{
    /// <summary>
    /// 完全情報の対局状態(不変)。相手の裏向きの駒の色や手駒の内訳を含むので、CPU や画面には直接渡さず、
    /// ReversaPlayerView(視点)にしてから渡す。
    /// </summary>
    public sealed class ReversaState
    {
        readonly Tile?[] _cells;
        readonly int[] _blue;
        readonly int[] _red;
        readonly TileKind[] _discards;

        public IReadOnlyList<Tile?> Cells => _cells;
        public int FirstPlayer { get; }
        public int CurrentPlayer { get; }
        /// <summary>これまでの手数(捨てる手・パスを含む)。</summary>
        public int Ply { get; }
        public GamePhase Phase { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }
        public ReversaMove? LastMove { get; }

        internal ReversaState(Tile?[] cells, int[] blue, int[] red, TileKind[] discards, int firstPlayer, int currentPlayer,
            int ply, GamePhase phase, GameResult result, EndReason endReason, ReversaMove? lastMove)
        {
            if (cells == null || cells.Length != ReversaBoard.CellCount) throw new ArgumentException("cells");
            _cells = cells;
            _blue = blue;
            _red = red;
            _discards = discards;
            FirstPlayer = firstPlayer;
            CurrentPlayer = currentPlayer;
            Ply = ply;
            Phase = phase;
            Result = result;
            EndReason = endReason;
            LastMove = lastMove;
        }

        public Tile? At(int x, int y) => _cells[ReversaBoard.Index(x, y)];

        /// <summary>手駒の kind(青か赤)の数。</summary>
        public int HandOf(int player, TileKind kind) => kind == TileKind.Blue ? _blue[player] : kind == TileKind.Red ? _red[player] : 0;

        public int HandCount(int player) => _blue[player] + _red[player];

        /// <summary>捨てた色(まだ捨てていなければ Unknown)。</summary>
        public TileKind DiscardOf(int player) => _discards[player];

        public bool PoltergeistOnBoard => ReversaBoard.PoltergeistOnBoard(_cells);

        public int FaceUpCount(int player, TileKind kind) => ReversaBoard.FaceUpCount(_cells, player, kind);

        internal Tile?[] CopyCells() => (Tile?[])_cells.Clone();
        internal int[] CopyBlue() => (int[])_blue.Clone();
        internal int[] CopyRed() => (int[])_red.Clone();
        internal TileKind[] CopyDiscards() => (TileKind[])_discards.Clone();

        public List<ReversaMove> GetLegalMoves(int player) =>
            Result.IsOver || player != CurrentPlayer
                ? new List<ReversaMove>()
                : ReversaBoard.GenerateMoves(Phase, _cells, player, _blue[player], _red[player]);

        public bool HasPlacement(int player) => ReversaBoard.HasPlacement(_cells, player, _blue[player], _red[player]);
    }
}

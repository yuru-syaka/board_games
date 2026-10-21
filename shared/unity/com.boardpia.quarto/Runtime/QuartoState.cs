using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Quarto
{
    /// <summary>手番の中のどの行動を待っているか。</summary>
    public enum TurnPhase
    {
        /// <summary>手番プレイヤーが相手に渡す駒を選ぶ。</summary>
        Give,
        /// <summary>手番プレイヤーが渡された駒(PieceInHand)を置く。</summary>
        Place,
    }

    public enum EndReason
    {
        None,
        /// <summary>勝者が置いた駒でラインがそろった。</summary>
        Quarto,
        /// <summary>16 個置き終わってもそろわなかった(引き分け)。</summary>
        BoardFull,
    }

    /// <summary>対局状態(不変)。非公開情報は無いので CPU・表示・通信のすべてでこのまま使う。</summary>
    public sealed class QuartoState
    {
        readonly int[] _cells;
        readonly int[] _winningLines;

        /// <summary>16 マス(マス番号 = y * 4 + x)。空きは QuartoBoard.Empty。</summary>
        public IReadOnlyList<int> Cells => _cells;
        public int CurrentPlayer { get; }
        public TurnPhase Phase { get; }
        /// <summary>手番プレイヤーが置くために渡された駒(Phase == Place のとき)。無ければ -1。</summary>
        public int PieceInHand { get; }
        /// <summary>これまでの行動数(置く・渡すをそれぞれ 1 と数える)。</summary>
        public int Ply { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }
        public QuartoMove? LastMove { get; }
        /// <summary>そろったライン番号(QuartoBoard.Lines の添字)。決着していなければ空。</summary>
        public IReadOnlyList<int> WinningLines => _winningLines;

        internal QuartoState(int[] cells, int currentPlayer, TurnPhase phase, int pieceInHand, int ply, GameResult result,
            EndReason endReason, QuartoMove? lastMove, int[] winningLines)
        {
            _cells = cells;
            CurrentPlayer = currentPlayer;
            Phase = phase;
            PieceInHand = pieceInHand;
            Ply = ply;
            Result = result;
            EndReason = endReason;
            LastMove = lastMove;
            _winningLines = winningLines ?? System.Array.Empty<int>();
        }

        internal int[] CopyCells() => (int[])_cells.Clone();

        public int PieceAt(int x, int y) => _cells[QuartoBoard.Index(x, y)];

        public bool IsOnBoard(int piece) => System.Array.IndexOf(_cells, piece) >= 0;

        /// <summary>まだ盤にも手もちにも無い(= 渡せる)駒か。</summary>
        public bool IsAvailable(int piece) =>
            QuartoPiece.IsValid(piece) && piece != PieceInHand && !IsOnBoard(piece);

        public List<int> AvailablePieces()
        {
            var list = new List<int>(QuartoPiece.Count);
            for (int p = 0; p < QuartoPiece.Count; p++)
                if (IsAvailable(p)) list.Add(p);
            return list;
        }

        public int PlacedCount
        {
            get
            {
                int n = 0;
                foreach (int p in _cells)
                    if (p >= 0) n++;
                return n;
            }
        }
    }
}

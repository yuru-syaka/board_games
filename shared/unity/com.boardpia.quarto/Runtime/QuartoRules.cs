using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Quarto
{
    public sealed class QuartoRules : IGameRules<QuartoState, QuartoMove>
    {
        public IReadOnlyList<QuartoMove> GetLegalMoves(QuartoState state)
        {
            if (state.Result.IsOver) return Array.Empty<QuartoMove>();
            var moves = new List<QuartoMove>(QuartoBoard.CellCount);
            if (state.Phase == TurnPhase.Place)
            {
                for (int c = 0; c < QuartoBoard.CellCount; c++)
                    if (state.Cells[c] == QuartoBoard.Empty) moves.Add(QuartoMove.Place(QuartoBoard.X(c), QuartoBoard.Y(c)));
            }
            else
            {
                foreach (int p in state.AvailablePieces()) moves.Add(QuartoMove.Give(p));
            }
            return moves;
        }

        public bool IsLegal(QuartoState state, QuartoMove move)
        {
            if (state.Result.IsOver) return false;
            if (move.IsPlace)
            {
                return state.Phase == TurnPhase.Place && move.Piece == -1 && QuartoBoard.InBounds(move.X, move.Y) &&
                       state.Cells[move.Cell] == QuartoBoard.Empty;
            }
            return state.Phase == TurnPhase.Give && move.X == -1 && move.Y == -1 && state.IsAvailable(move.Piece);
        }

        public QuartoState ApplyMove(QuartoState state, QuartoMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException($"Illegal move: {move}");

            int mover = state.CurrentPlayer;
            int ply = state.Ply + 1;

            if (move.IsGive)
            {
                return new QuartoState(state.CopyCells(), 1 - mover, TurnPhase.Place, move.Piece, ply, GameResult.Ongoing,
                    EndReason.None, move, null);
            }

            var cells = state.CopyCells();
            cells[move.Cell] = state.PieceInHand;

            // 置く前はどのラインもそろっていないので、そろったラインは全て今置いた駒によるもの
            var lines = QuartoBoard.FindWinningLines(cells);
            if (lines.Count > 0)
            {
                return new QuartoState(cells, mover, TurnPhase.Place, -1, ply, GameResult.WinFor(mover), EndReason.Quarto, move,
                    lines.ToArray());
            }

            bool full = Array.IndexOf(cells, QuartoBoard.Empty) < 0;
            if (full)
                return new QuartoState(cells, mover, TurnPhase.Place, -1, ply, GameResult.Draw, EndReason.BoardFull, move, null);

            return new QuartoState(cells, mover, TurnPhase.Give, -1, ply, GameResult.Ongoing, EndReason.None, move, null);
        }

        public GameResult GetResult(QuartoState state) => state.Result;
    }
}

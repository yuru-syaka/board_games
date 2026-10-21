using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Geister
{
    public sealed class GeisterRules : IGameRules<GeisterState, GeisterMove>
    {
        public IReadOnlyList<GeisterMove> GetLegalMoves(GeisterState state)
        {
            if (state.Result.IsOver) return Array.Empty<GeisterMove>();
            return GeisterBoard.GenerateMoves(state.Ghosts, state.CurrentPlayer);
        }

        public bool IsLegal(GeisterState state, GeisterMove move)
        {
            foreach (var m in GetLegalMoves(state))
                if (m.Equals(move)) return true;
            return false;
        }

        public GeisterState ApplyMove(GeisterState state, GeisterMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException($"Illegal move: {move}");

            int mover = state.CurrentPlayer;
            int opponent = 1 - mover;
            var ghosts = state.CopyGhosts();
            int moverIndex = GeisterBoard.FindById(ghosts, move.GhostId);
            int capturedId = -1;
            GameResult result = GameResult.Ongoing;
            EndReason reason = EndReason.None;

            if (move.IsEscape)
            {
                ghosts[moverIndex] = ghosts[moverIndex].WithStatus(GhostStatus.Escaped);
                result = GameResult.WinFor(mover);
                reason = EndReason.Escaped;
            }
            else
            {
                int target = GeisterBoard.FindAt(ghosts, move.ToX, move.ToY);
                if (target >= 0)
                {
                    capturedId = ghosts[target].Id;
                    ghosts[target] = ghosts[target].WithStatus(GhostStatus.Captured);
                }
                ghosts[moverIndex] = ghosts[moverIndex].MovedTo(move.ToX, move.ToY);

                if (GeisterBoard.Count(ghosts, opponent, GhostType.Good, GhostStatus.Captured) == GeisterBoard.GoodPerPlayer)
                {
                    result = GameResult.WinFor(mover);
                    reason = EndReason.CapturedAllGood;
                }
                else if (GeisterBoard.Count(ghosts, opponent, GhostType.Evil, GhostStatus.Captured) == GeisterBoard.EvilPerPlayer)
                {
                    result = GameResult.WinFor(opponent);
                    reason = EndReason.LostAllEvil;
                }
            }

            int ply = state.Ply + 1;
            if (!result.IsOver)
            {
                if (GeisterBoard.GenerateMoves(ghosts, opponent).Count == 0)
                {
                    result = GameResult.WinFor(mover);
                    reason = EndReason.NoMoves;
                }
                else if (ply >= GeisterBoard.MaxPlies)
                {
                    result = GameResult.Draw;
                    reason = EndReason.TurnLimit;
                }
            }

            return new GeisterState(ghosts, opponent, ply, result, reason, move, capturedId);
        }

        public GameResult GetResult(GeisterState state) => state.Result;
    }
}

using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Gobblet
{
    public sealed class GobbletRules : IGameRules<GobbletState, GobbletMove>
    {
        public IReadOnlyList<GobbletMove> GetLegalMoves(GobbletState state)
        {
            if (state.Result.IsOver) return Array.Empty<GobbletMove>();
            var moves = new List<GobbletMove>();
            AddMoves(state, state.CurrentPlayer, moves);
            return moves;
        }

        /// <summary>手持ちから置く手 → 盤上の駒を動かす手 の順に列挙する。</summary>
        internal static void AddMoves(GobbletState state, int player, List<GobbletMove> moves)
        {
            for (int z = 0; z < 3; z++)
            {
                var size = (PieceSize)z;
                if (state.HandCount(player, size) <= 0) continue;
                for (int to = 0; to < GobbletState.CellCount; to++)
                    if (state.CanCover(size, to)) moves.Add(GobbletMove.Place(size, to));
            }

            for (int from = 0; from < GobbletState.CellCount; from++)
            {
                var top = state.Top(from);
                if (!top.HasValue || top.Value.Owner != player) continue;
                for (int to = 0; to < GobbletState.CellCount; to++)
                    if (to != from && state.CanCover(top.Value.Size, to)) moves.Add(GobbletMove.Move(from, top.Value.Size, to));
            }
        }

        static bool HasAnyMove(GobbletState state, int player)
        {
            var moves = new List<GobbletMove>();
            AddMoves(state, player, moves);
            return moves.Count > 0;
        }

        public bool IsLegal(GobbletState state, GobbletMove move)
        {
            if (state.Result.IsOver) return false;
            if (move.To < 0 || move.To >= GobbletState.CellCount) return false;
            if (!state.CanCover(move.Size, move.To)) return false;
            if (move.IsFromHand) return state.HandCount(state.CurrentPlayer, move.Size) > 0;
            if (move.From < 0 || move.From >= GobbletState.CellCount || move.From == move.To) return false;
            var top = state.Top(move.From);
            return top.HasValue && top.Value.Owner == state.CurrentPlayer && top.Value.Size == move.Size;
        }

        public GobbletState ApplyMove(GobbletState state, GobbletMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException($"Illegal move: {move}");
            return ApplyUnchecked(state, move);
        }

        /// <summary>合法性チェックを省いた ApplyMove(CPU の探索用)。</summary>
        internal static GobbletState ApplyUnchecked(GobbletState state, GobbletMove move)
        {
            int mover = state.CurrentPlayer;
            int opponent = 1 - mover;
            var stacks = state.CopyStacks();

            if (!move.IsFromHand)
            {
                var from = stacks[move.From];
                var lifted = new Piece[from.Length - 1];
                Array.Copy(from, lifted, lifted.Length);
                stacks[move.From] = lifted;
            }
            var dest = stacks[move.To];
            var pushed = new Piece[dest.Length + 1];
            Array.Copy(dest, pushed, dest.Length);
            pushed[dest.Length] = new Piece(mover, move.Size);
            stacks[move.To] = pushed;

            var result = GameResult.Ongoing;
            var reason = EndReason.None;
            // 持ち上げて現れた相手の列を、自分の列より優先する
            int[] line = GobbletState.FindLine(stacks, opponent);
            if (line != null)
            {
                result = GameResult.WinFor(opponent);
                reason = EndReason.RevealedLine;
            }
            else if ((line = GobbletState.FindLine(stacks, mover)) != null)
            {
                result = GameResult.WinFor(mover);
                reason = EndReason.Line;
            }

            int ply = state.Ply + 1;
            var next = new GobbletState(stacks, opponent, ply, result, reason, move, line);
            if (result.IsOver) return next;

            if (!HasAnyMove(next, opponent))
                return new GobbletState(stacks, opponent, ply, GameResult.WinFor(mover), EndReason.NoMoves, move, null);
            if (ply >= GobbletState.MaxPlies)
                return new GobbletState(stacks, opponent, ply, GameResult.Draw, EndReason.TurnLimit, move, null);
            return next;
        }

        public GameResult GetResult(GobbletState state) => state.Result;
    }
}

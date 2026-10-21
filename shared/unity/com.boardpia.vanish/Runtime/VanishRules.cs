using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Vanish
{
    public sealed class VanishRules : IGameRules<VanishState, VanishMove>
    {
        public IReadOnlyList<VanishMove> GetLegalMoves(VanishState state)
        {
            if (state.Result.IsOver) return Array.Empty<VanishMove>();
            var moves = new List<VanishMove>();
            AddMoves(state, moves);
            return moves;
        }

        /// <summary>空いているマスに置く手を列挙する(消える予定の自分の駒のマスは、置く前はまだ埋まっているので含まない)。</summary>
        internal static void AddMoves(VanishState state, List<VanishMove> moves)
        {
            for (int c = 0; c < VanishState.CellCount; c++)
                if (state.IsEmpty(c)) moves.Add(VanishMove.Place(c));
        }

        public bool IsLegal(VanishState state, VanishMove move) =>
            !state.Result.IsOver && move.Cell >= 0 && move.Cell < VanishState.CellCount && state.IsEmpty(move.Cell);

        public VanishState ApplyMove(VanishState state, VanishMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException($"Illegal move: {move}");
            return ApplyUnchecked(state, move);
        }

        /// <summary>合法性チェックを省いた ApplyMove(CPU の探索用)。</summary>
        internal static VanishState ApplyUnchecked(VanishState state, VanishMove move)
        {
            int mover = state.CurrentPlayer;
            int opponent = 1 - mover;
            var prev = state.OrderOf(mover);

            // 3 体そろっていれば一番古い駒が消え、新しい駒を末尾に足す
            int vanished = VanishState.Empty;
            int skip = 0;
            if (prev.Length >= VanishState.MaxPieces)
            {
                vanished = prev[0];
                skip = 1;
            }
            var mine = new int[prev.Length - skip + 1];
            Array.Copy(prev, skip, mine, 0, prev.Length - skip);
            mine[mine.Length - 1] = move.Cell;

            var order = new int[2][];
            order[mover] = mine;
            order[opponent] = state.OrderOf(opponent);

            int ply = state.Ply + 1;
            var result = GameResult.Ongoing;
            var reason = EndReason.None;
            int[] line = mine.Length == VanishState.MaxPieces ? FindLine(mine) : null;
            if (line != null)
            {
                result = GameResult.WinFor(mover);
                reason = EndReason.Line;
            }
            else if (ply >= VanishState.MaxPlies)
            {
                result = GameResult.Draw;
                reason = EndReason.TurnLimit;
            }
            return new VanishState(order, opponent, ply, result, reason, move, vanished, line);
        }

        /// <summary>3 体の駒のマスが 1 列になっていればその列を返す。</summary>
        static int[] FindLine(int[] pieces)
        {
            foreach (var line in VanishState.Lines)
            {
                bool all = true;
                foreach (int c in line)
                    if (Array.IndexOf(pieces, c) < 0)
                    {
                        all = false;
                        break;
                    }
                if (all) return line;
            }
            return null;
        }

        public GameResult GetResult(VanishState state) => state.Result;
    }
}

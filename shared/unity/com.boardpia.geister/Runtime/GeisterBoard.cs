using System;
using System.Collections.Generic;

namespace BoardGames.Geister
{
    /// <summary>盤面の定数と、状態・視点の両方から使う共通処理。</summary>
    public static class GeisterBoard
    {
        public const int Size = 6;
        public const int GhostsPerPlayer = 8;
        public const int GoodPerPlayer = 4;
        public const int EvilPerPlayer = 4;
        public const int MaxPlies = 200;

        static readonly int[] Dx = { 0, 1, 0, -1 };
        static readonly int[] Dy = { 1, 0, -1, 0 };

        public static bool InBounds(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size;

        /// <summary>player の善玉が脱出できる角(相手側の角)か。</summary>
        public static bool IsEscapeSquare(int player, int x, int y)
        {
            int row = player == 0 ? Size - 1 : 0;
            return y == row && (x == 0 || x == Size - 1);
        }

        /// <summary>player が守るべき自陣の角(相手が脱出に使う角)か。</summary>
        public static bool IsOwnCorner(int player, int x, int y) => IsEscapeSquare(1 - player, x, y);

        public static bool IsAdjacent(int x1, int y1, int x2, int y2) =>
            Math.Abs(x1 - x2) + Math.Abs(y1 - y2) == 1;

        /// <summary>player の脱出用の角までの最短マンハッタン距離。</summary>
        public static int DistanceToEscape(int player, int x, int y)
        {
            int row = player == 0 ? Size - 1 : 0;
            return Math.Abs(row - y) + Math.Min(x, Size - 1 - x);
        }

        public static int FindAt(IReadOnlyList<Ghost> ghosts, int x, int y)
        {
            for (int i = 0; i < ghosts.Count; i++)
            {
                var g = ghosts[i];
                if (g.IsOnBoard && g.X == x && g.Y == y) return i;
            }
            return -1;
        }

        public static int FindById(IReadOnlyList<Ghost> ghosts, int id)
        {
            for (int i = 0; i < ghosts.Count; i++)
                if (ghosts[i].Id == id) return i;
            return -1;
        }

        public static int Count(IReadOnlyList<Ghost> ghosts, int owner, GhostType type, GhostStatus status)
        {
            int n = 0;
            foreach (var g in ghosts)
                if (g.Owner == owner && g.Type == type && g.Status == status) n++;
            return n;
        }

        /// <summary>
        /// player の合法手を列挙する。自分の駒の種類さえ分かれば計算できるので PlayerView からも使える。
        /// </summary>
        public static List<GeisterMove> GenerateMoves(IReadOnlyList<Ghost> ghosts, int player)
        {
            var moves = new List<GeisterMove>(32);
            foreach (var g in ghosts)
            {
                if (!g.IsOnBoard || g.Owner != player) continue;

                for (int d = 0; d < 4; d++)
                {
                    int nx = g.X + Dx[d], ny = g.Y + Dy[d];
                    if (!InBounds(nx, ny)) continue;
                    int occupant = FindAt(ghosts, nx, ny);
                    if (occupant >= 0 && ghosts[occupant].Owner == player) continue;
                    moves.Add(GeisterMove.Step(g.Id, g.X, g.Y, nx, ny));
                }

                if (g.Type == GhostType.Good && IsEscapeSquare(player, g.X, g.Y))
                    moves.Add(GeisterMove.Escape(g.Id, g.X, g.Y));
            }
            return moves;
        }
    }
}

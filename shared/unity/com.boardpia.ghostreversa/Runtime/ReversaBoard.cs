using System.Collections.Generic;

namespace BoardGames.GhostReversa
{
    /// <summary>対局の段階。</summary>
    public enum GamePhase
    {
        /// <summary>準備: 先手 → 後手の順に 1 体ずつ捨てる。</summary>
        Discard = 0,
        /// <summary>対局中。</summary>
        Play = 1,
    }

    /// <summary>決着の理由。</summary>
    public enum EndReason
    {
        None = 0,
        /// <summary>指した人が自分の同じ色を 3 体表にした。</summary>
        Completed = 1,
        /// <summary>指した人が、相手の同じ色を 3 体表にしてしまった。</summary>
        Gifted = 2,
        /// <summary>両者とも置ける手がない(引き分け)。</summary>
        NoMoves = 3,
        /// <summary>手数の上限(引き分け)。</summary>
        TurnLimit = 4,
    }

    /// <summary>
    /// 盤の定数と、State / PlayerView の両方から使う共通処理。
    /// マスは index = y * Size + x(y = 0 が P0 側の行)。
    /// </summary>
    public static class ReversaBoard
    {
        public const int Size = 3;
        public const int CellCount = Size * Size;
        /// <summary>各プレイヤーが最初に持つ、1 色あたりの駒の数。</summary>
        public const int PiecesPerColor = 3;
        /// <summary>勝つために表にする同色の駒の数。</summary>
        public const int WinCount = 3;
        /// <summary>この手数(捨てる手を含む)に達したら引き分け。</summary>
        public const int MaxPlies = 80;

        public static bool InBounds(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size;

        public static int Index(int x, int y) => y * Size + x;

        public static bool IsColor(TileKind kind) => kind == TileKind.Blue || kind == TileKind.Red;

        public static bool PoltergeistOnBoard(IReadOnlyList<Tile?> cells)
        {
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].HasValue && cells[i].Value.IsPoltergeist) return true;
            return false;
        }

        /// <summary>player の表向きの kind の駒の数。</summary>
        public static int FaceUpCount(IReadOnlyList<Tile?> cells, int player, TileKind kind)
        {
            int n = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                var t = cells[i];
                if (t.HasValue && t.Value.FaceUp && t.Value.Owner == player && t.Value.Kind == kind) n++;
            }
            return n;
        }

        /// <summary>player が同じ色を WinCount 体表にしているか。</summary>
        public static bool HasWinningSet(IReadOnlyList<Tile?> cells, int player) =>
            FaceUpCount(cells, player, TileKind.Blue) >= WinCount || FaceUpCount(cells, player, TileKind.Red) >= WinCount;

        /// <summary>player が (x, y) に手駒を置けるか(空きマス、または相手の裏向きの駒)。</summary>
        public static bool CanPlaceColorAt(IReadOnlyList<Tile?> cells, int player, int x, int y)
        {
            var t = cells[Index(x, y)];
            if (!t.HasValue) return true;
            var tile = t.Value;
            return !tile.FaceUp && !tile.IsPoltergeist && tile.Owner == 1 - player;
        }

        /// <summary>
        /// 合法手の一覧。自分の手駒の数と、盤上の駒の持ち主・表裏だけで決まるので、視点(相手の駒の色が見えない)からも使える。
        /// 準備中は捨てる手、置ける手が無ければパスだけ。
        /// </summary>
        public static List<ReversaMove> GenerateMoves(GamePhase phase, IReadOnlyList<Tile?> cells, int player, int blueInHand, int redInHand)
        {
            var moves = new List<ReversaMove>();
            if (phase == GamePhase.Discard)
            {
                if (blueInHand > 0) moves.Add(ReversaMove.Discard(TileKind.Blue));
                if (redInHand > 0) moves.Add(ReversaMove.Discard(TileKind.Red));
                return moves;
            }

            AddPlacements(moves, cells, player, blueInHand, redInHand);
            if (moves.Count == 0) moves.Add(ReversaMove.Pass());
            return moves;
        }

        /// <summary>置く手が 1 つでもあるか(パス・引き分けの判定用)。</summary>
        public static bool HasPlacement(IReadOnlyList<Tile?> cells, int player, int blueInHand, int redInHand)
        {
            bool polterFree = !PoltergeistOnBoard(cells);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var t = cells[Index(x, y)];
                if (polterFree && !t.HasValue) return true;
                if ((blueInHand > 0 || redInHand > 0) && CanPlaceColorAt(cells, player, x, y)) return true;
            }
            return false;
        }

        static void AddPlacements(List<ReversaMove> moves, IReadOnlyList<Tile?> cells, int player, int blueInHand, int redInHand)
        {
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (!CanPlaceColorAt(cells, player, x, y)) continue;
                if (blueInHand > 0)
                {
                    moves.Add(ReversaMove.Place(TileKind.Blue, x, y, true));
                    moves.Add(ReversaMove.Place(TileKind.Blue, x, y, false));
                }
                if (redInHand > 0)
                {
                    moves.Add(ReversaMove.Place(TileKind.Red, x, y, true));
                    moves.Add(ReversaMove.Place(TileKind.Red, x, y, false));
                }
            }

            if (PoltergeistOnBoard(cells)) return;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (!cells[Index(x, y)].HasValue)
                    moves.Add(ReversaMove.Place(TileKind.Poltergeist, x, y, true));
        }

        /// <summary>(x, y) に kind を表で置いたときに裏返るマス(置いたマス自身は含まない)。</summary>
        public static List<int> FlipTargets(IReadOnlyList<Tile?> cells, TileKind kind, int x, int y)
        {
            var result = new List<int>();
            for (int cy = 0; cy < Size; cy++)
            for (int cx = 0; cx < Size; cx++)
            {
                if (cx == x && cy == y) continue;
                bool hit = kind == TileKind.Poltergeist || (kind == TileKind.Blue && cy == y) || (kind == TileKind.Red && cx == x);
                int i = Index(cx, cy);
                if (hit && cells[i].HasValue) result.Add(i);
            }
            return result;
        }

        /// <summary>
        /// 置く手を cells(コピー済みの配列)に適用する。相手の裏向きの駒を上書きしたら、その駒を returned に返す。
        /// 表で置いたら裏返し、裏になったポルターガイストは盤から取り除く。
        /// </summary>
        public static void ApplyPlacement(Tile?[] cells, int player, ReversaMove move, out Tile? returned)
        {
            int at = Index(move.X, move.Y);
            returned = cells[at];
            var placed = move.Piece == TileKind.Poltergeist ? Tile.Poltergeist : new Tile(player, move.Piece, move.FaceUp);
            cells[at] = placed;
            if (!placed.FaceUp) return;

            foreach (int i in FlipTargets(cells, move.Piece, move.X, move.Y))
            {
                var flipped = cells[i].Value.Flipped();
                cells[i] = flipped.IsPoltergeist && !flipped.FaceUp ? (Tile?)null : flipped;
            }
        }
    }
}

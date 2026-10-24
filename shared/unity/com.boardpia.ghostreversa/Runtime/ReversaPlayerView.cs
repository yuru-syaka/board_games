using System;
using System.Collections.Generic;

namespace BoardGames.GhostReversa
{
    /// <summary>
    /// あるプレイヤー(Viewer)から見た局面(不変)。CPU と画面にはこれだけを渡す。
    /// 伏せるもの: 相手の裏向きの駒の色、相手の手駒の内訳(合計数だけ見える)、相手が捨てた色、
    /// 相手の直前の手が裏置き・捨てる手だったときの色。決着後は CreateRevealed で全て公開する。
    /// </summary>
    public sealed class ReversaPlayerView
    {
        public const int Hidden = -1;

        readonly Tile?[] _cells;
        readonly int[] _blue;     // 相手の分は Hidden
        readonly int[] _red;      // 相手の分は Hidden
        readonly int[] _total;
        readonly TileKind[] _discards; // 見えない分は Unknown

        public int Viewer { get; }
        public int FirstPlayer { get; }
        public int CurrentPlayer { get; }
        public int Ply { get; }
        public GamePhase Phase { get; }
        public ReversaMove? LastMove { get; }
        public IReadOnlyList<Tile?> Cells => _cells;

        ReversaPlayerView(Tile?[] cells, int[] blue, int[] red, int[] total, TileKind[] discards, int viewer, int firstPlayer,
            int currentPlayer, int ply, GamePhase phase, ReversaMove? lastMove)
        {
            _cells = cells;
            _blue = blue;
            _red = red;
            _total = total;
            _discards = discards;
            Viewer = viewer;
            FirstPlayer = firstPlayer;
            CurrentPlayer = currentPlayer;
            Ply = ply;
            Phase = phase;
            LastMove = lastMove;
        }

        /// <summary>対局中の視点(相手の非公開情報を伏せる)。</summary>
        public static ReversaPlayerView Create(ReversaState state, int viewer) => Build(state, viewer, false);

        /// <summary>決着後の視点(全て公開)。</summary>
        public static ReversaPlayerView CreateRevealed(ReversaState state, int viewer) => Build(state, viewer, true);

        static ReversaPlayerView Build(ReversaState state, int viewer, bool revealed)
        {
            if (viewer != 0 && viewer != 1) throw new ArgumentOutOfRangeException(nameof(viewer));
            int opp = 1 - viewer;
            var cells = new Tile?[ReversaBoard.CellCount];
            for (int i = 0; i < cells.Length; i++)
            {
                var t = state.Cells[i];
                if (t.HasValue && !revealed && !t.Value.FaceUp && t.Value.Owner == opp) t = t.Value.WithKind(TileKind.Unknown);
                cells[i] = t;
            }

            var blue = new[] { state.HandOf(0, TileKind.Blue), state.HandOf(1, TileKind.Blue) };
            var red = new[] { state.HandOf(0, TileKind.Red), state.HandOf(1, TileKind.Red) };
            var total = new[] { state.HandCount(0), state.HandCount(1) };
            var discards = new[] { state.DiscardOf(0), state.DiscardOf(1) };
            var last = state.LastMove;
            if (!revealed)
            {
                blue[opp] = Hidden;
                red[opp] = Hidden;
                discards[opp] = TileKind.Unknown;
                if (last.HasValue && state.CurrentPlayer == viewer && HidesPiece(last.Value)) last = last.Value.Hidden();
            }
            return new ReversaPlayerView(cells, blue, red, total, discards, viewer, state.FirstPlayer, state.CurrentPlayer,
                state.Ply, state.Phase, last);
        }

        /// <summary>相手の手のうち、色を見せてはいけないもの(捨てる手・裏置き)。</summary>
        static bool HidesPiece(ReversaMove m) =>
            m.Kind == MoveKind.Discard || (m.Kind == MoveKind.Place && !m.FaceUp && m.Piece != TileKind.Poltergeist);

        /// <summary>通信で受け取った値から組み立てる(値の検証は呼び出し側で行う)。</summary>
        public static ReversaPlayerView FromParts(IReadOnlyList<Tile?> cells, int[] blue, int[] red, int[] total, TileKind[] discards,
            int viewer, int firstPlayer, int currentPlayer, int ply, GamePhase phase, ReversaMove? lastMove)
        {
            var c = new Tile?[ReversaBoard.CellCount];
            for (int i = 0; i < c.Length; i++) c[i] = cells[i];
            return new ReversaPlayerView(c, (int[])blue.Clone(), (int[])red.Clone(), (int[])total.Clone(),
                (TileKind[])discards.Clone(), viewer, firstPlayer, currentPlayer, ply, phase, lastMove);
        }

        public Tile? At(int x, int y) => _cells[ReversaBoard.Index(x, y)];

        /// <summary>手駒の kind の数。見えない(相手の内訳)なら Hidden。</summary>
        public int HandOf(int player, TileKind kind) => kind == TileKind.Blue ? _blue[player] : kind == TileKind.Red ? _red[player] : 0;

        public int HandCount(int player) => _total[player];

        /// <summary>捨てた色。まだ捨てていない・見えないなら Unknown。</summary>
        public TileKind DiscardOf(int player) => _discards[player];

        public bool PoltergeistOnBoard => ReversaBoard.PoltergeistOnBoard(_cells);

        public int FaceUpCount(int player, TileKind kind) => ReversaBoard.FaceUpCount(_cells, player, kind);

        /// <summary>Viewer の合法手(Viewer の手番でなければ空)。</summary>
        public List<ReversaMove> GetLegalMoves()
        {
            if (CurrentPlayer != Viewer || _blue[Viewer] < 0) return new List<ReversaMove>();
            return ReversaBoard.GenerateMoves(Phase, _cells, Viewer, _blue[Viewer], _red[Viewer]);
        }
    }
}

using System.Collections.Generic;

namespace BoardGames.Geister
{
    /// <summary>
    /// あるプレイヤーから見た状態。盤上にいる相手の駒の種類は Unknown に伏せられる
    /// (取られた駒・脱出した駒は公開情報なので見える)。
    /// </summary>
    public sealed class GeisterPlayerView
    {
        readonly Ghost[] _ghosts;

        public int Viewer { get; }
        public int CurrentPlayer { get; }
        public int Ply { get; }
        public IReadOnlyList<Ghost> Ghosts => _ghosts;
        public GeisterMove? LastMove { get; }

        GeisterPlayerView(Ghost[] ghosts, int viewer, int currentPlayer, int ply, GeisterMove? lastMove)
        {
            _ghosts = ghosts;
            Viewer = viewer;
            CurrentPlayer = currentPlayer;
            Ply = ply;
            LastMove = lastMove;
        }

        public static GeisterPlayerView Create(GeisterState state, int viewer)
        {
            var ghosts = new Ghost[state.Ghosts.Count];
            for (int i = 0; i < ghosts.Length; i++)
            {
                var g = state.Ghosts[i];
                bool hidden = g.Owner != viewer && g.IsOnBoard;
                ghosts[i] = hidden ? g.WithType(GhostType.Unknown) : g;
            }
            return new GeisterPlayerView(ghosts, viewer, state.CurrentPlayer, state.Ply, state.LastMove);
        }

        /// <summary>全ての駒の種類を明かした視点(対局終了後の公開用)。</summary>
        public static GeisterPlayerView CreateRevealed(GeisterState state, int viewer)
        {
            var ghosts = new Ghost[state.Ghosts.Count];
            for (int i = 0; i < ghosts.Length; i++) ghosts[i] = state.Ghosts[i];
            return new GeisterPlayerView(ghosts, viewer, state.CurrentPlayer, state.Ply, state.LastMove);
        }

        /// <summary>通信で受け取った値から視点を組み立てる。</summary>
        public static GeisterPlayerView FromParts(IReadOnlyList<Ghost> ghosts, int viewer, int currentPlayer, int ply,
            GeisterMove? lastMove)
        {
            var copy = new Ghost[ghosts.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = ghosts[i];
            return new GeisterPlayerView(copy, viewer, currentPlayer, ply, lastMove);
        }

        public Ghost GetGhost(int id) => _ghosts[GeisterBoard.FindById(_ghosts, id)];

        /// <summary>(x, y) にいる駒。いなければ null。</summary>
        public Ghost? GhostAt(int x, int y)
        {
            int i = GeisterBoard.FindAt(_ghosts, x, y);
            return i >= 0 ? _ghosts[i] : (Ghost?)null;
        }

        /// <summary>手番プレイヤーが自分のときの合法手。</summary>
        public List<GeisterMove> GetLegalMoves() =>
            CurrentPlayer == Viewer ? GeisterBoard.GenerateMoves(_ghosts, Viewer) : new List<GeisterMove>();

        public int CapturedCount(int owner, GhostType type) =>
            GeisterBoard.Count(_ghosts, owner, type, GhostStatus.Captured);
    }
}

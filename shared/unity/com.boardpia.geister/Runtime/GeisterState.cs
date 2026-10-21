using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Geister
{
    public enum EndReason
    {
        None,
        /// <summary>勝者が相手の善玉を全て取った。</summary>
        CapturedAllGood,
        /// <summary>勝者の悪玉が全て取られた。</summary>
        LostAllEvil,
        /// <summary>勝者の善玉が脱出した。</summary>
        Escaped,
        /// <summary>敗者に合法手が無かった。</summary>
        NoMoves,
        /// <summary>手数上限による引き分け。</summary>
        TurnLimit,
    }

    /// <summary>完全情報の対局状態(不変)。CPU には渡さず GeisterPlayerView を経由する。</summary>
    public sealed class GeisterState
    {
        readonly Ghost[] _ghosts;

        public IReadOnlyList<Ghost> Ghosts => _ghosts;
        public int CurrentPlayer { get; }
        public int Ply { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }
        public GeisterMove? LastMove { get; }
        /// <summary>直前の手で取られた駒の Id(無ければ -1)。</summary>
        public int LastCapturedId { get; }

        internal GeisterState(Ghost[] ghosts, int currentPlayer, int ply, GameResult result, EndReason endReason,
            GeisterMove? lastMove, int lastCapturedId)
        {
            _ghosts = ghosts;
            CurrentPlayer = currentPlayer;
            Ply = ply;
            Result = result;
            EndReason = endReason;
            LastMove = lastMove;
            LastCapturedId = lastCapturedId;
        }

        internal Ghost[] CopyGhosts() => (Ghost[])_ghosts.Clone();

        public Ghost GetGhost(int id) => _ghosts[GeisterBoard.FindById(_ghosts, id)];

        /// <summary>(x, y) にいる駒。いなければ null。</summary>
        public Ghost? GhostAt(int x, int y)
        {
            int i = GeisterBoard.FindAt(_ghosts, x, y);
            return i >= 0 ? _ghosts[i] : (Ghost?)null;
        }

        /// <summary>owner の駒のうち相手に取られた type の数。</summary>
        public int CapturedCount(int owner, GhostType type) =>
            GeisterBoard.Count(_ghosts, owner, type, GhostStatus.Captured);
    }
}

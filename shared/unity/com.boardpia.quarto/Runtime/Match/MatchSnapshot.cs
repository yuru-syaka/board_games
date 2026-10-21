using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Quarto
{
    /// <summary>
    /// あるプレイヤーの画面に出す局面。クアルトには非公開情報が無いので局面そのものと視点を持つだけだが、
    /// View はこれだけを見て描画するので、CPU 戦でもオンライン戦でも同じ描画処理を使える。
    /// </summary>
    public sealed class MatchSnapshot
    {
        static readonly QuartoRules Rules = new QuartoRules();

        public QuartoState State { get; }
        public int Viewer { get; }

        public GameResult Result => State.Result;
        public EndReason EndReason => State.EndReason;
        public int CurrentPlayer => State.CurrentPlayer;
        public TurnPhase Phase => State.Phase;
        public int PieceInHand => State.PieceInHand;
        public int Ply => State.Ply;
        public QuartoMove? LastMove => State.LastMove;
        public IReadOnlyList<int> WinningLines => State.WinningLines;
        public bool IsNewGame => Ply == 0 && LastMove == null;
        public bool IsMyTurn => !Result.IsOver && CurrentPlayer == Viewer;

        public MatchSnapshot(QuartoState state, int viewer)
        {
            State = state;
            Viewer = viewer;
        }

        public static MatchSnapshot For(QuartoState state, int viewer) => new MatchSnapshot(state, viewer);

        /// <summary>自分の手番のときの合法手(相手の手番なら空)。</summary>
        public IReadOnlyList<QuartoMove> GetLegalMoves() =>
            IsMyTurn ? Rules.GetLegalMoves(State) : (IReadOnlyList<QuartoMove>)System.Array.Empty<QuartoMove>();

        public bool IsLegal(QuartoMove move) => IsMyTurn && Rules.IsLegal(State, move);
    }
}

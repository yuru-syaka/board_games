using BoardGames.Core;

namespace BoardGames.GhostReversa
{
    /// <summary>
    /// 画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面。
    /// 対局中は相手の非公開情報が伏せられ、決着後は全て公開される。
    /// View はこれだけを見て描画するので、CPU 戦でもオンライン戦でも同じ描画処理を使える。
    /// </summary>
    public sealed class MatchSnapshot
    {
        public ReversaPlayerView View { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }

        public int Viewer => View.Viewer;
        public int CurrentPlayer => View.CurrentPlayer;
        public int Ply => View.Ply;
        public ReversaMove? LastMove => View.LastMove;
        public bool IsNewGame => Ply == 0 && LastMove == null;

        public MatchSnapshot(ReversaPlayerView view, GameResult result, EndReason endReason)
        {
            View = view;
            Result = result;
            EndReason = endReason;
        }

        public static MatchSnapshot For(ReversaState state, int viewer)
        {
            var view = state.Result.IsOver
                ? ReversaPlayerView.CreateRevealed(state, viewer)
                : ReversaPlayerView.Create(state, viewer);
            return new MatchSnapshot(view, state.Result, state.EndReason);
        }
    }
}

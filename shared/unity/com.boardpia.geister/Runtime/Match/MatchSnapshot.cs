using BoardGames.Core;

namespace BoardGames.Geister
{
    /// <summary>
    /// 画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面。
    /// 対局中は相手の盤上の駒の種類が伏せられ、決着後は全て公開される。
    /// View はこれだけを見て描画するので、CPU 戦でもオンライン戦でも同じ描画処理を使える。
    /// </summary>
    public sealed class MatchSnapshot
    {
        public GeisterPlayerView View { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }
        /// <summary>直前の手で取られた駒の Id(無ければ -1)。</summary>
        public int LastCapturedId { get; }

        public int Viewer => View.Viewer;
        public int CurrentPlayer => View.CurrentPlayer;
        public int Ply => View.Ply;
        public GeisterMove? LastMove => View.LastMove;
        public bool IsNewGame => Ply == 0 && LastMove == null;

        public MatchSnapshot(GeisterPlayerView view, GameResult result, EndReason endReason, int lastCapturedId)
        {
            View = view;
            Result = result;
            EndReason = endReason;
            LastCapturedId = lastCapturedId;
        }

        public static MatchSnapshot For(GeisterState state, int viewer)
        {
            var view = state.Result.IsOver
                ? GeisterPlayerView.CreateRevealed(state, viewer)
                : GeisterPlayerView.Create(state, viewer);
            return new MatchSnapshot(view, state.Result, state.EndReason, state.LastCapturedId);
        }
    }
}

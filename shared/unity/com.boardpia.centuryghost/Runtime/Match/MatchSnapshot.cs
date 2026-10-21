using BoardGames.Core;

namespace BoardGames.CenturyGhost
{
    /// <summary>
    /// 画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面(山札の並びは含まない)。
    /// View はこれだけを見て描画するので、CPU 戦でもオンライン戦でも同じ描画処理を使える。
    /// </summary>
    public sealed class MatchSnapshot
    {
        public CenturyPlayerView View { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }

        public int Viewer => View.Viewer;
        public int CurrentPlayer => View.CurrentPlayer;
        public int Ply => View.Ply;
        public CenturyMove LastMove => View.LastMove;
        public bool IsNewGame => Ply == 0 && LastMove == null;

        public MatchSnapshot(CenturyPlayerView view, GameResult result, EndReason endReason)
        {
            View = view;
            Result = result;
            EndReason = endReason;
        }

        public static MatchSnapshot For(CenturyState state, int viewer) =>
            new MatchSnapshot(CenturyPlayerView.Create(state, viewer), state.Result, state.EndReason);
    }
}

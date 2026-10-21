using BoardGames.Core;

namespace BoardGames.BattleLine
{
    /// <summary>
    /// 画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面。
    /// 対局中は相手の手札と山札の順番が伏せられ、決着後は相手の手札も公開される。
    /// View はこれだけを見て描画するので、CPU 戦でもオンライン戦でも同じ描画処理を使える。
    /// </summary>
    public sealed class MatchSnapshot
    {
        public BattleLinePlayerView View { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }

        public int Viewer => View.Viewer;
        public int CurrentPlayer => View.CurrentPlayer;
        public TurnPhase Phase => View.Phase;
        public int Step => View.Step;
        public BattleLineMove? LastMove => View.LastMove;
        public bool IsNewGame => Step == 0;

        public MatchSnapshot(BattleLinePlayerView view, GameResult result, EndReason endReason)
        {
            View = view;
            Result = result;
            EndReason = endReason;
        }

        public static MatchSnapshot For(BattleLineState state, int viewer) =>
            new MatchSnapshot(BattleLinePlayerView.Create(state, viewer, state.Result.IsOver), state.Result, state.EndReason);
    }
}

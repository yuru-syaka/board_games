using BoardGames.Core;

namespace BoardGames.GhostTrend
{
    /// <summary>
    /// 画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面。
    /// View はこれだけを見て描画するので、CPU 戦でもオンライン戦でも同じ描画処理を使える。
    /// </summary>
    public sealed class MatchSnapshot
    {
        public TrendPlayerView View { get; }

        public int Viewer => View.Viewer;
        public int PlayerCount => View.PlayerCount;
        public int CurrentPlayer => View.CurrentPlayer;
        public int Ply => View.Ply;
        public GameResult Result => View.Result;
        /// <summary>対局の最初の局面(まだ誰も手を指していない)。</summary>
        public bool IsNewGame => View.Ply == 0;

        public MatchSnapshot(TrendPlayerView view)
        {
            View = view;
        }

        /// <summary>
        /// next が新しい対局の局面か。オンラインでは CPU が先に指してから届くことがあり Ply が 0 とは限らないので、
        /// 手数が戻った・終局後に終局していない局面が届いた、も新しい対局とみなす。
        /// </summary>
        public static bool StartsNewGame(MatchSnapshot previous, MatchSnapshot next) =>
            previous == null
                ? next.IsNewGame || (next.View.Events.Count > 0 && next.View.Events[0].Seq == 0)
                : next.Ply < previous.Ply || (previous.Result.IsOver && !next.Result.IsOver);

        /// <summary>この対局で最初に出品した人(まだ誰も出品していなければ今の手番の人)。</summary>
        public int FirstAuctioneer
        {
            get
            {
                foreach (var e in View.Events)
                    if (e.Kind == EventKind.Played) return e.Player;
                return CurrentPlayer;
            }
        }

        public static MatchSnapshot For(TrendState state, int viewer) => new MatchSnapshot(TrendPlayerView.Create(state, viewer));
    }
}

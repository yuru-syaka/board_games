using System;

namespace BoardGames.Geister
{
    /// <summary>
    /// 画面から見た 1 対局。View は Snapshot を描画し、自分の手を SubmitLocalMove で送るだけ。
    /// 相手の手(CPU・オンライン)は SnapshotChanged で届く。
    /// </summary>
    public interface IGeisterMatch
    {
        /// <summary>この画面で操作するプレイヤー(0 または 1)。</summary>
        int LocalPlayer { get; }

        /// <summary>最新の局面。対局開始前は null。</summary>
        MatchSnapshot Snapshot { get; }

        event Action<MatchSnapshot> SnapshotChanged;

        /// <summary>自分の手を指す。手番でない・非合法の手は無視される。</summary>
        void SubmitLocalMove(GeisterMove move);
    }
}

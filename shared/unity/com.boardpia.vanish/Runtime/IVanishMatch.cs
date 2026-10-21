using System;

namespace BoardGames.Vanish
{
    /// <summary>
    /// 画面から見た 1 対局。View は State を描画し、自分の手を SubmitMove で送るだけ。
    /// 相手の手(CPU・オンライン)は StateChanged で届く。
    /// </summary>
    public interface IVanishMatch
    {
        /// <summary>最新の局面。対局開始前は null。</summary>
        VanishState State { get; }

        event Action<VanishState> StateChanged;

        /// <summary>自分の手を指す。手番でない・非合法の手は無視して false を返す。</summary>
        bool SubmitMove(VanishMove move);
    }
}

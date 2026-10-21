namespace BoardGames.Core
{
    /// <summary>
    /// CPU の基盤。非公開情報のあるゲームでは TView にプレイヤー視点の状態だけを渡す。
    /// </summary>
    public interface ICpuPlayer<TView, TMove>
    {
        TMove ChooseMove(TView view);
    }
}

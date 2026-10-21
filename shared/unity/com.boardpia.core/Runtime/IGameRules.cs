using System.Collections.Generic;

namespace BoardGames.Core
{
    /// <summary>
    /// 全ゲーム共通のルールインターフェース。状態は不変として扱い、ApplyMove は新しい状態を返す。
    /// </summary>
    public interface IGameRules<TState, TMove>
    {
        IReadOnlyList<TMove> GetLegalMoves(TState state);
        TState ApplyMove(TState state, TMove move);
        GameResult GetResult(TState state);
    }
}

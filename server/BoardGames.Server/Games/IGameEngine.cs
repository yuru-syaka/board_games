using System.Diagnostics.CodeAnalysis;

namespace BoardGames.Server.Games;

/// <summary>
/// 1 つのゲームのルールを、サーバーから使える形にまとめたもの。
/// ルールそのものは shared/unity/ の Logic（Unity と同じコード）を呼ぶだけにする。
/// </summary>
public interface IGameEngine
{
    /// <summary>ゲームの ID（ロビーのメッセージで使う。英小文字）。</summary>
    string Id { get; }

    /// <summary>乱数の種から新しい対局を始める。同じ種なら必ず同じ初期局面になる。</summary>
    IGameSession Start(int seed);
}

/// <summary>対局中の局面。サーバーは「種 ＋ 手の一覧」から毎回作り直す。</summary>
public interface IGameSession
{
    bool IsOver { get; }

    /// <summary>
    /// seat の席からの手（NetProtocol の "M|…"）を検証して適用する。
    /// 手番違い・非合法・壊れたメッセージなら false。成功したら、保存用に整えた手を返す。
    /// </summary>
    bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove);

    /// <summary>seat の席に送る局面（NetProtocol の "S|…"。隠し情報は伏せる）。</summary>
    string SnapshotFor(int seat);
}

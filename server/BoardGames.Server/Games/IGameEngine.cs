using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;

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

    /// <summary>対局を始められる最少の人数。2 人用のゲームは 2。</summary>
    int MinSeats => 2;

    /// <summary>1 つの部屋に入れる最大の人数。2 人用のゲームは 2。3 以上なら「多人数の部屋」（GameServer.Table.cs）で動かす。</summary>
    int MaxSeats => 2;

    /// <summary>多人数の部屋だけ: seats 人で新しい対局を始める。</summary>
    ITableSession StartTable(int seed, int seats) => throw new NotSupportedException(Id + " is a two-seat game");
}

/// <summary>対局中の局面。サーバーは「種 ＋ 手の一覧」から毎回作り直す。</summary>
public interface IGameSession
{
    bool IsOver { get; }

    /// <summary>勝敗（終局していなければ Ongoing）。</summary>
    GameResult Result { get; }

    /// <summary>今の手番の席（0 か 1。多人数の部屋は 0〜4）。</summary>
    int CurrentPlayer { get; }

    /// <summary>
    /// seat の席からの手（NetProtocol の "M|…"）を検証して適用する。
    /// 手番違い・非合法・壊れたメッセージなら false。成功したら、保存用に整えた手を返す。
    /// </summary>
    bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove);

    /// <summary>seat の席に送る局面（NetProtocol の "S|…"。隠し情報は伏せる）。</summary>
    string SnapshotFor(int seat);
}

/// <summary>多人数の部屋の対局。全席が人間で始まり、抜けた席だけ CPU が代わる。</summary>
public interface ITableSession : IGameSession
{
    /// <summary>この対局の人数。</summary>
    int Seats { get; }

    /// <summary>その席を CPU が代わっているか。</summary>
    bool IsCpu(int seat);

    /// <summary>以後その席を CPU が代わる（その席の番なら、すぐに進める）。もう代わっていれば false。</summary>
    bool Takeover(int seat);

    /// <summary>時間切れの席の代わりに指す手（NetProtocol の "M|…"。適用はしない）。その席の番でなければ false。</summary>
    bool TryGetAutoMove(int seat, [NotNullWhen(true)] out string? moveMessage);

    /// <summary>席ごとの順位（0 = 1 位。同点は同じ順位。CPU に代わった席は人間より下）。</summary>
    IReadOnlyList<int> Ranks { get; }
}

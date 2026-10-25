using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;
using BoardGames.GhostTrend.Online;

namespace BoardGames.Server.Games;

/// <summary>
/// ゴースト・トレンド。人間 2 人（席 0 / 1）＋ 残りの席は CPU。人数ごとに別のゲーム ID（ghosttrendthree / four / five）にする
/// （ロビーのメッセージにはゲーム ID しか無いので、人数でマッチングを分けるため）。
/// CPU の手は TrendTable が人間の手の直後にまとめて進める（種と手数から決まるので、作り直しても同じ局面になる）。
/// 勝敗は人間 2 人の所持金を比べる。局面は席ごとに伏せて送る（相手の手札・所持金・伏せた入札額）。
/// </summary>
public sealed class GhostTrendEngine : IGameEngine
{
    readonly int _players;

    public GhostTrendEngine(int players) => _players = players;

    public string Id => IdFor(_players);

    /// <summary>人数ごとのゲーム ID（ID は英小文字だけなので、数は英語で書く）。Unity 側の AwsOnlineService と同じ。</summary>
    public static string IdFor(int players) => "ghosttrend" + players switch { 3 => "three", 4 => "four", _ => "five" };

    public IGameSession Start(int seed) => new Session(new TrendTable(_players, seed));

    sealed class Session : IGameSession
    {
        readonly TrendTable _table;

        public Session(TrendTable table) => _table = table;

        public bool IsOver => _table.IsOver;

        public GameResult Result => _table.SeatResult;

        public int CurrentPlayer => _table.CurrentSeat;

        public bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove)
        {
            normalizedMove = null;
            if (!TrendNetProtocol.TryDecodeMove(moveMessage, out var move) || !_table.TryApply(seat, move)) return false;
            normalizedMove = TrendNetProtocol.EncodeMove(move);
            return true;
        }

        public string SnapshotFor(int seat) => _table.SnapshotFor(seat);
    }
}

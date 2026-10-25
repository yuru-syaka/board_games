using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;
using BoardGames.GhostTrend;
using BoardGames.GhostTrend.Online;

namespace BoardGames.Server.Games;

/// <summary>
/// ゴースト・トレンド。全席が人間（3〜5 人）の多人数の部屋で遊ぶ（GameServer.Table.cs）。
/// 途中で抜けた・戻らなかった・時間切れが続いた席だけ、CPU が代わる（TrendTable）。
/// 局面は席ごとに伏せて送る（相手の手札・所持金・伏せた入札額）。勝敗は最後の所持金の順位。
/// </summary>
public sealed class GhostTrendEngine : IGameEngine
{
    public const string GameId = "ghosttrend";

    public string Id => GameId;

    public int MinSeats => TrendLogic.MinPlayers;

    public int MaxSeats => TrendLogic.MaxPlayers;

    /// <summary>人数の指定がないとき（テストなど）は 4 人。</summary>
    public IGameSession Start(int seed) => StartTable(seed, 4);

    public ITableSession StartTable(int seed, int seats) => new Session(new TrendTable(seats, seed));

    sealed class Session : ITableSession
    {
        readonly TrendTable _table;

        public Session(TrendTable table) => _table = table;

        public bool IsOver => _table.IsOver;

        public GameResult Result => _table.SeatResult;

        public int CurrentPlayer => _table.CurrentSeat;

        public int Seats => _table.PlayerCount;

        public IReadOnlyList<int> Ranks => _table.Ranks;

        public bool IsCpu(int seat) => _table.IsCpu(seat);

        public bool Takeover(int seat) => _table.Takeover(seat);

        public bool TryGetAutoMove(int seat, [NotNullWhen(true)] out string? moveMessage)
        {
            moveMessage = null;
            if (!_table.TryGetAutoMove(seat, out var move)) return false;
            moveMessage = TrendNetProtocol.EncodeMove(move);
            return true;
        }

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

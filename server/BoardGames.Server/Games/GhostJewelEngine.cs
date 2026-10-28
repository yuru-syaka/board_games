using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;
using BoardGames.GhostJewel;
using BoardGames.GhostJewel.Online;

namespace BoardGames.Server.Games;

/// <summary>
/// ゴースト・ジュエル。全席が人間（2〜4 人）の多人数の部屋で遊ぶ（GameServer.Table.cs）。
/// 途中で抜けた・戻らなかった・時間切れが続いた席だけ、CPU が代わる（JewelTable）。
/// 局面は席ごとに伏せて送る（相手が山札から予約したカード・山札の並び）。勝敗は点の順位（同点ならカードが少ない人）。
/// </summary>
public sealed class GhostJewelEngine : IGameEngine
{
    public const string GameId = "ghostjewel";

    public string Id => GameId;

    public int MinSeats => JewelLogic.MinPlayers;

    public int MaxSeats => JewelLogic.MaxPlayers;

    /// <summary>人数の指定がないとき（テストなど）は 2 人。</summary>
    public IGameSession Start(int seed) => StartTable(seed, 2);

    public ITableSession StartTable(int seed, int seats) => new Session(new JewelTable(seats, seed));

    sealed class Session : ITableSession
    {
        readonly JewelTable _table;

        public Session(JewelTable table) => _table = table;

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
            moveMessage = JewelNetProtocol.EncodeMove(move);
            return true;
        }

        public bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove)
        {
            normalizedMove = null;
            if (!JewelNetProtocol.TryDecodeMove(moveMessage, out var move) || !_table.TryApply(seat, move)) return false;
            normalizedMove = JewelNetProtocol.EncodeMove(move);
            return true;
        }

        public string SnapshotFor(int seat) => _table.SnapshotFor(seat);
    }
}

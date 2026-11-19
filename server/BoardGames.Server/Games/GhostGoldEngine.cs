using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;
using BoardGames.GhostGold;
using BoardGames.GhostGold.Online;

namespace BoardGames.Server.Games;

/// <summary>
/// ゴースト・ゴールド。全席が人間（2〜5 人）の多人数の部屋で遊ぶ（GameServer.Table.cs）。
/// 途中で抜けた・戻らなかった・時間切れが続いた席だけ、CPU が代わる（GoldTable）。
/// 局面は席ごとに伏せて送る（山札の順番と、ほかの人のまだ公開していない「進む・帰る」）。勝敗は最後の点数の順位。
/// </summary>
public sealed class GhostGoldEngine : IGameEngine
{
    public const string GameId = "ghostgold";

    public string Id => GameId;

    public int MinSeats => GoldLogic.MinPlayers;

    public int MaxSeats => GoldLogic.MaxPlayers;

    /// <summary>人数の指定がないとき（テストなど）は 2 人。</summary>
    public IGameSession Start(int seed) => StartTable(seed, 2);

    public ITableSession StartTable(int seed, int seats) => new Session(new GoldTable(seats, seed));

    sealed class Session : ITableSession
    {
        readonly GoldTable _table;

        public Session(GoldTable table) => _table = table;

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
            moveMessage = GoldNetProtocol.EncodeMove(move);
            return true;
        }

        public bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove)
        {
            normalizedMove = null;
            if (!GoldNetProtocol.TryDecodeMove(moveMessage, out var move) || !_table.TryApply(seat, move)) return false;
            normalizedMove = GoldNetProtocol.EncodeMove(move);
            return true;
        }

        public string SnapshotFor(int seat) => _table.SnapshotFor(seat);
    }
}

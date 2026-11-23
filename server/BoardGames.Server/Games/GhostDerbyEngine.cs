using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;
using BoardGames.GhostDerby;
using BoardGames.GhostDerby.Online;

namespace BoardGames.Server.Games;

/// <summary>
/// ゴースト・ダービー。全席が人間（2〜5 人）の多人数の部屋で遊ぶ（GameServer.Table.cs）。
/// 途中で抜けた・戻らなかった・時間切れが続いた席だけ、CPU が代わる（DerbyTable）。
/// 局面は席ごとに伏せて送る（山札、ほかの人の手札、公開前の賭け）。勝敗は最後のコインの順位。
/// </summary>
public sealed class GhostDerbyEngine : IGameEngine
{
    public const string GameId = "ghostderby";

    public string Id => GameId;

    public int MinSeats => DerbyLogic.MinPlayers;

    public int MaxSeats => DerbyLogic.MaxPlayers;

    /// <summary>人数の指定がないとき（テストなど）は 2 人。</summary>
    public IGameSession Start(int seed) => StartTable(seed, 2);

    public ITableSession StartTable(int seed, int seats) => new Session(new DerbyTable(seats, seed));

    sealed class Session : ITableSession
    {
        readonly DerbyTable _table;

        public Session(DerbyTable table) => _table = table;

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
            moveMessage = DerbyNetProtocol.EncodeMove(move);
            return true;
        }

        public bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove)
        {
            normalizedMove = null;
            if (!DerbyNetProtocol.TryDecodeMove(moveMessage, out var move) || !_table.TryApply(seat, move)) return false;
            normalizedMove = DerbyNetProtocol.EncodeMove(move);
            return true;
        }

        public string SnapshotFor(int seat) => _table.SnapshotFor(seat);
    }
}

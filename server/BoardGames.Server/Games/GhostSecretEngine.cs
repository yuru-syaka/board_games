using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;
using BoardGames.GhostSecret;
using BoardGames.GhostSecret.Online;

namespace BoardGames.Server.Games;

/// <summary>
/// ゴースト・シークレット。全席が人間（2〜4 人）の多人数の部屋で遊ぶ（GameServer.Table.cs）。
/// 途中で抜けた・戻らなかった・時間切れが続いた席だけ、CPU が代わる（SecretTable）。
/// 局面は席ごとに伏せて送る（ほかの人の手札・山札・脇に置いたカード・ほかの人だけが見た出来事）。勝敗は秘密のかけらの順位。
/// </summary>
public sealed class GhostSecretEngine : IGameEngine
{
    public const string GameId = "ghostsecret";

    public string Id => GameId;

    public int MinSeats => SecretLogic.MinPlayers;

    public int MaxSeats => SecretLogic.MaxPlayers;

    /// <summary>人数の指定がないとき（テストなど）は 2 人。</summary>
    public IGameSession Start(int seed) => StartTable(seed, 2);

    public ITableSession StartTable(int seed, int seats) => new Session(new SecretTable(seats, seed));

    sealed class Session : ITableSession
    {
        readonly SecretTable _table;

        public Session(SecretTable table) => _table = table;

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
            moveMessage = SecretNetProtocol.EncodeMove(move);
            return true;
        }

        public bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove)
        {
            normalizedMove = null;
            if (!SecretNetProtocol.TryDecodeMove(moveMessage, out var move) || !_table.TryApply(seat, move)) return false;
            normalizedMove = SecretNetProtocol.EncodeMove(move);
            return true;
        }

        public string SnapshotFor(int seat) => _table.SnapshotFor(seat);
    }
}

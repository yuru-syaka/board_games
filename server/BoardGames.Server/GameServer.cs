using BoardGames.Net;
using BoardGames.Server.Games;

namespace BoardGames.Server;

/// <summary>
/// オンライン対戦サーバーの中身。AWS には依存しない（保存先と送信先は差し替えられる）。
/// - ロビー: ランダムマッチ（待ち行列）とルームコード。最初にいた人が席 0、あとから来た人が席 1。
/// - 対局: サーバーが局面を持つ（サーバー権威型）。手は各ゲームのルールで検証し、両方の席に局面を送る。
///   ブラウザ側は両者とも OnlineMatch の「ゲスト」として動く。
/// - メッセージ: ロビーは LobbyProtocol（"@…"）、対局中は各ゲームの NetProtocol（"S|1|…" など）。
/// </summary>
public sealed class GameServer
{
    const int MaxRetries = 5;
    const string RematchMessage = "R|1";

    readonly IMatchStore _store;
    readonly IConnectionPush _push;
    readonly GameRegistry _games;
    readonly Random _rng;

    public GameServer(IMatchStore store, IConnectionPush push, GameRegistry games, Random? rng = null)
    {
        _store = store;
        _push = push;
        _games = games;
        _rng = rng ?? Random.Shared;
    }

    /// <summary>接続が切れた（ブラウザを閉じた・通信が切れた）。対局中なら相手に知らせて部屋を閉じる。</summary>
    public Task OnDisconnectAsync(string connectionId) => LeaveAsync(connectionId);

    /// <summary>ブラウザからのメッセージ 1 つを処理する。</summary>
    public async Task OnMessageAsync(string connectionId, string? message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > 4096) return;

        if (LobbyProtocol.IsLobbyMessage(message))
        {
            if (!LobbyProtocol.TryParse(message, out var lobby))
            {
                await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.BadRequest));
                return;
            }
            switch (lobby.Kind)
            {
                case LobbyMessageKind.Quick: await QuickAsync(connectionId, lobby.Game); break;
                case LobbyMessageKind.Create: await CreateAsync(connectionId, lobby.Game); break;
                case LobbyMessageKind.Join: await JoinAsync(connectionId, lobby.Game, lobby.Code); break;
                case LobbyMessageKind.Leave: await LeaveAsync(connectionId); break;
                case LobbyMessageKind.Ping: await _push.SendAsync(connectionId, LobbyProtocol.Pong()); break;
                default: await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.BadRequest)); break;
            }
            return;
        }

        if (message == RematchMessage) await RematchAsync(connectionId);
        else await MoveAsync(connectionId, message);
    }

    // ---------- ロビー ----------

    async Task QuickAsync(string connectionId, string game)
    {
        if (!await PrepareToJoinAsync(connectionId, game)) return;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            // 待っている人がいれば、その部屋の席 1 に入る
            var waitingId = await _store.TryDequeueAsync(game);
            if (waitingId != null)
            {
                var waiting = await _store.GetMatchAsync(waitingId);
                if (waiting != null && await TrySeatSecondAsync(waiting, connectionId)) return;
                continue; // 待っていた人が抜けていた・ほかの人が先に入った: もう一度探す
            }

            // 誰もいなければ、自分の部屋を作って待ち行列に入る
            var match = NewMatch(game, connectionId, code: null);
            if (!await _store.TryPutMatchAsync(match)) continue;
            if (await _store.TryEnqueueAsync(game, match.Id))
            {
                await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, 0));
                await _push.SendAsync(connectionId, LobbyProtocol.Joined(0, null, opponentPresent: false));
                return;
            }
            await _store.DeleteMatchAsync(match.Id); // ほぼ同時にほかの人が並んだ: その人と組めるか探し直す
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    async Task CreateAsync(string connectionId, string game)
    {
        if (!await PrepareToJoinAsync(connectionId, game)) return;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            string code = _rng.Next(10000).ToString("D4");
            var match = NewMatch(game, connectionId, code);
            if (!await _store.TryReserveCodeAsync(game, code, match.Id)) continue; // 使用中のコード: 引き直す
            if (!await _store.TryPutMatchAsync(match))
            {
                await _store.ReleaseCodeAsync(game, code, match.Id);
                continue;
            }
            await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, 0));
            await _push.SendAsync(connectionId, LobbyProtocol.Joined(0, code, opponentPresent: false));
            return;
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    async Task JoinAsync(string connectionId, string game, string code)
    {
        if (!await PrepareToJoinAsync(connectionId, game)) return;

        var matchId = await _store.FindCodeAsync(game, code);
        var match = matchId == null ? null : await _store.GetMatchAsync(matchId);
        if (match == null || match.Connections[0] == null)
        {
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.RoomNotFound));
            return;
        }
        if (!await TrySeatSecondAsync(match, connectionId))
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.RoomFull));
    }

    /// <summary>ゲームがあるかを確かめ、すでにほかの部屋にいれば抜けておく。</summary>
    async Task<bool> PrepareToJoinAsync(string connectionId, string game)
    {
        if (_games.Find(game) == null)
        {
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.UnknownGame));
            return false;
        }
        await LeaveAsync(connectionId);
        return true;
    }

    /// <summary>部屋の席 1 に入り、対局を始める。席が埋まっていた・ほかの書き込みに負けたら false。</summary>
    async Task<bool> TrySeatSecondAsync(MatchRecord match, string connectionId)
    {
        if (match.Connections[1] != null || match.Started) return false;
        match.Connections[1] = connectionId;
        StartNewGame(match);
        if (!await _store.TryPutMatchAsync(match)) return false;

        if (match.Code != null) await _store.ReleaseCodeAsync(match.Game, match.Code, match.Id); // もう入れないのでコードを返す
        await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, 1));
        await _push.SendAsync(connectionId, LobbyProtocol.Joined(1, match.Code, opponentPresent: true));
        await _push.SendAsync(match.Connections[0]!, LobbyProtocol.OpponentJoined());
        await SendSnapshotsAsync(match, Rebuild(match));
        return true;
    }

    async Task LeaveAsync(string connectionId)
    {
        var connection = await _store.GetConnectionAsync(connectionId);
        if (connection == null) return;
        await _store.DeleteConnectionAsync(connectionId);

        var match = await _store.GetMatchAsync(connection.MatchId);
        if (match == null) return;
        await _store.DeleteMatchAsync(match.Id);
        await _store.RemoveFromQueueAsync(match.Game, match.Id);
        if (match.Code != null) await _store.ReleaseCodeAsync(match.Game, match.Code, match.Id);

        // 相手には「相手が抜けた」と知らせ、相手の席の記録も消す（相手はロビーに戻る）
        var other = match.Connections[match.OtherSeat(connection.Seat)];
        if (other != null)
        {
            await _store.DeleteConnectionAsync(other);
            await _push.SendAsync(other, LobbyProtocol.OpponentLeft());
        }
    }

    // ---------- 対局 ----------

    async Task MoveAsync(string connectionId, string message)
    {
        var connection = await _store.GetConnectionAsync(connectionId);
        if (connection == null) return; // 対局に入っていない

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(connection.MatchId);
            if (match == null || !match.Started) return;

            var session = Rebuild(match);
            if (!session.TryApply(connection.Seat, message, out var normalized))
            {
                // 手番違い・非合法: 今の局面を送り直して同期を取り直す（ホスト権威型のときと同じ）
                await _push.SendAsync(connectionId, session.SnapshotFor(connection.Seat));
                return;
            }
            match.Moves.Add(connection.Seat + ":" + normalized);
            if (await _store.TryPutMatchAsync(match))
            {
                await SendSnapshotsAsync(match, session);
                return;
            }
            // ほかの書き込みが先に入った: 読み直してやり直す
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    async Task RematchAsync(string connectionId)
    {
        var connection = await _store.GetConnectionAsync(connectionId);
        if (connection == null) return;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(connection.MatchId);
            if (match == null || !match.Started || match.Rematch[connection.Seat]) return;
            if (!Rebuild(match).IsOver) return; // 対局中は再戦できない

            match.Rematch[connection.Seat] = true;
            bool both = match.Rematch[0] && match.Rematch[1];
            if (both) StartNewGame(match);
            if (!await _store.TryPutMatchAsync(match)) continue;

            if (both)
            {
                await SendSnapshotsAsync(match, Rebuild(match));
            }
            else
            {
                var other = match.Connections[match.OtherSeat(connection.Seat)];
                if (other != null) await _push.SendAsync(other, RematchMessage);
            }
            return;
        }
    }

    // ---------- 内部 ----------

    MatchRecord NewMatch(string game, string connectionId, string? code)
    {
        var match = new MatchRecord { Id = Guid.NewGuid().ToString("N"), Game = game, Code = code };
        match.Connections[0] = connectionId;
        return match;
    }

    /// <summary>新しい対局にする（種を引き直し、手と再戦の希望を消す）。</summary>
    void StartNewGame(MatchRecord match)
    {
        match.Started = true;
        match.Seed = _rng.Next();
        match.Moves.Clear();
        match.Rematch[0] = match.Rematch[1] = false;
    }

    /// <summary>「種 ＋ 手の一覧」から局面を作り直す。</summary>
    IGameSession Rebuild(MatchRecord match)
    {
        var engine = _games.Find(match.Game) ?? throw new InvalidOperationException("unknown game " + match.Game);
        var session = engine.Start(match.Seed);
        foreach (var record in match.Moves)
        {
            int seat = record[0] - '0';
            if (record.Length < 3 || record[1] != ':' || !session.TryApply(seat, record.Substring(2), out _))
                throw new InvalidOperationException($"broken move record in match {match.Id}: {record}");
        }
        return session;
    }

    async Task SendSnapshotsAsync(MatchRecord match, IGameSession session)
    {
        for (int seat = 0; seat < 2; seat++)
        {
            var connectionId = match.Connections[seat];
            if (connectionId != null) await _push.SendAsync(connectionId, session.SnapshotFor(seat));
        }
    }
}

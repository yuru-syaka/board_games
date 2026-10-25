using BoardGames.Net;

namespace BoardGames.Server.Tests;

/// <summary>DynamoDB の代わり。読み書きのたびに複製して、本物と同じく「保存した値」だけが残るようにする。</summary>
sealed class InMemoryStore : IMatchStore
{
    readonly Dictionary<string, MatchRecord> _matches = new();
    readonly Dictionary<string, ConnectionRecord> _connections = new();
    readonly Dictionary<string, string> _queue = new();
    readonly Dictionary<string, string> _codes = new();
    readonly Dictionary<string, (string, int)> _tokens = new();

    /// <summary>残された対局結果（書かれた順）。</summary>
    public List<MatchResult> Results { get; } = new();

    /// <summary>次の TryPutMatchAsync をこの回数だけ「ほかの書き込みに負けた」ことにする（競合のテスト用）。</summary>
    public int FailNextMatchWrites { get; set; }

    public int MatchCount => _matches.Count;
    public int ConnectionCount => _connections.Count;
    public bool HasQueue(string game) => _queue.ContainsKey(game);
    public bool HasCode(string game, string code) => _codes.ContainsKey(game + "#" + code);
    public int TokenCount => _tokens.Count;
    public MatchRecord? PeekMatch() => _matches.Values.SingleOrDefault();

    public Task<MatchRecord?> GetMatchAsync(string matchId) =>
        Task.FromResult(_matches.TryGetValue(matchId, out var m) ? Clone(m) : null);

    public Task<bool> TryPutMatchAsync(MatchRecord match)
    {
        if (FailNextMatchWrites > 0)
        {
            FailNextMatchWrites--;
            return Task.FromResult(false);
        }
        _matches.TryGetValue(match.Id, out var current);
        bool ok = match.Version == 0 ? current == null : current != null && current.Version == match.Version;
        if (!ok) return Task.FromResult(false);
        match.Version++;
        _matches[match.Id] = Clone(match);
        return Task.FromResult(true);
    }

    public Task DeleteMatchAsync(string matchId)
    {
        _matches.Remove(matchId);
        return Task.CompletedTask;
    }

    public Task<ConnectionRecord?> GetConnectionAsync(string connectionId) =>
        Task.FromResult(_connections.GetValueOrDefault(connectionId));

    public Task PutConnectionAsync(ConnectionRecord connection)
    {
        _connections[connection.ConnectionId] = connection;
        return Task.CompletedTask;
    }

    public Task DeleteConnectionAsync(string connectionId)
    {
        _connections.Remove(connectionId);
        return Task.CompletedTask;
    }

    public Task<bool> TryEnqueueAsync(string game, string matchId) => Task.FromResult(_queue.TryAdd(game, matchId));

    public Task<string?> TryDequeueAsync(string game) =>
        Task.FromResult(_queue.Remove(game, out var id) ? id : null);

    public Task RemoveFromQueueAsync(string game, string matchId)
    {
        if (_queue.TryGetValue(game, out var id) && id == matchId) _queue.Remove(game);
        return Task.CompletedTask;
    }

    public Task<bool> TryReserveCodeAsync(string game, string code, string matchId) =>
        Task.FromResult(_codes.TryAdd(game + "#" + code, matchId));

    public Task<string?> FindCodeAsync(string game, string code) =>
        Task.FromResult(_codes.GetValueOrDefault(game + "#" + code));

    public Task ReleaseCodeAsync(string game, string code, string matchId)
    {
        if (_codes.TryGetValue(game + "#" + code, out var id) && id == matchId) _codes.Remove(game + "#" + code);
        return Task.CompletedTask;
    }

    public Task PutTokenAsync(string token, string matchId, int seat)
    {
        _tokens[token] = (matchId, seat);
        return Task.CompletedTask;
    }

    public Task<(string MatchId, int Seat)?> FindTokenAsync(string token) =>
        Task.FromResult<(string MatchId, int Seat)?>(_tokens.TryGetValue(token, out var t) ? t : null);

    public Task DeleteTokenAsync(string token)
    {
        _tokens.Remove(token);
        return Task.CompletedTask;
    }

    // ---------- 戦績とログイン（ステップ 5-2） ----------

    readonly Dictionary<string, string> _auth = new();
    readonly Dictionary<(string User, string Game), PlayerStats> _stats = new();
    readonly Dictionary<string, string> _names = new();

    /// <summary>次の TrySaveResultAsync をこの回数だけ「戦績が競合した」ことにする（やり直しのテスト用）。</summary>
    public int FailNextResultWrites { get; set; }

    public PlayerStats? Stats(string user, string game) => _stats.GetValueOrDefault((user, game));

    public void SetName(string user, string name) => _names[user] = name;

    public Task<SaveOutcome> TrySaveResultAsync(MatchResult result, IReadOnlyList<PlayerStats> stats)
    {
        if (Results.Any(r => r.Id == result.Id)) return Task.FromResult(SaveOutcome.AlreadySaved);
        if (FailNextResultWrites > 0)
        {
            FailNextResultWrites--;
            return Task.FromResult(SaveOutcome.Conflict);
        }
        foreach (var s in stats)
        {
            long current = _stats.TryGetValue((s.UserId, s.Game), out var existing) ? existing.Version : 0;
            if (current != s.Version) return Task.FromResult(SaveOutcome.Conflict);
        }
        Results.Add(result);
        foreach (var s in stats) _stats[(s.UserId, s.Game)] = s with { Version = s.Version + 1 };
        return Task.FromResult(SaveOutcome.Saved);
    }

    public Task PutAuthAsync(string connectionId, string userId, TimeSpan ttl)
    {
        _auth[connectionId] = userId;
        return Task.CompletedTask;
    }

    public Task<string?> GetAuthAsync(string connectionId) => Task.FromResult(_auth.GetValueOrDefault(connectionId));

    public Task<PlayerProfile> GetPlayerAsync(string userId) =>
        Task.FromResult(new PlayerProfile(userId, _names.GetValueOrDefault(userId),
            _stats.Where(kv => kv.Key.User == userId).ToDictionary(kv => kv.Key.Game, kv => kv.Value)));

    static MatchRecord Clone(MatchRecord m)
    {
        var copy = new MatchRecord
        {
            Id = m.Id, Game = m.Game, Code = m.Code, Started = m.Started, Seed = m.Seed, Version = m.Version,
            TurnStartedAt = m.TurnStartedAt, GameNo = m.GameNo, HostSeat = m.HostSeat, PlayerCount = m.PlayerCount,
            Connections = (string?[])m.Connections.Clone(), Rematch = (bool[])m.Rematch.Clone(), Tokens = (string?[])m.Tokens.Clone(),
            AwaySince = (long?[])m.AwaySince.Clone(), Users = (string?[])m.Users.Clone(), TimeoutStrikes = (int[])m.TimeoutStrikes.Clone(),
        };
        copy.Moves.AddRange(m.Moves);
        return copy;
    }
}

/// <summary>ID トークンの確認の代わり。"aaaa.bbbb.ユーザー名" の形のトークンを、そのユーザーとして受け付ける（"bad" で始まる部分は断る）。</summary>
sealed class FakeVerifier : Auth.ITokenVerifier
{
    public static string TokenFor(string user) => "testheader.testpayload." + user;

    public Task<string?> VerifyAsync(string idToken)
    {
        var user = idToken.Split('.')[^1];
        return Task.FromResult<string?>(user.StartsWith("bad", StringComparison.Ordinal) ? null : user);
    }
}

/// <summary>API Gateway の代わり。送られたメッセージを接続ごとに溜め、Pump でブラウザ役に届ける。</summary>
sealed class FakePush : IConnectionPush
{
    readonly Queue<(string connectionId, string message)> _outbox = new();
    readonly Dictionary<string, FakeClient> _clients = new();

    public List<(string connectionId, string message)> Log { get; } = new();

    public void Register(FakeClient client) => _clients[client.ConnectionId] = client;

    public void Disconnect(string connectionId) => _clients.Remove(connectionId);

    public Task<bool> SendAsync(string connectionId, string message)
    {
        if (!_clients.ContainsKey(connectionId)) return Task.FromResult(false);
        Log.Add((connectionId, message));
        _outbox.Enqueue((connectionId, message));
        return Task.FromResult(true);
    }

    /// <summary>溜まったメッセージを、なくなるまでブラウザ役に届ける。</summary>
    public void Pump()
    {
        while (_outbox.Count > 0)
        {
            var (id, message) = _outbox.Dequeue();
            if (_clients.TryGetValue(id, out var client)) client.Deliver(message);
        }
    }
}

/// <summary>
/// ブラウザ役。Unity の AwsOnlineService と同じく、"@…" はロビー、それ以外は OnlineMatch に渡す。
/// 送信はそのままサーバーに届け、返事は FakePush.Pump で受け取る。
/// </summary>
sealed class FakeClient :
    Vanish.Online.IMatchTransport, Gobblet.Online.IMatchTransport, Quarto.Online.IMatchTransport,
    Geister.Online.IMatchTransport, BattleLine.Online.IMatchTransport, CenturyGhost.Online.IMatchTransport,
    GhostReversa.Online.IMatchTransport, GhostTrend.Online.IMatchTransport
{
    readonly GameServer _server;
    readonly FakePush _push;

    public string ConnectionId { get; }
    public List<LobbyMessage> Lobby { get; } = new();
    public List<string> Received { get; } = new();
    public event Action<string>? MessageReceived;

    public FakeClient(GameServer server, FakePush push, string connectionId)
    {
        _server = server;
        _push = push;
        ConnectionId = connectionId;
        push.Register(this);
    }

    public LobbyMessage? LastLobby => Lobby.Count > 0 ? Lobby[^1] : null;

    public void Send(string message)
    {
        _server.OnMessageAsync(ConnectionId, message).GetAwaiter().GetResult();
        _push.Pump();
    }

    public void Disconnect()
    {
        _push.Disconnect(ConnectionId);
        _server.OnDisconnectAsync(ConnectionId).GetAwaiter().GetResult();
        _push.Pump();
    }

    public void Deliver(string message)
    {
        Received.Add(message);
        if (LobbyProtocol.TryParse(message, out var lobby)) Lobby.Add(lobby);
        else MessageReceived?.Invoke(message);
    }
}

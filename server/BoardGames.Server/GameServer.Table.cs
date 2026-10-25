using BoardGames.Net;
using BoardGames.Server.Games;

namespace BoardGames.Server;

/// <summary>
/// 多人数の部屋（ゴースト・トレンド。IGameEngine.MaxSeats が 3 以上のゲーム）。全席が人間で遊ぶ。
/// - 部屋: 最初に入った人がホスト。ランダムマッチは「始まっていない部屋」に空きがあれば入る。ルームコードでも入れる。
///   ホストは最少人数（MinSeats）がそろったら @start で始められ、最大人数（MaxSeats）がそろうと自動で始まる。
///   始めるときに、座っている人の席を 0 から詰め直し、各人に @joined を送り直す。始まる前の様子は @table で知らせる。
/// - 途中で抜けた・通信が切れて待ち時間を過ぎた席は、CPU が代わる（手の一覧に "席:@cpu" と記録。作り直しても同じ局面）。
/// - 1 手の制限時間を過ぎたら、待っている人の @claim で、その人の手を自動で進める。続けて 3 回なら CPU が代わる。
/// - 終局したら順位で結果を残す（多人数の Elo。ランダムマッチで、ログインした別々のアカウントが 2 人以上いればレートが動く）。
/// - 再戦: つながっている人が全員 R|1 を送り、最少人数以上なら、その顔ぶれで新しい対局。
/// 2 人用の部屋の処理（GameServer.cs）とは、対局 ID の頭の "t" で見分ける。
/// </summary>
public sealed partial class GameServer
{
    /// <summary>手の一覧の「この時点からこの席は CPU」の印。</summary>
    const string CpuRecord = "@cpu";

    /// <summary>時間切れの自動の手が、この回数続いたら CPU に代わる。</summary>
    const int TimeoutStrikesToTakeover = 3;

    sealed record TableRatingChange(int[] After, int[] Deltas);

    enum SeatOutcome { Seated, Full, Lost }

    static bool IsTableId(string matchId) => matchId.StartsWith(MatchRecord.TablePrefix, StringComparison.Ordinal);

    bool IsTableGame(string game) => _games.Find(game) is { MaxSeats: > 2 };

    IGameEngine EngineOf(MatchRecord match) =>
        _games.Find(match.Game) ?? throw new InvalidOperationException("unknown game " + match.Game);

    static int SeatedCount(MatchRecord match) => match.Connections.Count(c => c != null);

    // ---------- 部屋に入る ----------

    async Task TableQuickAsync(string connectionId, string game)
    {
        var (ok, user) = await PrepareToJoinAsync(connectionId, game);
        if (!ok) return;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            // 待ち行列にある部屋（空きのある、始まっていない部屋）に入る
            var waitingId = await _store.TryDequeueAsync(game);
            if (waitingId != null)
            {
                var waiting = await _store.GetMatchAsync(waitingId);
                if (waiting == null || waiting.Started) continue;
                var outcome = await TrySeatAtTableAsync(waiting, connectionId, user);
                if (outcome == SeatOutcome.Seated) return;
                if (outcome == SeatOutcome.Lost) await _store.TryEnqueueAsync(game, waitingId); // ほかの書き込みに負けた: 戻して探し直す
                continue;
            }

            // 無ければ、自分の部屋を作って待ち行列に入る
            var match = NewTable(game, connectionId, code: null, user);
            if (!await _store.TryPutMatchAsync(match)) continue;
            if (await _store.TryEnqueueAsync(game, match.Id))
            {
                await SeatTableHostAsync(match, connectionId);
                return;
            }
            await _store.DeleteMatchAsync(match.Id); // ほぼ同時にほかの人が並んだ: その部屋に入れるか探し直す
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    async Task TableCreateAsync(string connectionId, string game)
    {
        var (ok, user) = await PrepareToJoinAsync(connectionId, game);
        if (!ok) return;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            string code = _rng.Next(10000).ToString("D4");
            var match = NewTable(game, connectionId, code, user);
            if (!await _store.TryReserveCodeAsync(game, code, match.Id)) continue; // 使用中のコード: 引き直す
            if (!await _store.TryPutMatchAsync(match))
            {
                await _store.ReleaseCodeAsync(game, code, match.Id);
                continue;
            }
            await SeatTableHostAsync(match, connectionId);
            return;
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    async Task TableJoinAsync(string connectionId, string game, string code)
    {
        var (ok, user) = await PrepareToJoinAsync(connectionId, game);
        if (!ok) return;

        var matchId = await _store.FindCodeAsync(game, code);
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = matchId == null ? null : await _store.GetMatchAsync(matchId);
            if (match == null || SeatedCount(match) == 0)
            {
                await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.RoomNotFound));
                return;
            }
            if (match.Started)
            {
                await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.RoomFull));
                return;
            }
            switch (await TrySeatAtTableAsync(match, connectionId, user))
            {
                case SeatOutcome.Seated: return;
                case SeatOutcome.Full:
                    await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.RoomFull));
                    return;
            }
            // ほかの書き込みに負けた: 読み直してやり直す
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    MatchRecord NewTable(string game, string connectionId, string? code, string? user)
    {
        var engine = _games.Find(game) ?? throw new InvalidOperationException("unknown game " + game);
        var match = MatchRecord.NewTable(MatchRecord.TablePrefix + Guid.NewGuid().ToString("N"), game, code, engine.MaxSeats);
        match.Connections[0] = connectionId;
        match.Users[0] = user;
        match.Tokens[0] = NewToken();
        match.HostSeat = 0;
        return match;
    }

    /// <summary>作った部屋の席 0 に、ホストとして座る（保存済みの部屋に対して）。</summary>
    async Task SeatTableHostAsync(MatchRecord match, string connectionId)
    {
        await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, 0));
        await _store.PutTokenAsync(match.Tokens[0]!, match.Id, 0);
        await _push.SendAsync(connectionId, LobbyProtocol.Joined(0, match.Code, opponentPresent: false));
        await _push.SendAsync(connectionId, LobbyProtocol.Token(match.Tokens[0]!));
        await SendTableLobbyAsync(match);
    }

    /// <summary>始まっていない部屋の空いている席に座る。満員になったら対局を始める。</summary>
    async Task<SeatOutcome> TrySeatAtTableAsync(MatchRecord match, string connectionId, string? user)
    {
        int seat = Array.IndexOf(match.Connections, null);
        if (seat < 0 || match.Started) return SeatOutcome.Full;
        match.Connections[seat] = connectionId;
        match.Users[seat] = user;
        match.Tokens[seat] = NewToken();
        if (!await _store.TryPutMatchAsync(match)) return SeatOutcome.Lost;

        await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, seat));
        await _store.PutTokenAsync(match.Tokens[seat]!, match.Id, seat);
        await _push.SendAsync(connectionId, LobbyProtocol.Joined(seat, match.Code, opponentPresent: true));
        await _push.SendAsync(connectionId, LobbyProtocol.Token(match.Tokens[seat]!));

        if (SeatedCount(match) == match.Capacity)
        {
            await BeginTableGameAsync(match, OccupiedSeats(match)); // 満員: 自動で始める
            return SeatOutcome.Seated;
        }
        if (match.Code == null) await _store.TryEnqueueAsync(match.Game, match.Id); // まだ空きがある: 次の人が入れるように戻す
        await SendTableLobbyAsync(match);
        return SeatOutcome.Seated;
    }

    static List<int> OccupiedSeats(MatchRecord match) =>
        Enumerable.Range(0, match.Capacity).Where(s => match.Connections[s] != null).ToList();

    // ---------- 始める ----------

    async Task TableStartAsync(string connectionId)
    {
        var connection = await _store.GetConnectionAsync(connectionId);
        if (connection == null || !IsTableId(connection.MatchId)) return;
        var match = await _store.GetMatchAsync(connection.MatchId);
        if (match == null || match.Started) return;
        if (connection.Seat != match.HostSeat)
        {
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.NotHost));
            return;
        }
        if (SeatedCount(match) < EngineOf(match).MinSeats)
        {
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.NotEnoughPlayers));
            return;
        }
        await BeginTableGameAsync(match, OccupiedSeats(match));
    }

    /// <summary>
    /// seats の席の人で新しい対局を始める（開始・満員・再戦）。席は 0 から詰め直し、各人に新しい席を知らせる。
    /// ほかの書き込みに負けたら読み直し、まだ始められるならやり直す。
    /// </summary>
    async Task BeginTableGameAsync(MatchRecord match, IReadOnlyList<int> seats)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var engine = EngineOf(match);
            if (seats.Count < engine.MinSeats || seats.Any(s => match.Connections[s] == null)) return;

            var next = MatchRecord.NewTable(match.Id, match.Game, match.Code, match.Capacity);
            next.Version = match.Version;
            next.GameNo = match.GameNo;
            next.HostSeat = Math.Max(0, seats.ToList().IndexOf(match.HostSeat));
            for (int i = 0; i < seats.Count; i++)
            {
                next.Connections[i] = match.Connections[seats[i]];
                next.Users[i] = match.Users[seats[i]];
                next.Tokens[i] = match.Tokens[seats[i]];
            }
            next.Started = true;
            next.PlayerCount = seats.Count;
            next.Seed = _rng.Next();
            next.TurnStartedAt = NowMs;
            next.GameNo++;
            if (await _store.TryPutMatchAsync(next))
            {
                // 始まったら、もう誰も入れない
                await _store.RemoveFromQueueAsync(next.Game, next.Id);
                if (next.Code != null) await _store.ReleaseCodeAsync(next.Game, next.Code, next.Id);
                foreach (int s in Enumerable.Range(0, match.Capacity).Except(seats))
                    if (match.Tokens[s] is { } gone) await _store.DeleteTokenAsync(gone);
                for (int i = 0; i < seats.Count; i++)
                {
                    await _store.PutConnectionAsync(new ConnectionRecord(next.Connections[i]!, next.Id, i));
                    await _store.PutTokenAsync(next.Tokens[i]!, next.Id, i);
                    await _push.SendAsync(next.Connections[i]!, LobbyProtocol.Joined(i, next.Code, opponentPresent: true));
                }
                await SendTableStateAsync(next, RebuildTable(next));
                await SendTablePlayersAsync(next);
                return;
            }
            match = await _store.GetMatchAsync(match.Id) ?? match;
            if (match.Started && !match.Rematch.Any(r => r)) return; // もうほかの処理が始めた
        }
    }

    // ---------- 対局 ----------

    async Task TableMoveAsync(ConnectionRecord connection, string message)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(connection.MatchId);
            if (match == null || !match.Started || match.Connections[connection.Seat] != connection.ConnectionId) return;

            var session = RebuildTable(match);
            if (!session.TryApply(connection.Seat, message, out var normalized))
            {
                await _push.SendAsync(connection.ConnectionId, session.SnapshotFor(connection.Seat)); // 同期を取り直す
                return;
            }
            match.Moves.Add(connection.Seat + ":" + normalized);
            match.TimeoutStrikes[connection.Seat] = 0;
            match.TurnStartedAt = NowMs;
            if (await _store.TryPutMatchAsync(match))
            {
                await SendTableStateAsync(match, session);
                if (session.IsOver) await FinishTableAsync(match, session);
                return;
            }
        }
        await _push.SendAsync(connection.ConnectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    /// <summary>
    /// 申請（@claim）。通信が切れて待ち時間を過ぎた席があれば CPU に代わってもらう。
    /// なければ、ほかの人の手番で制限時間を過ぎていれば、その人の手を自動で進める（続けて 3 回なら CPU に代わる）。
    /// </summary>
    async Task TableClaimAsync(ConnectionRecord connection)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(connection.MatchId);
            if (match == null || !match.Started || match.Connections[connection.Seat] != connection.ConnectionId) return;
            var session = RebuildTable(match);
            if (session.IsOver) return;

            var taken = new List<TakenSeat>();
            for (int s = 0; s < match.PlayerCount; s++)
            {
                if (s == connection.Seat || session.IsCpu(s) || match.AwaySince[s] is not long since) continue;
                if (NowMs - since < (long)_settings.AwayGrace.TotalMilliseconds) continue;
                if (TakeoverSeat(match, session, s) is { } t) taken.Add(t);
            }

            int current = session.CurrentPlayer;
            if (taken.Count == 0)
            {
                bool timedOut = current >= 0 && current != connection.Seat && !session.IsCpu(current) &&
                                NowMs - match.TurnStartedAt >= (long)_settings.TurnLimitFor(match.Game).TotalMilliseconds;
                if (!timedOut)
                {
                    await _push.SendAsync(connection.ConnectionId, LobbyProtocol.Error(LobbyErrors.TooEarly));
                    return;
                }
                if (!session.TryGetAutoMove(current, out var auto) || !session.TryApply(current, auto, out var normalized))
                    throw new InvalidOperationException("auto move was not legal in match " + match.Id);
                match.Moves.Add(current + ":" + normalized);
                if (++match.TimeoutStrikes[current] >= TimeoutStrikesToTakeover && !session.IsOver &&
                    TakeoverSeat(match, session, current) is { } t)
                    taken.Add(t);
            }
            match.TurnStartedAt = NowMs;

            if (!await _store.TryPutMatchAsync(match)) continue;
            await AfterTakeoverAsync(match, session, taken);
            return;
        }
    }

    /// <summary>CPU に代わった席と、消す前のその席の接続・合言葉（保存できたら片付ける）。</summary>
    sealed record TakenSeat(int Seat, string? Connection, string? Token);

    async Task TableDisconnectAsync(ConnectionRecord connection)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(connection.MatchId);
            if (match == null || match.Connections[connection.Seat] != connection.ConnectionId)
            {
                await _store.DeleteConnectionAsync(connection.ConnectionId); // もう別の接続で戻っている・部屋が無い
                return;
            }
            if (!match.Started || RebuildTable(match).IsOver)
            {
                await TableLeaveAsync(connection); // 始まる前・終局後は、抜けたことにする
                return;
            }

            match.Connections[connection.Seat] = null;
            match.AwaySince[connection.Seat] = NowMs;
            if (!await _store.TryPutMatchAsync(match)) continue;

            await _store.DeleteConnectionAsync(connection.ConnectionId);
            var message = LobbyProtocol.SeatAway((int)_settings.AwayGrace.TotalSeconds, connection.Seat);
            foreach (var other in match.Connections)
                if (other != null) await _push.SendAsync(other, message);
            return;
        }
    }

    /// <summary>部屋から抜ける。始まる前は席を空け、対局中は CPU に代わってもらい、終局後は再戦の顔ぶれから外れる。</summary>
    async Task TableLeaveAsync(ConnectionRecord connection)
    {
        int seat = connection.Seat;
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(connection.MatchId);
            if (match == null || match.Connections[seat] != connection.ConnectionId)
            {
                await _store.DeleteConnectionAsync(connection.ConnectionId);
                return;
            }
            var token = match.Tokens[seat];

            if (!match.Started)
            {
                match.Connections[seat] = null;
                match.Users[seat] = null;
                match.Tokens[seat] = null;
                if (SeatedCount(match) == 0)
                {
                    await CloseMatchAsync(match);
                    if (token != null) await _store.DeleteTokenAsync(token);
                    await _store.DeleteConnectionAsync(connection.ConnectionId);
                    return;
                }
                if (match.HostSeat == seat) match.HostSeat = OccupiedSeats(match)[0]; // ホストが抜けた: 次の人がホスト
                if (!await _store.TryPutMatchAsync(match)) continue;
                await _store.DeleteConnectionAsync(connection.ConnectionId);
                if (token != null) await _store.DeleteTokenAsync(token);
                if (match.Code == null) await _store.TryEnqueueAsync(match.Game, match.Id); // 空きができた
                await SendTableLobbyAsync(match);
                return;
            }

            var session = RebuildTable(match);
            if (session.IsOver)
            {
                match.Connections[seat] = null;
                match.Rematch[seat] = false;
                if (match.Connections.All(c => c == null))
                {
                    await CloseMatchAsync(match);
                    await _store.DeleteConnectionAsync(connection.ConnectionId);
                    return;
                }
                if (!await _store.TryPutMatchAsync(match)) continue;
                await _store.DeleteConnectionAsync(connection.ConnectionId);
                if (token != null) await _store.DeleteTokenAsync(token);
                await StartRematchIfReadyAsync(match);
                return;
            }

            // 対局の途中で抜けた: その席は CPU が代わる（抜けた人は最下位）。抜けた本人には知らせない
            if (TakeoverSeat(match, session, seat) is not { } taken) return;
            match.TurnStartedAt = NowMs;
            if (!await _store.TryPutMatchAsync(match)) continue;
            await AfterTakeoverAsync(match, session, new[] { taken with { Connection = null } });
            await _store.DeleteConnectionAsync(connection.ConnectionId);
            return;
        }
    }

    /// <summary>
    /// 以後その席を CPU が代わる。手の一覧に印を残し、席の接続・合言葉・離席を消す（保存は呼んだ側）。
    /// 消す前の接続と合言葉を返す（もう CPU なら null）。
    /// </summary>
    static TakenSeat? TakeoverSeat(MatchRecord match, ITableSession session, int seat)
    {
        if (!session.Takeover(seat)) return null;
        var taken = new TakenSeat(seat, match.Connections[seat], match.Tokens[seat]);
        match.Moves.Add(seat + ":" + CpuRecord);
        match.Connections[seat] = null;
        match.Tokens[seat] = null;
        match.AwaySince[seat] = null;
        match.TimeoutStrikes[seat] = 0;
        return taken;
    }

    /// <summary>CPU に代わったことを全員に知らせ、その席の合言葉と接続を片付け、局面を送り直す。終局したら結果を残す。</summary>
    async Task AfterTakeoverAsync(MatchRecord match, ITableSession session, IReadOnlyCollection<TakenSeat> taken)
    {
        foreach (var t in taken)
        {
            if (t.Token != null) await _store.DeleteTokenAsync(t.Token);
            if (t.Connection != null)
            {
                // まだつながっている（時間切れが続いた）人には、自分の席を CPU が代わったことを知らせてから外す
                await _push.SendAsync(t.Connection, LobbyProtocol.CpuSeat(t.Seat));
                await _store.DeleteConnectionAsync(t.Connection);
            }
            foreach (var other in match.Connections)
                if (other != null) await _push.SendAsync(other, LobbyProtocol.CpuSeat(t.Seat));
        }
        await SendTableStateAsync(match, session);
        if (session.IsOver) await FinishTableAsync(match, session);
        if (match.Connections.All(c => c == null)) await CloseMatchAsync(match); // つながっている人がもういない
    }

    // ---------- 戻る ----------

    async Task TableResumeAsync(string connectionId, string token, string matchId, int seat)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(matchId);
            if (match == null || seat >= match.Capacity || match.Tokens[seat] != token)
            {
                await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.SessionExpired));
                return;
            }

            var previous = match.Connections[seat];
            bool wasAway = match.AwaySince[seat] != null;
            match.Connections[seat] = connectionId;
            match.AwaySince[seat] = null;
            if (!await _store.TryPutMatchAsync(match)) continue;

            if (previous != null && previous != connectionId) await _store.DeleteConnectionAsync(previous);
            await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, seat));

            int others = match.Started ? match.PlayerCount - 1 : SeatedCount(match) - 1;
            await _push.SendAsync(connectionId, LobbyProtocol.Resumed(seat, match.Code, others > 0));
            if (match.Started)
            {
                var session = RebuildTable(match);
                await _push.SendAsync(connectionId, session.SnapshotFor(seat));
                await _push.SendAsync(connectionId, TurnMessage(match, session));
                await SendTablePlayersAsync(match, connectionId);
                for (int s = 0; s < match.PlayerCount; s++)
                {
                    if (s == seat) continue;
                    if (session.IsCpu(s)) await _push.SendAsync(connectionId, LobbyProtocol.CpuSeat(s));
                    else if (match.AwaySince[s] is long since)
                        await _push.SendAsync(connectionId, LobbyProtocol.SeatAway(RemainingAwaySeconds(since), s));
                }
                if (Enumerable.Range(0, match.PlayerCount).Any(s => s != seat && match.Rematch[s]))
                    await _push.SendAsync(connectionId, RematchMessage);
            }
            else
            {
                await _push.SendAsync(connectionId, LobbyProtocol.Table(SeatedCount(match), match.Capacity, match.HostSeat));
                await SendTablePlayersAsync(match, connectionId);
            }
            if (wasAway)
                foreach (var other in match.Connections)
                    if (other != null && other != connectionId) await _push.SendAsync(other, LobbyProtocol.OpponentBack());
            return;
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    // ---------- 再戦 ----------

    async Task TableRematchAsync(ConnectionRecord connection)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(connection.MatchId);
            if (match == null || !match.Started || match.Connections[connection.Seat] != connection.ConnectionId) return;
            if (match.Rematch[connection.Seat] || !RebuildTable(match).IsOver) return;

            match.Rematch[connection.Seat] = true;
            if (!await _store.TryPutMatchAsync(match)) continue;
            foreach (var other in match.Connections)
                if (other != null && other != connection.ConnectionId) await _push.SendAsync(other, RematchMessage);
            await StartRematchIfReadyAsync(match);
            return;
        }
    }

    /// <summary>つながっている人が全員再戦を希望していれば、その顔ぶれで新しい対局を始める（最少人数に足りなければ知らせる）。</summary>
    async Task StartRematchIfReadyAsync(MatchRecord match)
    {
        var present = OccupiedSeats(match);
        if (present.Count == 0 || !present.All(s => match.Rematch[s])) return;
        if (present.Count < EngineOf(match).MinSeats)
        {
            foreach (int s in present) await _push.SendAsync(match.Connections[s]!, LobbyProtocol.Error(LobbyErrors.NotEnoughPlayers));
            return;
        }
        await BeginTableGameAsync(match, present);
    }

    // ---------- 内部 ----------

    /// <summary>「種 ＋ 手の一覧（CPU に代わった印も含む）」から局面を作り直す。</summary>
    ITableSession RebuildTable(MatchRecord match)
    {
        var session = EngineOf(match).StartTable(match.Seed, match.PlayerCount);
        foreach (var record in match.Moves)
        {
            int seat = record[0] - '0';
            bool ok = record.Length >= 3 && record[1] == ':' &&
                      (record.Substring(2) == CpuRecord ? session.Takeover(seat) : session.TryApply(seat, record.Substring(2), out _));
            if (!ok) throw new InvalidOperationException($"broken move record in match {match.Id}: {record}");
        }
        return session;
    }

    /// <summary>つながっている席に、局面と手番の残り時間を送る。</summary>
    async Task SendTableStateAsync(MatchRecord match, ITableSession session)
    {
        var turn = TurnMessage(match, session);
        for (int seat = 0; seat < match.PlayerCount; seat++)
        {
            var connectionId = match.Connections[seat];
            if (connectionId == null) continue;
            await _push.SendAsync(connectionId, session.SnapshotFor(seat));
            await _push.SendAsync(connectionId, turn);
        }
    }

    /// <summary>始まる前の部屋の様子（人数・最大・ホスト）と、座っている人の名前を全員に送る。</summary>
    async Task SendTableLobbyAsync(MatchRecord match)
    {
        var message = LobbyProtocol.Table(SeatedCount(match), match.Capacity, match.HostSeat);
        foreach (var connectionId in match.Connections)
            if (connectionId != null) await _push.SendAsync(connectionId, message);
        await SendTablePlayersAsync(match);
    }

    /// <summary>席ごとの名前とレート（対局中は人数分、始まる前は最大人数分。空いている席は名前なし）。</summary>
    async Task SendTablePlayersAsync(MatchRecord match, string? onlyTo = null)
    {
        int count = match.Started ? match.PlayerCount : match.Capacity;
        var names = new string?[count];
        var ratings = Enumerable.Repeat(-1, count).ToArray();
        for (int seat = 0; seat < count; seat++)
        {
            if (match.Users[seat] is not { } user) continue;
            var profile = await _store.GetPlayerAsync(user);
            names[seat] = profile.Name;
            ratings[seat] = profile.Stats.TryGetValue(match.Game, out var stats) ? stats.Rating : _settings.InitialRating;
        }
        var message = LobbyProtocol.Players(names!, ratings);
        if (onlyTo != null)
        {
            await _push.SendAsync(onlyTo, message);
            return;
        }
        foreach (var connectionId in match.Connections)
            if (connectionId != null) await _push.SendAsync(connectionId, message);
    }

    async Task FinishTableAsync(MatchRecord match, ITableSession session)
    {
        var change = await SaveTableResultAsync(match, session);
        if (change == null) return;
        for (int seat = 0; seat < match.PlayerCount; seat++)
            if (change.After[seat] >= 0 && match.Connections[seat] is { } connectionId)
                await _push.SendAsync(connectionId, LobbyProtocol.Rated(change.After[seat], change.Deltas[seat]));
    }

    /// <summary>
    /// 順位で結果を 1 行残し、ログインしていた人の戦績を同時に書く。1 位が 1 人ならその人の勝ち、1 位が並んだら引き分け、ほかは負け。
    /// 同じアカウントが複数の席にいたら、そのアカウントは数えない。ランダムマッチで、数える人が 2 人以上いればレートも動く。
    /// </summary>
    async Task<TableRatingChange?> SaveTableResultAsync(MatchRecord match, ITableSession session)
    {
        int n = match.PlayerCount;
        var ranks = session.Ranks.ToArray();
        var users = match.Users.Take(n).ToArray();
        var counts = users.Where(u => u != null).GroupBy(u => u!).ToDictionary(g => g.Key, g => g.Count());
        var counted = Enumerable.Range(0, n).Where(s => users[s] is { } u && counts[u] == 1).ToList();
        int firstPlaces = ranks.Count(r => r == 0);
        int winner = firstPlaces == 1 ? Array.IndexOf(ranks, 0) : -1;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var before = new PlayerStats?[n];
            foreach (int s in counted)
            {
                var profile = await _store.GetPlayerAsync(users[s]!);
                before[s] = profile.Stats.GetValueOrDefault(match.Game) ?? PlayerStats.New(users[s]!, match.Game, _settings.InitialRating);
            }

            TableRatingChange? change = null;
            if (match.Code == null && counted.Count >= 2)
            {
                var deltas = MultiElo.Deltas(counted.Select(s => before[s]!.Rating).ToList(), counted.Select(s => ranks[s]).ToList(),
                    _settings.RatingK);
                var after = Enumerable.Repeat(-1, n).ToArray();
                var allDeltas = new int[n];
                for (int i = 0; i < counted.Count; i++)
                {
                    after[counted[i]] = before[counted[i]]!.Rating + deltas[i];
                    allDeltas[counted[i]] = deltas[i];
                }
                change = new TableRatingChange(after, allDeltas);
            }

            var updates = new List<PlayerStats>();
            foreach (int s in counted)
            {
                var st = before[s]!;
                bool first = ranks[s] == 0;
                updates.Add(st with
                {
                    Games = st.Games + 1,
                    Wins = st.Wins + (first && firstPlaces == 1 ? 1 : 0),
                    Draws = st.Draws + (first && firstPlaces > 1 ? 1 : 0),
                    Losses = st.Losses + (first ? 0 : 1),
                    Rating = change != null ? change.After[s] : st.Rating,
                    RatedGames = st.RatedGames + (change != null ? 1 : 0),
                });
            }

            var result = new MatchResult(match.Id, match.GameNo, match.Game, NowMs, winner,
                winner >= 0 ? ResultReasons.Normal : ResultReasons.Draw, match.Moves.Count, match.Moves.ToArray(), match.Seed,
                users, change?.After, change?.Deltas, ranks);
            switch (await _store.TrySaveResultAsync(result, updates))
            {
                case SaveOutcome.Saved: return change;
                case SaveOutcome.AlreadySaved: return null;
            }
        }
        return null;
    }
}

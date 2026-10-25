using System.Security.Cryptography;
using BoardGames.Net;
using BoardGames.Server.Games;

namespace BoardGames.Server;

/// <summary>
/// オンライン対戦サーバーの中身。AWS には依存しない（保存先・送信先・時計は差し替えられる）。
/// - ロビー: ランダムマッチ（待ち行列）とルームコード。最初にいた人が席 0、あとから来た人が席 1。
/// - 対局: サーバーが局面を持つ（サーバー権威型）。手は各ゲームのルールで検証し、両方の席に局面を送る。
///   ブラウザ側は両者とも OnlineMatch の「ゲスト」として動く。
/// - 通信が切れたとき: 対局中なら部屋は消さず、その席を「離席中」にして相手に知らせる。
///   切れた側は合言葉（Token）で同じ席に戻れる（Resume）。待ち時間（ServerSettings.AwayGrace）を過ぎても
///   戻らなければ、残った側が勝ちを申請できる（Claim）。タイマーは動かさず、申請が届いたときに時刻を確かめる。
/// - 1 手の制限時間（ServerSettings.TurnLimitFor）: 局面と一緒に残り時間（@turn）を送る。過ぎたら待っている側が
///   Claim で勝ちを申請できる。申請される前に手が届けば、その手は受け付ける。
/// - 対局結果: 終局・時間切れ・離脱のたびに 1 局 1 行（MatchResult）を残す。同じ対局を 2 回は書かない。
/// - ログイン（ステップ 5-2）: 部屋に入る前に @auth で ID トークンを受け取り、確かめたら接続をその人のものと覚える。
///   部屋に入るときに席とユーザーを結びつけ、対局の始まりに両者の名前とレート（@players）を送る。
///   終局したら、ログインしていた人の勝敗の数を、レートが動く対局（MatchRecord.IsRated）ならレートも、結果と同時に書く。
/// - メッセージ: ロビーは LobbyProtocol（"@…"）、対局中は各ゲームの NetProtocol（"S|1|…" など）。
/// - 多人数の部屋（ゴースト・トレンド。IGameEngine.MaxSeats が 3 以上）は GameServer.Table.cs で動かす。
///   対局 ID の頭が "t" の部屋は、各処理の最初でそちらに渡す（2 人用の処理はここから下のまま）。
/// </summary>
public sealed partial class GameServer
{
    const int MaxRetries = 5;
    const string RematchMessage = "R|1";

    readonly IMatchStore _store;
    readonly IConnectionPush _push;
    readonly GameRegistry _games;
    readonly Random _rng;
    readonly Func<DateTimeOffset> _clock;
    readonly ServerSettings _settings;
    readonly Auth.ITokenVerifier? _verifier;

    /// <param name="verifier">ID トークンを確かめるもの。null ならログインは受け付けない（全員ログインしていない扱い）</param>
    public GameServer(IMatchStore store, IConnectionPush push, GameRegistry games, Random? rng = null,
        Func<DateTimeOffset>? clock = null, ServerSettings? settings = null, Auth.ITokenVerifier? verifier = null)
    {
        _store = store;
        _push = push;
        _games = games;
        _rng = rng ?? Random.Shared;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _settings = settings ?? ServerSettings.Default;
        _verifier = verifier;
    }

    /// <summary>レートが動いた対局の、席ごとの対局後のレートと増減。</summary>
    sealed record RatingChange(int[] After, int[] Deltas);

    long NowMs => _clock().ToUnixTimeMilliseconds();

    /// <summary>
    /// 接続が切れた（ブラウザを閉じた・通信が切れた）。
    /// 対局中なら席を「離席中」にして相手に知らせる（戻ってくるのを待つ）。それ以外なら部屋から抜ける。
    /// </summary>
    public async Task OnDisconnectAsync(string connectionId)
    {
        var connection = await _store.GetConnectionAsync(connectionId);
        if (connection == null) return;
        if (IsTableId(connection.MatchId))
        {
            await TableDisconnectAsync(connection);
            return;
        }

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var match = await _store.GetMatchAsync(connection.MatchId);
            if (match == null || match.Connections[connection.Seat] != connectionId)
            {
                await _store.DeleteConnectionAsync(connectionId); // もう別の接続で戻っている・対局が無い
                return;
            }
            if (!match.Started || Rebuild(match).IsOver)
            {
                await LeaveAsync(connectionId); // 相手待ち・終局後は、これまでどおり抜ける
                return;
            }

            match.Connections[connection.Seat] = null;
            match.AwaySince[connection.Seat] = NowMs;
            if (!await _store.TryPutMatchAsync(match)) continue;

            await _store.DeleteConnectionAsync(connectionId);
            var other = match.Connections[match.OtherSeat(connection.Seat)];
            if (other != null) await _push.SendAsync(other, LobbyProtocol.OpponentAway((int)_settings.AwayGrace.TotalSeconds));
            return;
        }
    }

    /// <summary>ブラウザからのメッセージ 1 つを処理する。</summary>
    public async Task OnMessageAsync(string connectionId, string? message)
    {
        if (string.IsNullOrEmpty(message) || message.Length > LobbyProtocol.MaxMessageLength) return;

        if (LobbyProtocol.IsLobbyMessage(message))
        {
            if (!LobbyProtocol.TryParse(message, out var lobby))
            {
                await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.BadRequest));
                return;
            }
            switch (lobby.Kind)
            {
                case LobbyMessageKind.Quick when IsTableGame(lobby.Game): await TableQuickAsync(connectionId, lobby.Game); break;
                case LobbyMessageKind.Create when IsTableGame(lobby.Game): await TableCreateAsync(connectionId, lobby.Game); break;
                case LobbyMessageKind.Join when IsTableGame(lobby.Game): await TableJoinAsync(connectionId, lobby.Game, lobby.Code); break;
                case LobbyMessageKind.Start: await TableStartAsync(connectionId); break;
                case LobbyMessageKind.Quick: await QuickAsync(connectionId, lobby.Game); break;
                case LobbyMessageKind.Create: await CreateAsync(connectionId, lobby.Game); break;
                case LobbyMessageKind.Join: await JoinAsync(connectionId, lobby.Game, lobby.Code); break;
                case LobbyMessageKind.Leave: await LeaveAsync(connectionId); break;
                case LobbyMessageKind.Ping: await _push.SendAsync(connectionId, LobbyProtocol.Pong()); break;
                case LobbyMessageKind.Resume: await ResumeAsync(connectionId, lobby.Token); break;
                case LobbyMessageKind.Claim: await ClaimAsync(connectionId); break;
                case LobbyMessageKind.Auth: await AuthAsync(connectionId, lobby.IdToken); break;
                default: await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.BadRequest)); break;
            }
            return;
        }

        if (message == RematchMessage) await RematchAsync(connectionId);
        else await MoveAsync(connectionId, message);
    }

    // ---------- ログイン ----------

    /// <summary>ID トークンを確かめ、この接続をその人のものと覚える。返事は @authed|表示名 か @error|auth_failed。</summary>
    async Task AuthAsync(string connectionId, string idToken)
    {
        var userId = _verifier == null ? null : await _verifier.VerifyAsync(idToken);
        if (userId == null)
        {
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.AuthFailed));
            return;
        }
        await _store.PutAuthAsync(connectionId, userId, _settings.AuthTtl);
        var player = await _store.GetPlayerAsync(userId);
        await _push.SendAsync(connectionId, LobbyProtocol.Authed(player.Name));
    }

    // ---------- ロビー ----------

    async Task QuickAsync(string connectionId, string game)
    {
        var (ok, user) = await PrepareToJoinAsync(connectionId, game);
        if (!ok) return;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            // 待っている人がいれば、その部屋の席 1 に入る
            var waitingId = await _store.TryDequeueAsync(game);
            if (waitingId != null)
            {
                var waiting = await _store.GetMatchAsync(waitingId);
                if (waiting != null && await TrySeatSecondAsync(waiting, connectionId, user)) return;
                continue; // 待っていた人が抜けていた・ほかの人が先に入った: もう一度探す
            }

            // 誰もいなければ、自分の部屋を作って待ち行列に入る
            var match = NewMatch(game, connectionId, code: null, user);
            if (!await _store.TryPutMatchAsync(match)) continue;
            if (await _store.TryEnqueueAsync(game, match.Id))
            {
                await SeatFirstAsync(match, connectionId);
                return;
            }
            await _store.DeleteMatchAsync(match.Id); // ほぼ同時にほかの人が並んだ: その人と組めるか探し直す
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    async Task CreateAsync(string connectionId, string game)
    {
        var (ok, user) = await PrepareToJoinAsync(connectionId, game);
        if (!ok) return;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            string code = _rng.Next(10000).ToString("D4");
            var match = NewMatch(game, connectionId, code, user);
            if (!await _store.TryReserveCodeAsync(game, code, match.Id)) continue; // 使用中のコード: 引き直す
            if (!await _store.TryPutMatchAsync(match))
            {
                await _store.ReleaseCodeAsync(game, code, match.Id);
                continue;
            }
            await SeatFirstAsync(match, connectionId);
            return;
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    async Task JoinAsync(string connectionId, string game, string code)
    {
        var (ok, user) = await PrepareToJoinAsync(connectionId, game);
        if (!ok) return;

        var matchId = await _store.FindCodeAsync(game, code);
        var match = matchId == null ? null : await _store.GetMatchAsync(matchId);
        if (match == null || match.Connections[0] == null)
        {
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.RoomNotFound));
            return;
        }
        if (!await TrySeatSecondAsync(match, connectionId, user))
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.RoomFull));
    }

    /// <summary>ゲームがあるかを確かめ、すでにほかの部屋にいれば抜けておく。ログインしていればユーザー ID も返す。</summary>
    async Task<(bool Ok, string? User)> PrepareToJoinAsync(string connectionId, string game)
    {
        if (_games.Find(game) == null)
        {
            await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.UnknownGame));
            return (false, null);
        }
        await LeaveAsync(connectionId);
        return (true, await _store.GetAuthAsync(connectionId));
    }

    /// <summary>作った部屋の席 0 に座る（保存済みの部屋に対して）。</summary>
    async Task SeatFirstAsync(MatchRecord match, string connectionId)
    {
        await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, 0));
        await _store.PutTokenAsync(match.Tokens[0]!, match.Id, 0);
        await _push.SendAsync(connectionId, LobbyProtocol.Joined(0, match.Code, opponentPresent: false));
        await _push.SendAsync(connectionId, LobbyProtocol.Token(match.Tokens[0]!));
    }

    /// <summary>部屋の席 1 に入り、対局を始める。席が埋まっていた・ほかの書き込みに負けたら false。</summary>
    async Task<bool> TrySeatSecondAsync(MatchRecord match, string connectionId, string? user)
    {
        if (match.Connections[1] != null || match.Started) return false;
        match.Connections[1] = connectionId;
        match.Users[1] = user;
        match.Tokens[1] = NewToken();
        StartNewGame(match);
        if (!await _store.TryPutMatchAsync(match)) return false;

        if (match.Code != null) await _store.ReleaseCodeAsync(match.Game, match.Code, match.Id); // もう入れないのでコードを返す
        await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, 1));
        await _store.PutTokenAsync(match.Tokens[1]!, match.Id, 1);
        await _push.SendAsync(connectionId, LobbyProtocol.Joined(1, match.Code, opponentPresent: true));
        await _push.SendAsync(connectionId, LobbyProtocol.Token(match.Tokens[1]!));
        await _push.SendAsync(match.Connections[0]!, LobbyProtocol.OpponentJoined());
        await SendStateAsync(match, Rebuild(match));
        await SendPlayersAsync(match);
        return true;
    }

    /// <summary>部屋から抜ける（自分で抜けた・相手待ちや終局後に切れた）。相手がいれば「相手が抜けた」と知らせる。</summary>
    async Task LeaveAsync(string connectionId)
    {
        var connection = await _store.GetConnectionAsync(connectionId);
        if (connection == null) return;
        if (IsTableId(connection.MatchId))
        {
            await TableLeaveAsync(connection);
            return;
        }
        await _store.DeleteConnectionAsync(connectionId);

        var match = await _store.GetMatchAsync(connection.MatchId);
        if (match == null) return;
        RatingChange? change = null;
        if (match.Started)
        {
            var session = Rebuild(match);
            if (!session.IsOver) change = await SaveResultAsync(match, match.OtherSeat(connection.Seat), ResultReasons.Left); // 対局の途中で抜けた: 相手の勝ち
        }
        await CloseMatchAsync(match);

        // 相手には「相手が抜けた」と知らせ、相手の席の記録も消す（相手はロビーに戻る）
        var other = match.Connections[match.OtherSeat(connection.Seat)];
        if (other != null)
        {
            await _store.DeleteConnectionAsync(other);
            await _push.SendAsync(other, LobbyProtocol.OpponentLeft());
        }
        await SendRatedAsync(match, change);
    }

    /// <summary>合言葉で、通信が切れる前の席に戻る。</summary>
    async Task ResumeAsync(string connectionId, string token)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var found = await _store.FindTokenAsync(token);
            if (found != null && IsTableId(found.Value.MatchId))
            {
                await TableResumeAsync(connectionId, token, found.Value.MatchId, found.Value.Seat);
                return;
            }
            var match = found == null ? null : await _store.GetMatchAsync(found.Value.MatchId);
            if (found == null || match == null || match.Tokens[found.Value.Seat] != token)
            {
                await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.SessionExpired));
                return;
            }

            int seat = found.Value.Seat;
            int otherSeat = match.OtherSeat(seat);
            var previous = match.Connections[seat];
            bool wasAway = match.AwaySince[seat] != null;
            match.Connections[seat] = connectionId;
            match.AwaySince[seat] = null;
            if (!await _store.TryPutMatchAsync(match)) continue;

            if (previous != null && previous != connectionId) await _store.DeleteConnectionAsync(previous); // 切れたと気づく前の古い接続
            await _store.PutConnectionAsync(new ConnectionRecord(connectionId, match.Id, seat));

            bool opponentPresent = match.Connections[otherSeat] != null || match.AwaySince[otherSeat] != null;
            await _push.SendAsync(connectionId, LobbyProtocol.Resumed(seat, match.Code, opponentPresent));
            if (match.Started)
            {
                var session = Rebuild(match);
                await _push.SendAsync(connectionId, session.SnapshotFor(seat));
                await _push.SendAsync(connectionId, TurnMessage(match, session));
                await SendPlayersAsync(match, connectionId);
                if (match.Rematch[otherSeat]) await _push.SendAsync(connectionId, RematchMessage);
                if (match.AwaySince[otherSeat] is long since) await _push.SendAsync(connectionId, LobbyProtocol.OpponentAway(RemainingAwaySeconds(since)));
            }
            var other = match.Connections[otherSeat];
            if (other != null && wasAway) await _push.SendAsync(other, LobbyProtocol.OpponentBack());
            return;
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.ServerBusy));
    }

    /// <summary>勝ちの申請。相手の通信が切れてから待ち時間を過ぎていれば、申請した側の勝ちにして対局を閉じる。</summary>
    async Task ClaimAsync(string connectionId)
    {
        var connection = await _store.GetConnectionAsync(connectionId);
        if (connection == null) return;
        if (IsTableId(connection.MatchId))
        {
            await TableClaimAsync(connection);
            return;
        }
        var match = await _store.GetMatchAsync(connection.MatchId);
        if (match == null || !match.Started) return;

        int otherSeat = match.OtherSeat(connection.Seat);
        if (match.AwaySince[otherSeat] is long since && NowMs - since >= (long)_settings.AwayGrace.TotalMilliseconds)
        {
            var abandonChange = await SaveResultAsync(match, connection.Seat, ResultReasons.Abandoned);
            await CloseMatchAsync(match);
            await _store.DeleteConnectionAsync(connectionId);
            await _push.SendAsync(connectionId, LobbyProtocol.Won(LobbyEndReasons.Abandon));
            await SendRatedAsync(match, abandonChange);
            return;
        }

        // 相手の手番で、1 手の制限時間を過ぎている: 時間切れ負け
        var session = Rebuild(match);
        if (!session.IsOver && session.CurrentPlayer == otherSeat &&
            NowMs - match.TurnStartedAt >= (long)_settings.TurnLimitFor(match.Game).TotalMilliseconds)
        {
            var timeoutChange = await SaveResultAsync(match, connection.Seat, ResultReasons.Timeout);
            await CloseMatchAsync(match);
            await _store.DeleteConnectionAsync(connectionId);
            await _push.SendAsync(connectionId, LobbyProtocol.Won(LobbyEndReasons.Timeout));
            var other = match.Connections[otherSeat];
            if (other != null)
            {
                await _store.DeleteConnectionAsync(other);
                await _push.SendAsync(other, LobbyProtocol.Lost(LobbyEndReasons.Timeout));
            }
            await SendRatedAsync(match, timeoutChange);
            return;
        }
        await _push.SendAsync(connectionId, LobbyProtocol.Error(LobbyErrors.TooEarly));
    }

    // ---------- 対局 ----------

    async Task MoveAsync(string connectionId, string message)
    {
        var connection = await _store.GetConnectionAsync(connectionId);
        if (connection == null) return; // 対局に入っていない
        if (IsTableId(connection.MatchId))
        {
            await TableMoveAsync(connection, message);
            return;
        }

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
            match.TurnStartedAt = NowMs;
            if (await _store.TryPutMatchAsync(match))
            {
                await SendStateAsync(match, session);
                if (session.IsOver)
                {
                    var result = session.Result;
                    var change = await SaveResultAsync(match, result.Status == Core.GameStatus.Win ? result.Winner : -1,
                        result.Status == Core.GameStatus.Win ? ResultReasons.Normal : ResultReasons.Draw);
                    await SendRatedAsync(match, change);
                }
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
        if (IsTableId(connection.MatchId))
        {
            await TableRematchAsync(connection);
            return;
        }

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
                await SendStateAsync(match, Rebuild(match));
                await SendPlayersAsync(match); // 前の対局でレートが変わっている
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

    MatchRecord NewMatch(string game, string connectionId, string? code, string? user)
    {
        var match = new MatchRecord { Id = Guid.NewGuid().ToString("N"), Game = game, Code = code };
        match.Connections[0] = connectionId;
        match.Users[0] = user;
        match.Tokens[0] = NewToken();
        return match;
    }

    /// <summary>推測できない合言葉（16 進数 32 文字 = 128 ビット）。</summary>
    static string NewToken() => RandomNumberGenerator.GetHexString(LobbyProtocol.TokenLength, lowercase: true);

    /// <summary>対局と、それに付いている待ち行列・ルームコード・合言葉を消す。</summary>
    async Task CloseMatchAsync(MatchRecord match)
    {
        await _store.DeleteMatchAsync(match.Id);
        await _store.RemoveFromQueueAsync(match.Game, match.Id);
        if (match.Code != null) await _store.ReleaseCodeAsync(match.Game, match.Code, match.Id);
        foreach (var token in match.Tokens)
            if (token != null) await _store.DeleteTokenAsync(token);
    }

    int RemainingAwaySeconds(long since) =>
        (int)Math.Max(0, Math.Ceiling((_settings.AwayGrace.TotalMilliseconds - (NowMs - since)) / 1000.0));

    /// <summary>新しい対局にする（種を引き直し、手と再戦の希望を消す）。</summary>
    void StartNewGame(MatchRecord match)
    {
        match.Started = true;
        match.Seed = _rng.Next();
        match.Moves.Clear();
        match.Rematch[0] = match.Rematch[1] = false;
        match.TurnStartedAt = NowMs;
        match.GameNo++;
    }

    /// <summary>
    /// 対局結果を 1 行残し、ログインしていた人の戦績（勝敗の数、レートが動く対局ならレートも）を同時に書く。
    /// 同じ対局の 2 回目は何も書かない。戦績がほかの対局の終わりと重なったら、読み直してやり直す。
    /// レートが動いたら、席ごとの対局後のレートと増減を返す（呼んだ側が終局のお知らせのあとに @rated で送る）。
    /// </summary>
    async Task<RatingChange?> SaveResultAsync(MatchRecord match, int winner, string reason)
    {
        // 同じアカウントが両方の席にいるときは、戦績を数えない（自分と対戦して勝ちを増やせないように）
        bool countStats = match.Users[0] != match.Users[1];
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var before = new PlayerStats?[2];
            for (int seat = 0; seat < 2; seat++)
            {
                if (!countStats || match.Users[seat] is not { } user) continue;
                var profile = await _store.GetPlayerAsync(user);
                before[seat] = profile.Stats.GetValueOrDefault(match.Game) ?? PlayerStats.New(user, match.Game, _settings.InitialRating);
            }

            RatingChange? change = null;
            if (match.IsRated)
            {
                double score0 = winner == 0 ? 1 : winner == 1 ? 0 : 0.5;
                var (d0, d1) = Elo.Deltas(before[0]!.Rating, before[1]!.Rating, score0, _settings.RatingK);
                change = new RatingChange(new[] { before[0]!.Rating + d0, before[1]!.Rating + d1 }, new[] { d0, d1 });
            }

            var updates = new List<PlayerStats>();
            for (int seat = 0; seat < 2; seat++)
            {
                if (before[seat] is not { } s) continue;
                updates.Add(s with
                {
                    Games = s.Games + 1,
                    Wins = s.Wins + (winner == seat ? 1 : 0),
                    Losses = s.Losses + (winner == 1 - seat ? 1 : 0),
                    Draws = s.Draws + (winner == -1 ? 1 : 0),
                    Rating = change?.After[seat] ?? s.Rating,
                    RatedGames = s.RatedGames + (change != null ? 1 : 0),
                });
            }

            var result = new MatchResult(match.Id, match.GameNo, match.Game, NowMs, winner, reason, match.Moves.Count,
                match.Moves.ToArray(), match.Seed, match.Users.ToArray(), change?.After, change?.Deltas);
            switch (await _store.TrySaveResultAsync(result, updates))
            {
                case SaveOutcome.Saved: return change;
                case SaveOutcome.AlreadySaved: return null;
                // Conflict: 読み直してやり直す
            }
        }
        return null;
    }

    /// <summary>レートが動いたら、つながっている席に新しいレートと増減を知らせる。</summary>
    async Task SendRatedAsync(MatchRecord match, RatingChange? change)
    {
        if (change == null) return;
        for (int seat = 0; seat < 2; seat++)
            if (match.Connections[seat] is { } connectionId)
                await _push.SendAsync(connectionId, LobbyProtocol.Rated(change.After[seat], change.Deltas[seat]));
    }

    /// <summary>両方の席の名前とレートを送る（onlyTo を指定したら、その接続にだけ）。ログインしていない席は名前なし・レート -。</summary>
    async Task SendPlayersAsync(MatchRecord match, string? onlyTo = null)
    {
        var names = new string?[2];
        var ratings = new[] { -1, -1 };
        for (int seat = 0; seat < 2; seat++)
        {
            if (match.Users[seat] is not { } user) continue;
            var profile = await _store.GetPlayerAsync(user);
            names[seat] = profile.Name;
            ratings[seat] = profile.Stats.TryGetValue(match.Game, out var stats) ? stats.Rating : _settings.InitialRating;
        }
        var message = LobbyProtocol.Players(names[0], ratings[0], names[1], ratings[1]);
        if (onlyTo != null)
        {
            await _push.SendAsync(onlyTo, message);
            return;
        }
        foreach (var connectionId in match.Connections)
            if (connectionId != null) await _push.SendAsync(connectionId, message);
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

    /// <summary>両方の席に、局面と手番の残り時間を送る（通信が切れている席には送らない）。</summary>
    async Task SendStateAsync(MatchRecord match, IGameSession session)
    {
        var turn = TurnMessage(match, session);
        for (int seat = 0; seat < 2; seat++)
        {
            var connectionId = match.Connections[seat];
            if (connectionId == null) continue;
            await _push.SendAsync(connectionId, session.SnapshotFor(seat));
            await _push.SendAsync(connectionId, turn);
        }
    }

    /// <summary>手番の残り時間（終局していれば「計測なし」）。</summary>
    string TurnMessage(MatchRecord match, IGameSession session)
    {
        if (session.IsOver) return LobbyProtocol.TurnOff();
        double left = _settings.TurnLimitFor(match.Game).TotalMilliseconds - (NowMs - match.TurnStartedAt);
        return LobbyProtocol.Turn(session.CurrentPlayer, (int)Math.Max(0, Math.Ceiling(left / 1000.0)));
    }
}

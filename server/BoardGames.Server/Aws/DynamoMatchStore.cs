using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace BoardGames.Server.Aws;

/// <summary>
/// DynamoDB の 1 つのテーブルに、種類ごとに先頭の文字を変えたキー（pk）で保存する。
///   m#対局ID           対局（席の接続 ID・種・手の一覧・版番号）
///   c#接続ID           接続がどの対局のどの席にいるか
///   q#ゲーム           ランダムマッチの待ち行列（待っている対局 1 つ）
///   r#ゲーム#コード    ルームコード → 対局
///   k#合言葉           合言葉 → 対局と席（通信が切れたあと、同じ席に戻るため）
///   u#接続ID           ログインした接続 → ユーザー ID（ステップ 5-2）
/// どの行にも ttl（期限）を付け、古いものは DynamoDB が自動で消す（消えるまで最大 2 日ほどかかる）。
/// 対局結果は別のテーブル（期限なし）に、キー id = 対局ID#何局目 で残す。
/// プレイヤーのテーブル（期限なし、ステップ 5-2）: キー id = ユーザー ID、key = 行の種類
///   profile            表示名（name。5-4 のマイページで決める）
///   g#ゲーム           そのゲームの戦績（rating・games・wins・losses・draws・rated・ver）。
///                      rankGame（= ゲーム）と rating で、索引 ranking からレートの高い順に読める
/// </summary>
public sealed class DynamoMatchStore : IMatchStore
{
    static readonly TimeSpan MatchTtl = TimeSpan.FromDays(1);
    static readonly TimeSpan QueueTtl = TimeSpan.FromHours(1);
    const string ProfileKey = "profile";
    const string GameKeyPrefix = "g#";

    readonly IAmazonDynamoDB _db;
    readonly string _table;
    readonly string _resultsTable;
    readonly string _playersTable;

    public DynamoMatchStore(IAmazonDynamoDB db, string table, string resultsTable, string playersTable)
    {
        _db = db;
        _table = table;
        _resultsTable = resultsTable;
        _playersTable = playersTable;
    }

    // ---------- 対局 ----------

    public async Task<MatchRecord?> GetMatchAsync(string matchId)
    {
        var item = await GetAsync("m#" + matchId);
        if (item == null) return null;
        var match = new MatchRecord
        {
            Id = matchId,
            Game = item["game"].S,
            Code = OptionalString(item, "code"),
            Started = item.TryGetValue("started", out var started) && started.BOOL == true,
            Seed = int.Parse(item["seed"].N, CultureInfo.InvariantCulture),
            Version = long.Parse(item["ver"].N, CultureInfo.InvariantCulture),
            TurnStartedAt = item.TryGetValue("ts", out var ts) ? long.Parse(ts.N, CultureInfo.InvariantCulture) : 0,
            GameNo = item.TryGetValue("gn", out var gn) ? int.Parse(gn.N, CultureInfo.InvariantCulture) : 0,
        };
        match.Connections[0] = OptionalString(item, "c0");
        match.Connections[1] = OptionalString(item, "c1");
        if (item.TryGetValue("moves", out var moves) && moves.L != null) match.Moves.AddRange(moves.L.Select(m => m.S));
        var rematch = OptionalString(item, "rm") ?? "00";
        match.Rematch[0] = rematch[0] == '1';
        match.Rematch[1] = rematch[1] == '1';
        for (int seat = 0; seat < 2; seat++)
        {
            match.Tokens[seat] = OptionalString(item, "k" + seat);
            match.Users[seat] = OptionalString(item, "u" + seat);
            if (item.TryGetValue("a" + seat, out var away)) match.AwaySince[seat] = long.Parse(away.N, CultureInfo.InvariantCulture);
        }
        return match;
    }

    public async Task<bool> TryPutMatchAsync(MatchRecord match)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = S("m#" + match.Id),
            ["game"] = S(match.Game),
            ["started"] = new AttributeValue { BOOL = match.Started },
            ["seed"] = N(match.Seed),
            ["moves"] = new AttributeValue { L = match.Moves.Select(S).ToList() },
            ["rm"] = S((match.Rematch[0] ? "1" : "0") + (match.Rematch[1] ? "1" : "0")),
            ["ver"] = N(match.Version + 1),
            ["ts"] = N(match.TurnStartedAt),
            ["gn"] = N(match.GameNo),
            ["ttl"] = Ttl(MatchTtl),
        };
        if (match.Connections[0] != null) item["c0"] = S(match.Connections[0]!);
        if (match.Connections[1] != null) item["c1"] = S(match.Connections[1]!);
        if (match.Code != null) item["code"] = S(match.Code);
        for (int seat = 0; seat < 2; seat++)
        {
            if (match.Tokens[seat] != null) item["k" + seat] = S(match.Tokens[seat]!);
            if (match.Users[seat] != null) item["u" + seat] = S(match.Users[seat]!);
            if (match.AwaySince[seat] is long away) item["a" + seat] = N(away);
        }

        var request = new PutItemRequest { TableName = _table, Item = item };
        if (match.Version == 0)
        {
            request.ConditionExpression = "attribute_not_exists(pk)";
        }
        else
        {
            request.ConditionExpression = "ver = :v";
            request.ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = N(match.Version) };
        }
        if (!await TryAsync(() => _db.PutItemAsync(request))) return false;
        match.Version++;
        return true;
    }

    public Task DeleteMatchAsync(string matchId) => DeleteAsync("m#" + matchId);

    // ---------- 接続 ----------

    public async Task<ConnectionRecord?> GetConnectionAsync(string connectionId)
    {
        var item = await GetAsync("c#" + connectionId);
        return item == null
            ? null
            : new ConnectionRecord(connectionId, item["mid"].S, int.Parse(item["seat"].N, CultureInfo.InvariantCulture));
    }

    public Task PutConnectionAsync(ConnectionRecord connection) =>
        _db.PutItemAsync(new PutItemRequest
        {
            TableName = _table,
            Item = new Dictionary<string, AttributeValue>
            {
                ["pk"] = S("c#" + connection.ConnectionId),
                ["mid"] = S(connection.MatchId),
                ["seat"] = N(connection.Seat),
                ["ttl"] = Ttl(MatchTtl),
            },
        });

    public Task DeleteConnectionAsync(string connectionId) => DeleteAsync("c#" + connectionId);

    // ---------- 待ち行列 ----------

    public Task<bool> TryEnqueueAsync(string game, string matchId) => TryPutIfAbsentAsync("q#" + game, matchId, QueueTtl);

    public async Task<string?> TryDequeueAsync(string game)
    {
        // 消すと同時に消す前の値を受け取る: 同時に 2 人が取りに来ても、受け取れるのは 1 人だけ
        var response = await _db.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _table,
            Key = Key("q#" + game),
            ReturnValues = ReturnValue.ALL_OLD,
        });
        return response.Attributes != null && response.Attributes.TryGetValue("mid", out var mid) ? mid.S : null;
    }

    public Task RemoveFromQueueAsync(string game, string matchId) => DeleteIfAsync("q#" + game, matchId);

    // ---------- ルームコード ----------

    public Task<bool> TryReserveCodeAsync(string game, string code, string matchId) =>
        TryPutIfAbsentAsync($"r#{game}#{code}", matchId, MatchTtl);

    public async Task<string?> FindCodeAsync(string game, string code) => OptionalString(await GetAsync($"r#{game}#{code}"), "mid");

    public Task ReleaseCodeAsync(string game, string code, string matchId) => DeleteIfAsync($"r#{game}#{code}", matchId);

    // ---------- 合言葉 ----------

    public Task PutTokenAsync(string token, string matchId, int seat) =>
        _db.PutItemAsync(new PutItemRequest
        {
            TableName = _table,
            Item = new Dictionary<string, AttributeValue>
            {
                ["pk"] = S("k#" + token),
                ["mid"] = S(matchId),
                ["seat"] = N(seat),
                ["ttl"] = Ttl(MatchTtl),
            },
        });

    public async Task<(string MatchId, int Seat)?> FindTokenAsync(string token)
    {
        var item = await GetAsync("k#" + token);
        return item == null ? null : (item["mid"].S, int.Parse(item["seat"].N, CultureInfo.InvariantCulture));
    }

    public Task DeleteTokenAsync(string token) => DeleteAsync("k#" + token);

    // ---------- 対局結果と戦績 ----------

    public async Task<SaveOutcome> TrySaveResultAsync(MatchResult result, IReadOnlyList<PlayerStats> stats)
    {
        var resultItem = new Dictionary<string, AttributeValue>
        {
            ["id"] = S(result.Id),
            ["game"] = S(result.Game),
            ["endedAt"] = N(result.EndedAt),
            ["winner"] = N(result.Winner),
            ["reason"] = S(result.Reason),
            ["plies"] = N(result.Plies),
            ["moves"] = new AttributeValue { L = result.Moves.Select(S).ToList() },
            ["seed"] = N(result.Seed),
        };
        for (int seat = 0; seat < 2; seat++)
        {
            if (result.Users?[seat] is { } user) resultItem["u" + seat] = S(user);
            if (result.RatingsAfter != null) resultItem["ra" + seat] = N(result.RatingsAfter[seat]);
            if (result.RatingDeltas != null) resultItem["rd" + seat] = N(result.RatingDeltas[seat]);
        }

        // 1 つ目は結果（同じ対局は 1 回だけ）、2 つ目からは戦績（読んだときの版のままなら書く）。全部書けるか、何も書かないか
        var items = new List<TransactWriteItem>
        {
            new() { Put = new Put { TableName = _resultsTable, Item = resultItem, ConditionExpression = "attribute_not_exists(id)" } },
        };
        foreach (var s in stats)
        {
            var put = new Put
            {
                TableName = _playersTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["id"] = S(s.UserId),
                    ["key"] = S(GameKeyPrefix + s.Game),
                    ["game"] = S(s.Game),
                    ["rankGame"] = S(s.Game),
                    ["rating"] = N(s.Rating),
                    ["games"] = N(s.Games),
                    ["wins"] = N(s.Wins),
                    ["losses"] = N(s.Losses),
                    ["draws"] = N(s.Draws),
                    ["rated"] = N(s.RatedGames),
                    ["updatedAt"] = N(result.EndedAt),
                    ["ver"] = N(s.Version + 1),
                },
            };
            if (s.Version == 0)
            {
                put.ConditionExpression = "attribute_not_exists(id)";
            }
            else
            {
                put.ConditionExpression = "ver = :v";
                put.ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = N(s.Version) };
            }
            items.Add(new TransactWriteItem { Put = put });
        }

        try
        {
            await _db.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = items });
            return SaveOutcome.Saved;
        }
        catch (TransactionCanceledException e)
        {
            // 理由は書き込みの順に並ぶ。結果の行がもうあれば「保存済み」、それ以外は戦績の競合としてやり直す
            var reasons = e.CancellationReasons;
            if (reasons is { Count: > 0 } && reasons[0].Code == "ConditionalCheckFailed") return SaveOutcome.AlreadySaved;
            return SaveOutcome.Conflict;
        }
    }

    // ---------- ログイン ----------

    public Task PutAuthAsync(string connectionId, string userId, TimeSpan ttl) =>
        _db.PutItemAsync(new PutItemRequest
        {
            TableName = _table,
            Item = new Dictionary<string, AttributeValue>
            {
                ["pk"] = S("u#" + connectionId),
                ["uid"] = S(userId),
                ["ttl"] = Ttl(ttl),
            },
        });

    public async Task<string?> GetAuthAsync(string connectionId) => OptionalString(await GetAsync("u#" + connectionId), "uid");

    public async Task<PlayerProfile> GetPlayerAsync(string userId)
    {
        // 1 人の行は profile と、遊んだゲームの数（最大 6）だけなので、1 回の Query で全部読める
        var response = await _db.QueryAsync(new QueryRequest
        {
            TableName = _playersTable,
            KeyConditionExpression = "id = :u",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":u"] = S(userId) },
            ConsistentRead = true,
        });
        string? name = null;
        var stats = new Dictionary<string, PlayerStats>();
        foreach (var item in response.Items ?? new List<Dictionary<string, AttributeValue>>())
        {
            var key = item["key"].S;
            if (key == ProfileKey)
            {
                name = OptionalString(item, "name");
            }
            else if (key.StartsWith(GameKeyPrefix, StringComparison.Ordinal))
            {
                var game = key.Substring(GameKeyPrefix.Length);
                stats[game] = new PlayerStats(userId, game, Int(item, "rating"), Int(item, "games"), Int(item, "wins"),
                    Int(item, "losses"), Int(item, "draws"), Int(item, "rated"), Int(item, "ver"));
            }
        }
        return new PlayerProfile(userId, name, stats);
    }

    static int Int(Dictionary<string, AttributeValue> item, string name) =>
        item.TryGetValue(name, out var v) && v.N != null ? int.Parse(v.N, CultureInfo.InvariantCulture) : 0;

    // ---------- 内部 ----------

    async Task<Dictionary<string, AttributeValue>?> GetAsync(string pk)
    {
        var response = await _db.GetItemAsync(new GetItemRequest { TableName = _table, Key = Key(pk), ConsistentRead = true });
        if (response.Item is not { Count: > 0 } item) return null;
        // 期限切れでも、DynamoDB が実際に消すまでは読めてしまうので、ここで無かったことにする
        if (item.TryGetValue("ttl", out var ttl) &&
            long.Parse(ttl.N, CultureInfo.InvariantCulture) < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return null;
        return item;
    }

    Task DeleteAsync(string pk) => _db.DeleteItemAsync(new DeleteItemRequest { TableName = _table, Key = Key(pk) });

    /// <summary>行の mid が matchId のときだけ消す（ほかの対局のものは消さない）。</summary>
    Task DeleteIfAsync(string pk, string matchId) =>
        TryAsync(() => _db.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _table,
            Key = Key(pk),
            ConditionExpression = "mid = :m",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":m"] = S(matchId) },
        }));

    Task<bool> TryPutIfAbsentAsync(string pk, string matchId, TimeSpan ttl) =>
        TryAsync(() => _db.PutItemAsync(new PutItemRequest
        {
            TableName = _table,
            Item = new Dictionary<string, AttributeValue> { ["pk"] = S(pk), ["mid"] = S(matchId), ["ttl"] = Ttl(ttl) },
            ConditionExpression = "attribute_not_exists(pk)",
        }));

    /// <summary>条件付きの書き込み。条件に合わなかったら false（ほかの書き込みが先に入った）。</summary>
    static async Task<bool> TryAsync(Func<Task> write)
    {
        try
        {
            await write();
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    static Dictionary<string, AttributeValue> Key(string pk) => new() { ["pk"] = S(pk) };
    static AttributeValue S(string value) => new() { S = value };
    static AttributeValue N(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };
    static AttributeValue Ttl(TimeSpan span) => N(DateTimeOffset.UtcNow.Add(span).ToUnixTimeSeconds());

    static string? OptionalString(Dictionary<string, AttributeValue>? item, string name) =>
        item != null && item.TryGetValue(name, out var value) ? value.S : null;
}

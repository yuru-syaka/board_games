using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using BoardGames.Server.Api;

namespace BoardGames.Server.Aws;

/// <summary>
/// プレイヤーのテーブル（ステップ 5-2・5-4）。キー id = ユーザー ID、key = 行の種類。
///   profile                       表示名（name）
///   g#ゲーム                      そのゲームの戦績。rankGame ＋ rating の索引 ranking でレートの高い順に読める
///   r#終わった時刻#結果のID       その人が遊んだ対局結果へのつながり（rid・seat・game・endedAt）。
///                                 アカウントを消すとき、結果の行からその人の ID を取り除くのに使う（「最近の対局」にも使える）
/// </summary>
public sealed class DynamoPlayerStore : IPlayerStore
{
    public const string ProfileKey = "profile";
    public const string GameKeyPrefix = "g#";
    public const string ResultKeyPrefix = "r#";
    const string RankingIndex = "ranking";

    readonly IAmazonDynamoDB _db;
    readonly string _playersTable;
    readonly string _resultsTable;

    public DynamoPlayerStore(IAmazonDynamoDB db, string playersTable, string resultsTable)
    {
        _db = db;
        _playersTable = playersTable;
        _resultsTable = resultsTable;
    }

    /// <summary>結果へのつながりの行のキー（新しいものほど後ろに並ぶ）。</summary>
    public static string ResultLinkKey(long endedAt, string resultId) =>
        ResultKeyPrefix + endedAt.ToString("D13", CultureInfo.InvariantCulture) + "#" + resultId;

    // ---------- 読む ----------

    public Task<PlayerProfile> GetPlayerAsync(string userId) => LoadProfileAsync(_db, _playersTable, userId);

    /// <summary>表示名と戦績を読む（結果へのつながりの行は読まない: key が g# 〜 profile の範囲だけ）。</summary>
    public static async Task<PlayerProfile> LoadProfileAsync(IAmazonDynamoDB db, string table, string userId)
    {
        var response = await db.QueryAsync(new QueryRequest
        {
            TableName = table,
            KeyConditionExpression = "id = :u AND #k BETWEEN :from AND :to",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#k"] = "key" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":u"] = S(userId),
                [":from"] = S(GameKeyPrefix),
                [":to"] = S(ProfileKey),
            },
            ConsistentRead = true,
        });
        string? name = null;
        var stats = new Dictionary<string, PlayerStats>();
        foreach (var item in response.Items ?? new List<Dictionary<string, AttributeValue>>())
        {
            var key = item["key"].S;
            if (key == ProfileKey)
            {
                name = item.TryGetValue("name", out var n) ? n.S : null;
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

    public async Task<IReadOnlyList<RankingEntry>> GetRankingAsync(string game, int size, int minRatedGames)
    {
        var rows = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? start = null;
        // 条件（レート戦の数）で外れる人がいるので、足りなければ続きを読む（読みすぎないよう 5 回まで）
        for (int page = 0; page < 5 && rows.Count < size; page++)
        {
            var response = await _db.QueryAsync(new QueryRequest
            {
                TableName = _playersTable,
                IndexName = RankingIndex,
                KeyConditionExpression = "rankGame = :g",
                FilterExpression = "rated >= :min",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":g"] = S(game),
                    [":min"] = N(minRatedGames),
                },
                ScanIndexForward = false, // レートの高い順
                Limit = size * 2,
                ExclusiveStartKey = start,
            });
            rows.AddRange(response.Items ?? new List<Dictionary<string, AttributeValue>>());
            start = response.LastEvaluatedKey;
            if (start == null || start.Count == 0) break;
        }
        if (rows.Count > size) rows.RemoveRange(size, rows.Count - size);

        var names = await LoadNamesAsync(rows.Select(r => r["id"].S).Distinct().ToList());
        return rows.Select(r => new RankingEntry(r["id"].S, names.GetValueOrDefault(r["id"].S), Int(r, "rating"), Int(r, "rated"),
            Int(r, "wins"), Int(r, "losses"), Int(r, "draws"))).ToList();
    }

    /// <summary>表示名をまとめて読む（BatchGetItem は 1 回 100 件まで）。</summary>
    async Task<Dictionary<string, string>> LoadNamesAsync(IReadOnlyList<string> userIds)
    {
        var names = new Dictionary<string, string>();
        foreach (var chunk in userIds.Chunk(100))
        {
            var keys = chunk.Select(id => new Dictionary<string, AttributeValue> { ["id"] = S(id), ["key"] = S(ProfileKey) }).ToList();
            var request = new Dictionary<string, KeysAndAttributes>
            {
                [_playersTable] = new KeysAndAttributes { Keys = keys, ProjectionExpression = "id, #n", ExpressionAttributeNames = new() { ["#n"] = "name" } },
            };
            for (int attempt = 0; attempt < 5 && request.Count > 0; attempt++)
            {
                var response = await _db.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = request });
                if (response.Responses != null && response.Responses.TryGetValue(_playersTable, out var items))
                    foreach (var item in items)
                        if (item.TryGetValue("name", out var n) && n.S != null) names[item["id"].S] = n.S;
                request = response.UnprocessedKeys ?? new Dictionary<string, KeysAndAttributes>();
                if (request.Count > 0) await Task.Delay(50 << attempt);
            }
        }
        return names;
    }

    // ---------- 書く ----------

    public Task SetNameAsync(string userId, string name) =>
        _db.PutItemAsync(new PutItemRequest
        {
            TableName = _playersTable,
            Item = new Dictionary<string, AttributeValue>
            {
                ["id"] = S(userId),
                ["key"] = S(ProfileKey),
                ["name"] = S(name),
                ["updatedAt"] = N(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            },
        });

    public async Task DeleteAccountDataAsync(string userId)
    {
        // その人の行を全部読む（戦績・名前・結果へのつながり）
        var items = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? start = null;
        do
        {
            var response = await _db.QueryAsync(new QueryRequest
            {
                TableName = _playersTable,
                KeyConditionExpression = "id = :u",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":u"] = S(userId) },
                ExclusiveStartKey = start,
            });
            items.AddRange(response.Items ?? new List<Dictionary<string, AttributeValue>>());
            start = response.LastEvaluatedKey;
        } while (start is { Count: > 0 });

        // 対局結果の行から、その人の ID を取り除く（「誰かが遊んだ」結果としては残す）
        foreach (var link in items.Where(i => i["key"].S.StartsWith(ResultKeyPrefix, StringComparison.Ordinal)))
        {
            if (!link.TryGetValue("rid", out var rid) || !link.TryGetValue("seat", out var seatValue)) continue;
            var attr = "u" + seatValue.N;
            try
            {
                await _db.UpdateItemAsync(new UpdateItemRequest
                {
                    TableName = _resultsTable,
                    Key = new Dictionary<string, AttributeValue> { ["id"] = rid },
                    UpdateExpression = "REMOVE " + attr,
                    ConditionExpression = attr + " = :u",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":u"] = S(userId) },
                });
            }
            catch (ConditionalCheckFailedException)
            {
                // もう取り除かれている
            }
        }

        // その人の行を消す（BatchWriteItem は 1 回 25 件まで。残った分はやり直す）
        foreach (var chunk in items.Chunk(25))
        {
            var request = new Dictionary<string, List<WriteRequest>>
            {
                [_playersTable] = chunk.Select(i => new WriteRequest
                {
                    DeleteRequest = new DeleteRequest { Key = new Dictionary<string, AttributeValue> { ["id"] = i["id"], ["key"] = i["key"] } },
                }).ToList(),
            };
            for (int attempt = 0; attempt < 5 && request.Count > 0; attempt++)
            {
                var response = await _db.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = request });
                request = response.UnprocessedItems ?? new Dictionary<string, List<WriteRequest>>();
                if (request.Count > 0) await Task.Delay(50 << attempt);
            }
            if (request.Count > 0) throw new InvalidOperationException("could not delete all player items");
        }
    }

    // ---------- 内部 ----------

    static AttributeValue S(string value) => new() { S = value };
    static AttributeValue N(long value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };

    static int Int(Dictionary<string, AttributeValue> item, string name) =>
        item.TryGetValue(name, out var v) && v.N != null ? int.Parse(v.N, CultureInfo.InvariantCulture) : 0;
}

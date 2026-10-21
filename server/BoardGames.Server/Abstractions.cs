namespace BoardGames.Server;

/// <summary>
/// 1 つの対局（部屋）。DynamoDB には 1 行で保存する。
/// 局面そのものは保存せず、「乱数の種 ＋ 手の一覧」から毎回作り直す（ゲームごとに保存の仕組みを作らないため）。
/// </summary>
public sealed class MatchRecord
{
    public required string Id { get; init; }
    public required string Game { get; init; }
    /// <summary>席 0 / 1 の接続 ID（いなければ null）。</summary>
    public string?[] Connections { get; init; } = new string?[2];
    /// <summary>ルームコード（ランダムマッチなら null）。</summary>
    public string? Code { get; set; }
    /// <summary>2 人そろって対局が始まっているか。</summary>
    public bool Started { get; set; }
    public int Seed { get; set; }
    /// <summary>これまでの手。"席:手のメッセージ"（例: "0:M|1|4"）。</summary>
    public List<string> Moves { get; init; } = new();
    /// <summary>終局後に再戦を希望しているか（席ごと）。</summary>
    public bool[] Rematch { get; init; } = new bool[2];
    /// <summary>
    /// 書き込みの版番号。読んだときの版のまま書き込めたときだけ成功する（同時に 2 つの手が届いても壊れないように）。
    /// 0 = まだ保存されていない。
    /// </summary>
    public long Version { get; set; }

    public int OtherSeat(int seat) => 1 - seat;
}

/// <summary>接続ごとの「どの対局のどの席にいるか」。対局に入っていない接続は保存しない。</summary>
public sealed record ConnectionRecord(string ConnectionId, string MatchId, int Seat);

/// <summary>対局・接続・待ち行列・ルームコードの保存先（本番は DynamoDB、テストはメモリ）。</summary>
public interface IMatchStore
{
    Task<MatchRecord?> GetMatchAsync(string matchId);

    /// <summary>
    /// 版番号を確かめて保存する（match.Version が 0 なら新規）。成功したら match.Version を 1 増やして true。
    /// ほかの書き込みが先に入っていたら false（呼んだ側で読み直してやり直す）。
    /// </summary>
    Task<bool> TryPutMatchAsync(MatchRecord match);

    Task DeleteMatchAsync(string matchId);

    Task<ConnectionRecord?> GetConnectionAsync(string connectionId);
    Task PutConnectionAsync(ConnectionRecord connection);
    Task DeleteConnectionAsync(string connectionId);

    /// <summary>ランダムマッチの待ち行列に、まだ誰もいなければ自分の対局を置く。</summary>
    Task<bool> TryEnqueueAsync(string game, string matchId);

    /// <summary>待ち行列の対局を取り出す（取り出した人だけが受け取る）。いなければ null。</summary>
    Task<string?> TryDequeueAsync(string game);

    /// <summary>待ち行列にあるのがこの対局なら消す。</summary>
    Task RemoveFromQueueAsync(string game, string matchId);

    /// <summary>ルームコードを、まだ使われていなければ予約する。</summary>
    Task<bool> TryReserveCodeAsync(string game, string code, string matchId);

    Task<string?> FindCodeAsync(string game, string code);

    /// <summary>ルームコードがこの対局のものなら消す。</summary>
    Task ReleaseCodeAsync(string game, string code, string matchId);
}

/// <summary>接続にメッセージを送る（本番は API Gateway の管理 API）。</summary>
public interface IConnectionPush
{
    /// <summary>送れたら true。相手の接続がもう無ければ false。</summary>
    Task<bool> SendAsync(string connectionId, string message);
}

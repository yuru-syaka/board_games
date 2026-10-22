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
    /// <summary>席ごとの合言葉。通信が切れたあと、これを持っている人だけが同じ席に戻れる。</summary>
    public string?[] Tokens { get; init; } = new string?[2];
    /// <summary>席ごとの「通信が切れた時刻」（UNIX ミリ秒。いれば null）。待ち時間を過ぎたら相手が勝ちを申請できる。</summary>
    public long?[] AwaySince { get; init; } = new long?[2];
    /// <summary>今の手番が始まった時刻（UNIX ミリ秒）。1 手の制限時間を過ぎたかを確かめるため。</summary>
    public long TurnStartedAt { get; set; }
    /// <summary>この部屋で何局目か（再戦するたびに 1 増える。最初の対局は 1）。結果の行の名前に使う。</summary>
    public int GameNo { get; set; }

    public int OtherSeat(int seat) => 1 - seat;
}

/// <summary>対局結果の終わり方。</summary>
public static class ResultReasons
{
    /// <summary>ルールどおりに勝敗がついた。</summary>
    public const string Normal = "normal";
    /// <summary>ルールどおりに引き分けになった。</summary>
    public const string Draw = "draw";
    /// <summary>手番の側が制限時間を過ぎ、相手が勝ちを申請した。</summary>
    public const string Timeout = "timeout";
    /// <summary>通信が切れた側が待ち時間のうちに戻らず、相手が勝ちを申請した。</summary>
    public const string Abandoned = "abandoned";
    /// <summary>対局の途中で、自分で部屋から抜けた（相手の勝ち）。</summary>
    public const string Left = "left";
}

/// <summary>
/// 1 局の結果（ステップ 4-4）。期限なしで残し、ステップ 5 のレーティング・ランキングの元にする。
/// 「誰が」はまだ残さない（ログインはステップ 5）。棋譜（Moves）と乱数の種（Seed）から対局をそのまま再現できる。
/// </summary>
public sealed record MatchResult(
    string MatchId,
    int GameNo,
    string Game,
    long EndedAt,
    // 勝った席（0 か 1）。引き分けは -1
    int Winner,
    string Reason,
    int Plies,
    IReadOnlyList<string> Moves,
    int Seed)
{
    /// <summary>結果の行の名前（同じ対局の結果を 2 回書かないため）。</summary>
    public string Id => MatchId + "#" + GameNo;
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

    /// <summary>合言葉 → 対局と席、を覚える（Resume で探すため）。</summary>
    Task PutTokenAsync(string token, string matchId, int seat);

    Task<(string MatchId, int Seat)?> FindTokenAsync(string token);

    Task DeleteTokenAsync(string token);

    /// <summary>対局結果を残す。同じ結果（MatchResult.Id）がもう残っていれば何もしないで false。</summary>
    Task<bool> TryPutResultAsync(MatchResult result);
}

/// <summary>接続にメッセージを送る（本番は API Gateway の管理 API）。</summary>
public interface IConnectionPush
{
    /// <summary>送れたら true。相手の接続がもう無ければ false。</summary>
    Task<bool> SendAsync(string connectionId, string message);
}

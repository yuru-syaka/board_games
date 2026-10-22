namespace BoardGames.Server;

/// <summary>対戦サーバーの時間の設定。変えるときはここだけを直す。</summary>
public sealed class ServerSettings
{
    /// <summary>通信が切れた相手を待つ時間。過ぎたら、残った側が勝ちを申請できる。</summary>
    public TimeSpan AwayGrace { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>1 手の制限時間。過ぎたら、待っている側が勝ちを申請できる（申請される前に手が届けば受け付ける）。</summary>
    public TimeSpan TurnLimit { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>ゲームごとの 1 手の制限時間（TurnLimit と違うものだけ）。</summary>
    public IReadOnlyDictionary<string, TimeSpan> TurnLimitByGame { get; init; } = new Dictionary<string, TimeSpan>
    {
        // 手の中身（術の回数・昇格）が多く、考える時間がかかる
        ["centuryghost"] = TimeSpan.FromSeconds(180),
    };

    /// <summary>レーティングの初期値（ステップ 5-2）。</summary>
    public int InitialRating { get; init; } = 1500;

    /// <summary>Elo の K（1 局で動く大きさの上限。同じレート同士なら ±K/2）。</summary>
    public int RatingK { get; init; } = 32;

    /// <summary>ログインした接続を覚えておく時間（WebSocket は最長 2 時間で切れるので、それより長く）。</summary>
    public TimeSpan AuthTtl { get; init; } = TimeSpan.FromHours(3);

    public TimeSpan TurnLimitFor(string game) => TurnLimitByGame.TryGetValue(game, out var limit) ? limit : TurnLimit;

    public static ServerSettings Default { get; } = new();
}

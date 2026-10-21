namespace BoardGames.Server;

/// <summary>対戦サーバーの時間の設定。変えるときはここだけを直す。</summary>
public sealed class ServerSettings
{
    /// <summary>通信が切れた相手を待つ時間。過ぎたら、残った側が勝ちを申請できる。</summary>
    public TimeSpan AwayGrace { get; init; } = TimeSpan.FromSeconds(90);

    public static ServerSettings Default { get; } = new();
}

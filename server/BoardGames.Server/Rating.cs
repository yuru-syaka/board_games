namespace BoardGames.Server;

/// <summary>
/// Elo レーティング（ステップ 5-2）。期待勝率 = 1 / (1 + 10^((相手 − 自分) / 400))、変化 = K × (結果 − 期待勝率)。
/// 結果は勝ち 1、引き分け 0.5、負け 0。2 人の増減は必ず同じ大きさ・逆向きにする（合計が増えも減りもしない）。
/// </summary>
public static class Elo
{
    /// <summary>席 0 の得点（scoreA: 1 / 0.5 / 0）から、2 人の増減を求める。</summary>
    public static (int DeltaA, int DeltaB) Deltas(int ratingA, int ratingB, double scoreA, int k)
    {
        double expectedA = 1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));
        int deltaA = (int)Math.Round(k * (scoreA - expectedA), MidpointRounding.AwayFromZero);
        return (deltaA, -deltaA);
    }
}

/// <summary>1 人の 1 ゲームの戦績（プレイヤーのテーブルの 1 行）。まだ遊んでいなければ Version = 0。</summary>
public sealed record PlayerStats(
    string UserId,
    string Game,
    int Rating,
    // ログインして遊んだ対局（ルームコードを含む）
    int Games,
    int Wins,
    int Losses,
    int Draws,
    // レートが動いた対局（両者ログイン中のランダムマッチ）
    int RatedGames,
    long Version)
{
    public static PlayerStats New(string userId, string game, int initialRating) =>
        new(userId, game, initialRating, 0, 0, 0, 0, 0, 0);
}

/// <summary>プレイヤー（表示名と、ゲームごとの戦績）。</summary>
public sealed record PlayerProfile(string UserId, string? Name, IReadOnlyDictionary<string, PlayerStats> Stats);

/// <summary>結果と戦績の保存の結果。</summary>
public enum SaveOutcome
{
    /// <summary>結果も戦績も書けた。</summary>
    Saved,
    /// <summary>同じ対局の結果がもう残っていた（何も書いていない）。</summary>
    AlreadySaved,
    /// <summary>戦績がほかの書き込みで変わっていた（何も書いていない。読み直してやり直す）。</summary>
    Conflict,
}

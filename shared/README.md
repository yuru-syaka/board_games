# shared/ — Unity とサーバーで共有するゲームのルール

7 本のゲームのルール（Logic）と共通部品（Core）の **正本**。
Unity のゲームと、オンライン対戦のサーバー（AWS Lambda、C#）の両方が、ここにある同じ `.cs` を使う。
ルールを直すときは、ここだけを直す（各 Unity プロジェクトにはコピーを置かない）。

## 中身

```
shared/
  unity/                          Unity のパッケージ（UPM）。.cs の正本はここ
    com.boardpia.core/            共通部品（GameResult、IGameRules、ICpuPlayer）
    com.boardpia.<game>/          ゲームごと
      Runtime/                    ルール・局面・手・CPU・通信の形式（Online/）
      Tests/                      EditMode テスト（NUnit）
  dotnet/                         同じ .cs を .NET でビルド・テストするためのプロジェクト
    BoardGames.Shared.slnx
    Directory.Build.props         C# 9（Unity と同じ）、.cs は shared/unity/ から読む
    BoardGames.Core/              netstandard2.1
    BoardGames.<Game>.Logic/      netstandard2.1
    BoardGames.<Game>.Tests/      net10.0、NUnit 3（Unity と同じ書き方のテストを動かす）
```

| パッケージ | ゲーム | Unity プロジェクト |
|---|---|---|
| `com.boardpia.vanish` | ゴースト・マルバツゲーム | `ghost_tic_tac_toe` |
| `com.boardpia.gobblet` | ゴースト・ゴブル | `Gobblet_Gobblers` |
| `com.boardpia.quarto` | ゴースト・クアルト | `Quarto` |
| `com.boardpia.geister` | ゴースト・ブラフ | `Geister` |
| `com.boardpia.battleline` | ゴースト・ライン | `ButtleLine` |
| `com.boardpia.centuryghost` | センチュリー・ゴースト | `CenturyGolem` |
| `com.boardpia.ghostreversa` | ゴースト・リバーサ | `Anomie` |

Unity プロジェクトは `C:\Users\nullg\createGame\boardGames\<プロジェクト>`（このリポジトリの外）にある。

## Unity からの読み込み

各プロジェクトの `Packages/manifest.json` に、ローカルのパッケージとして書いている。

```json
"com.boardpia.core": "file:C:/Users/nullg/Desktop/board_games/shared/unity/com.boardpia.core",
"com.boardpia.vanish": "file:C:/Users/nullg/Desktop/board_games/shared/unity/com.boardpia.vanish",
...
"testables": ["com.boardpia.vanish"]
```

- `testables` に入れると、パッケージの中のテストが Unity の Test Runner に出る。
- アセンブリ名（`BoardGames.Core`、`BoardGames.<Game>.Logic`）は移す前と同じなので、
  View などの参照はそのまま動く。
- Unity エディターでパッケージの `.cs` を直すと、このフォルダのファイルが直接書き換わる。

## 決まりごと

- **Unity に依存しない C# だけを書く**（`noEngineReferences: true`。サーバーでも動かすため）。
- **C# 9 まで**の書き方にする（Unity 6 の C# のバージョン）。`dotnet build` も C# 9 でビルドするので、
  新しい書き方が混ざるとここでエラーになる。
- `.meta` は Unity が作るもの。手で書き換えない（コミットはする）。
- ルールや Core を直したら、両方のテストを通す。

## テストの実行

```bash
# .NET（速い。数秒）
dotnet test shared/dotnet/BoardGames.Shared.slnx

# Unity（各プロジェクト。1 本 1 分ほど）
unity test C:/Users/nullg/createGame/boardGames/ghost_tic_tac_toe --mode EditMode
```

2026-10-21 時点: 6 本で合計 283 件（Vanish 41、Gobblet 48、Quarto 43、Geister 43、BattleLine 69、CenturyGhost 39）。
Unity と .NET のどちらでも、すべて通る。
2026-10-24: ゴースト・リバーサ（GhostReversa）49 件を追加。Unity と .NET のどちらでも通る。

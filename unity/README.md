# unity/

各ゲームの Unity プロジェクト（`C:\Users\nullg\createGame\boardGames\<project>`）で共通して使うファイルの正本です。
変更したら、各プロジェクトへコピーしてください。

| このリポジトリ | コピー先（各 Unity プロジェクト） |
|---|---|
| `WebGLTemplates/BoardGames/` | `Assets/WebGLTemplates/BoardGames/` |
| `Editor/WebBuild.cs` | `Assets/Editor/WebBuild.cs` |

ゲームごとの値は、各プロジェクトの `Assets/Editor/WebBuildSettings.json` に書きます。

| Unity プロジェクト | ゲーム名（title） | slug（URL と出力先） | 紹介ページ（page） |
|---|---|---|---|
| `ghost_tic_tac_toe` | ゴースト・マルバツゲーム | `ghost-tic-tac-toe` | `ghost-tic-tac-toe.html` |
| `Gobblet_Gobblers` | ゴースト・ゴブル | `ghost-gobble` | `ghost-gobble.html` |
| `Geister` | ゴースト・ブラフ | `ghost-bluff` | `ghost-bluff.html` |
| `Quarto` | ゴースト・クアルト | `ghost-quarto` | `ghost-quarto.html` |
| `ButtleLine` | ゴースト・ライン | `ghost-line` | `ghost-line.html` |
| `CenturyGolem` | センチュリー・ゴースト | `century-ghost` | `century-ghost.html` |
| `Anomie` | ゴースト・リバーサ | `ghost-reversa` | `ghost-reversa.html`（紹介ページはまだ無い） |
| `MODEAN ART` | ゴースト・トレンド | `ghost-trend` | `ghost-trend.html` |

```json
{ "title": "ゴースト・マルバツゲーム", "page": "ghost-tic-tac-toe.html" }
```

## ビルドと公開

1. Unity Editor でそのプロジェクトを開いていない状態で、WebGL ビルドを `web/play/<slug>/` に出力します。

```bash
unity build C:\Users\nullg\createGame\boardGames\ghost_tic_tac_toe --target WebGL --execute-method WebBuild.Build --output-path C:\Users\nullg\Desktop\board_games\web\play\ghost-tic-tac-toe
```

2. `infra/` で PlayStack をデプロイすると、`web/play/` にあるゲームが S3 にアップロードされます（GitHub Actions からは更新されません）。

```bash
cdk deploy PlayStack --exclusively --profile board-game-pia-admin
```

- `--exclusively` を付けて、依存する WebStack・AuthStack には触れません（AuthStack は Google ログインのシークレットが要るので GitHub Actions からだけ出します。付け忘れると、シークレットがないというエラーで止まります）。
- `web/play/` は git の管理対象外です（1 本約 20MB のため）。ビルドは S3 にだけ置きます。
- 手元に `web/play/<slug>/` がないゲームは、デプロイしても S3 から消えません（そのゲームはアップロードされないだけ）。
- 同じゲームを再ビルドしてデプロイすると、S3 の `play/<slug>/` の中身は新しいビルドに置き換わります（古いファイルは削除）。

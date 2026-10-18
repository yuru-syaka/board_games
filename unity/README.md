# unity/

各ゲームの Unity プロジェクト（`C:\Users\nullg\createGame\boardGames\<project>`）で共通して使うファイルの正本です。
変更したら、各プロジェクトへコピーしてください。

| このリポジトリ | コピー先（各 Unity プロジェクト） |
|---|---|
| `WebGLTemplates/BoardGames/` | `Assets/WebGLTemplates/BoardGames/` |
| `Editor/WebBuild.cs` | `Assets/Editor/WebBuild.cs` |

ゲームごとの値は、各プロジェクトの `Assets/Editor/WebBuildSettings.json` に書きます。

```json
{ "title": "ゴースト・マルバツゲーム", "page": "ghost-tic-tac-toe.html" }
```

## ビルド

Unity Editor でそのプロジェクトを開いていない状態で実行します。出力先は `web/play/<slug>/` です。

```bash
unity build C:\Users\nullg\createGame\boardGames\ghost_tic_tac_toe --target WebGL --execute-method WebBuild.Build --output-path C:\Users\nullg\Desktop\board_games\web\play\ghost-tic-tac-toe
```

ビルド後に `web/` を push すると GitHub Pages に、`infra/` で `cdk deploy WebStack` すると CloudFront に反映されます。

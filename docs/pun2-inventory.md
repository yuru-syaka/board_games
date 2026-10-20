# PUN2 棚卸し（2026-10-18、センチュリー・ゴーストを 2026-10-21 に追加）

対象: `C:\Users\nullg\createGame\boardGames` 配下の Unity プロジェクト 6 本（Unity 6000.5.8f1）。
Photon パッケージ本体（`Assets/Photon/`）は除外し、自作コードだけを調べた。

## 1. 結論

- **PUN2 を直接呼んでいるのは各ゲーム 1 ファイル（`Online.Photon/PhotonOnlineService.cs`）だけ。**
  6 本とも中身は同じで、違いは定数 3 つ（GameVersion、ルームコードの接頭辞、コメント）と名前空間のみ。
- ゲーム側は `IOnlineService` / `IMatchTransport` というインターフェース越しに通信しており、
  **`PhotonOnlineService` を AWS 用の実装に差し替えれば、ゲーム本体はほぼ無改修で移行できる。**
- ルール（`Logic/`）と共通部品（`Core/Scripts/`）は `noEngineReferences: true` の **純粋な C#**。
  **AWS 側のゲームサーバーを C#（.NET）で作れば、ルールと通信プロトコルのコードをそのまま再利用できる。**
- 使っている PUN2 の機能は最小限。`[PunRPC]`、`PhotonView`、`OnPhotonSerializeView`、
  `PhotonNetwork.Instantiate`、Player の `CustomProperties`、`LoadLevel` は **どのゲームでも未使用**。

## 2. ゲーム一覧

| プロジェクト | ゲーム（コード上の名前） | 自作スクリプト | 情報 | GameVersion | ルームコード接頭辞 |
|---|---|---|---|---|---|
| `ghost_tic_tac_toe` | ゴースト・マルバツ（Vanish） | 34 本 / 約 4,200 行 | 完全情報 | `ghostvanish-1` | `GV` |
| `Gobblet_Gobblers` | ゴブレットゴブラーズ（Gobblet） | 36 本 / 約 4,700 行 | 完全情報 | `ghostgobble-1` | `GG` |
| `Quarto` | クアルト（Quarto） | 39 本 / 約 4,900 行 | 完全情報 | `ghostquattro-1` | `GQ` |
| `Geister` | ガイスター（Geister） | 39 本 / 約 4,700 行 | **不完全情報**（相手の駒の種類が見えない） | `ghostbluff-1` | `GB` |
| `ButtleLine` | バトルライン（BattleLine） | 46 本 / 約 6,700 行 | **不完全情報**（相手の手札・山札の順番） | `neonline-1` | `NL` |
| `CenturyGolem` | センチュリー・ゴースト（CenturyGhost） | 41 本 / 約 5,900 行 | **不完全情報**（山札 2 つの順番のみ。手札は公開） | `centuryghost-1` | `CG` |

## 3. 共通のオンライン構成（6 本とも同じ）

```
View/<Game>Game.Online.cs        オンラインメニュー UI（ランダムマッチ / ルームを作る / ルームに入る）
   │ 使う
View/Online/IOnlineService.cs    接続・マッチング・ルーム・イベントのインターフェース
   ├─ Online.Photon/PhotonOnlineService.cs   ← PUN2 を呼ぶ唯一のファイル（ここを差し替える）
   └─ View/Online/LoopbackOnlineService.cs   Editor 専用。CPU ボット相手の動作確認用
Logic/Online/IMatchTransport.cs  文字列メッセージを相手に送る / 受け取るだけのインターフェース
Logic/Online/OnlineMatch.cs      ホスト権威型の対局進行（手の検証・局面の配信・再戦）
Logic/Online/<Game>NetProtocol.cs メッセージの文字列化（"種類|版|本体"）
```

`OnlineServices.Factory` に実装を登録する方式（`[RuntimeInitializeOnLoadMethod]`）なので、
AWS 版の実装を同じように登録すれば切り替わる。

## 4. PUN2 の使用箇所（`PhotonOnlineService.cs`）

### 4-1. 接続・ルーム（AWS 版ではサーバーとロビーに移る）

| PUN2 の機能 | 使い方 | AWS 版での置き換え |
|---|---|---|
| `ConnectUsingSettings` / `GameVersion` | Photon Cloud に接続。GameVersion でゲームごとに分離 | WebSocket で `wss://…/game` に接続。URL かメッセージでゲーム ID を指定 |
| `AutomaticallySyncScene = false` | シーン同期は使わない | 不要 |
| `JoinRandomRoom(m=r, 2人)` → 失敗時 `CreateRoom`（公開・2人） | ランダムマッチ。空きがなければ自分で部屋を作って待つ | サーバー側のマッチング待ち行列 |
| `CreateRoom("GV"+4桁, 非公開)`（衝突時 5 回まで再試行） | ルームコード方式の部屋作成 | サーバーが重複しないコードを発行 |
| `JoinRoom("GV"+コード)` | コードで入室 | サーバーにコードを送って入室 |
| `CurrentRoom.IsOpen/IsVisible = false`（2 人そろったら） | 3 人目を入れない | サーバー側で定員管理 |
| `IsMasterClient` | 入室時に「ホスト（P0）かどうか」を決めるだけ | サーバーが席（P0 / P1）を割り当てる |
| `LeaveRoom` / `Disconnect` | 退室・終了 | 同等のメッセージ / 切断 |

### 4-2. ゲーム中の通信

| PUN2 の機能 | 使い方 | AWS 版での置き換え |
|---|---|---|
| `RaiseEvent(code=1, string, Others, Reliable)` | **全ゲームの通信はこれ 1 本**。文字列を相手にだけ確実に送る | WebSocket のテキストメッセージ |
| `IOnEventCallback.OnEvent` | 受け取った文字列を `MessageReceived` へ | 同上 |

### 4-3. コールバック（イベント）

| PUN2 コールバック | ゲームへの通知 |
|---|---|
| `OnConnectedToMaster` | 状態 → Lobby |
| `OnJoinedRoom` | `JoinedRoom`（ホストは相手待ち、ゲストは対局開始待ち） |
| `OnPlayerEnteredRoom` | `OpponentJoined` → ホストが `StartNewGame()` |
| `OnPlayerLeftRoom` | `OpponentLeft` → **即「対戦終了」**（再接続の猶予なし） |
| `OnDisconnected` / `OnLeftRoom` | `Error`（日本語メッセージ） |
| `OnJoinRandomFailed` / `OnCreateRoomFailed` / `OnJoinRoomFailed` | 部屋作成・再試行・エラー表示 |

## 5. ゲームごとの通信メッセージ

形式はすべて `種類|版|本体`（版 = 1）。種類は 3 つだけ。

| 種類 | 向き | 内容 |
|---|---|---|
| `S`（Snapshot） | ホスト → ゲスト | 局面全体（ゲスト視点） |
| `M`（Move） | ゲスト → ホスト | 指したい手 |
| `R`（Rematch） | 双方向 | 再戦の希望 |

| ゲーム | Move の本体 | Snapshot の主な中身 | 隠し情報の扱い |
|---|---|---|---|
| Vanish | `cell` | 手番、手数、勝敗、直前の手、消えたマス、そろった列、両者の駒（古い順） | なし |
| Gobblet | `from,size,to` | 手番、手数、勝敗、直前の手、そろった列、9 マスの積み重ね | なし |
| Quarto | `kind(置く/渡す),x,y,piece` | 視点、手番、フェーズ、渡された駒、16 マスの駒 | なし |
| Geister | `id,fx,fy,tx,ty,escape` | 視点、手番、取った駒、全駒（**相手の駒の種類は伏せ済み**） | ホストがゲスト用に伏せて送る |
| BattleLine | `kind,card,flag,target,toFlag,deck` | 視点、フェーズ、山札の残り、自分の手札、**相手の手札は枚数のみ**、9 本のフラッグ | ホストがゲスト用に伏せて送る |
| CenturyGhost | `kind,target,times,upgrades`（kind = 術を使う / 覚える / おばけを呼ぶ / 休む） | 視点、先手、手番、手数、最終ラウンドか、勝敗と終了理由、**山札 2 つは枚数のみ**、直前の手、場（市場 6 枚とその上の魂・おばけの列 5 枚・残りコイン）、両者の魂・手札・使用済み・おばけ・コイン | 手札は公開情報なのでそのまま送る。伏せるのは山札の並びだけ |

CenturyGhost の Snapshot は 16 項目で、`/`・`.`・`,` の 3 段の区切りを使う（魂は `YYCM` 形式、空は `_`）。
手の候補が多い（術の回数・昇格の組み合わせ）ため、ゲストの送信前チェックは、他のゲームの
「合法手の一覧に含まれるか」ではなく `IsLegal(move)` で直接判定している。

## 6. 現状の課題（AWS 移行で解決すべきこと）

| # | 課題 | 影響 | AWS 版での対応 |
|---|---|---|---|
| 1 | **ホスト（P0）がクライアント**で、完全な局面を持っている | Geister / BattleLine / CenturyGhost では、ホスト側のアプリを改造すれば相手の駒や手札・山札の順番が見える（不正の余地）。CenturyGhost では次に市場へ出る術やおばけが先にわかる | **サーバーが局面を持つ「サーバー権威型」にする**。両プレイヤーとも「ゲスト」と同じ扱いにする |
| 2 | 乱数（先手、配置、配札、山札のシャッフル）をホストが決めている | ホストが有利な乱数を選べてしまう | サーバーで乱数を決める |
| 3 | 相手が抜けると即「対戦終了」 | 一瞬の通信切れやページの再読み込みで対局が終わる | 60 秒ほどの再接続待ちを入れる |
| 4 | ホスト自身の手は検証されない（ホストのクライアントで `IsLegal` を確認するだけ） | 改造したホストは不正な手を打てる | サーバーで両者の手を検証する |
| 5 | 対局結果がどこにも残らない | 戦績・レーティングを作れない | 終局時にサーバーが DynamoDB に保存する |
| 6 | 持ち時間・放置への対策がない | 相手が放置すると終わらない | サーバー側にターンの制限時間を設ける（任意） |

## 7. 移行方針（2026-10-18 決定）

### 方針: ゲームサーバーを C#（ASP.NET Core）で作り、Logic を共有する（Colyseus 案は不採用）

当初の案では Colyseus（Node.js / TypeScript）だったが、ルールが純粋な C# で書かれているため、
**サーバーも C# にすればルールを書き直さずに済む**（5 ゲーム分のルールと 5 つのプロトコルを TS に移植する手間とバグの危険がなくなる）。

```
共有（Unity とサーバーの両方で使う）
  Core/Scripts/*                    GameResult、IGameRules など
  Games/<Game>/Logic/*              ルール・局面・手・プロトコル（Online/ 含む）

Unity 側（各ゲームでの変更）
  Online.Photon/ を削除し、Online.Aws/AwsOnlineService.cs を追加（IOnlineService の実装）
  WebSocket ライブラリ（例: NativeWebSocket）で接続
  OnlineMatch は「両者ゲスト」モードで動かす（局面はサーバーから受け取る）

サーバー側（server/、.NET 8 以降、ECS Fargate）
  WebSocket の受け口、ロビー / マッチング / ルームコード
  ルームごとに「サーバー版 OnlineMatch」がルール（<Game>Rules）で手を検証し、
  各プレイヤーに MatchSnapshot.For(state, player) で伏せた局面を送る
```

### 段階的な進め方

1. **ステップ A（最小の差し替え）**: サーバーはマッチングと文字列の中継だけを行い、ホスト権威型はそのまま残す。
   Unity 側は `AwsOnlineService` を追加するだけ。PUN2 からの脱却を最短で確認できる。
2. **ステップ B（サーバー権威型）**: サーバーが `OnlineMatch` 相当を実行し、課題 1・2・4 を解消する。
   不完全情報の Geister / BattleLine を優先する（CenturyGhost は隠し情報が山札の並びだけなので、その次）。
3. **ステップ C**: 再接続、結果保存、レーティング、制限時間（課題 3・5・6）。

### ゲームごとの移行の難しさ

| ゲーム | ステップ A | ステップ B | 備考 |
|---|---|---|---|
| Vanish | 小 | 小 | 最初の 1 本に最適（Phase 1 の対象でもある） |
| Gobblet | 小 | 小 | |
| Quarto | 小 | 小〜中 | 「置く / 渡す」の 2 段階の手番 |
| Geister | 小 | 中 | 視点ごとに伏せた局面を送る |
| BattleLine | 小 | 中 | 山札・手札・戦術カード。メッセージ項目が 26 個と多い |
| CenturyGhost | 小 | 小〜中 | 伏せるのは山札 2 つの並びだけ。手は 4 種類だが、1 手の中身（回数・昇格）が多い。最大 200 手と対局が長め |

## 8. 補足（気づいた点）

- 6 本とも同じコード（`IOnlineService`、`OnlineMatch` の骨格、`PhotonOnlineService`）を
  プロジェクトごとにコピーして持っている。AWS 版では共通部分を UPM パッケージ（`unity/`）にまとめると、
  修正が 1 か所で済む。
- 各ゲームに `<Game>OnlineTests.cs` がある。サーバー版 OnlineMatch のテストの土台として使える。
- Geister の初期配置は `GeisterSetup.CreateRandom`（両者ランダム）で、プレイヤーが配置を選ぶ画面はない。
  サーバー権威型にするとき、配置を選べるようにするかを決めておく。

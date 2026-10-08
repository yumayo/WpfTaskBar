Wpfで作ったお手製のタスクバーです。
勤怠情報も追加で表示しています。

Windows 11 22H2（ビルド 22621）以降では、背後の壁紙やウィンドウをぼかし、一定の半透明のダーク色を重ねた背景を表示します。
Windows の「設定 → 個人用設定 → 色 → 透明効果」をオンにしてください。非対応 OS、透明効果オフ、ハイコントラスト、API の設定失敗時は単色の背景に戻ります。
非アクティブ時に灰色へ切り替わる Desktop Acrylic の代わりに、`SetWindowCompositionAttribute` の `ACCENT_ENABLE_BLURBEHIND` を使用します。
これは非公開の Accent API を使う実装で、Windows の更新による動作変更の可能性があります。Windows 標準タスクバーと完全に同じ描画ではありません。
背景方式の切り替えはログの `Taskbar background:` に記録します。

背景の動作確認は Windows 実機で、タスクバーと別ウィンドウ間のマウス移動、別ウィンドウの選択・終了、通知領域への最小化・復元、透明効果のオン・オフを行ってください。

# API

## 出勤

```sh
curl -X POST "http://localhost:5000/clock-in" -H "Content-Type: application/json" -d "{\"date\": \"2025-06-04T09:55:00\"}"
```

## 退勤

```sh
curl -X POST "http://localhost:5000/clock-out" -H "Content-Type: application/json" -d "{\"date\": \"2025-06-04T19:34:00\"}"
```

## 勤怠のクリア

```sh
curl -X POST http://localhost:5000/clear
```

呼び出さなくても午前0時にリセットされます。

## 通知

```sh
curl -X POST "http://localhost:5000/notification" -H "Content-Type: application/json" -d "{\"title\": \"通知タイトル\", \"message\": \"通知内容\"}"
```

## タスクの状態表示 API

タスクアイコンの左側に、すべてのタスクで同じ幅の状態表示領域があります。
AI の開始・終了時に `POST /tasks/status` を呼び出すと、次の表示に切り替わります。

| status | 表示 |
| --- | --- |
| `running` | 青い回転マーク（実行中） |
| `waiting` | 黄色の「?」（質問・承認待ち） |
| `interrupted` | オレンジの一時停止マーク（中断） |
| `completed` | 緑のチェックアイコン（実行済み） |
| `none` | 状態アイコンを解除（領域は残ります） |

まず、対象ウィンドウの `handle` と `title` を確認します。
一覧は現在の仮想デスクトップ以外のタスクも含みます。

```sh
curl "http://localhost:5000/tasks"
# {"tasks":[{"handle":123456,"processId":1234,"title":"my-project","moduleFileName":"...","status":"none"}]}
```

AI の開始時には `running`、質問・承認待ちには `waiting`、中断時には `interrupted`、応答終了時には `completed` を送ります。
`handle` は実際のウィンドウ ID に置き換えてください。

```sh
curl -X POST "http://localhost:5000/tasks/status" \
  -H "Content-Type: application/json" \
  -d '{"handle":123456,"status":"running"}'

curl -X POST "http://localhost:5000/tasks/status" \
  -H "Content-Type: application/json" \
  -d '{"handle":123456,"status":"completed"}'

curl -X POST "http://localhost:5000/tasks/status" \
  -H "Content-Type: application/json" \
  -d '{"handle":123456,"status":"none"}'
```

`handle` の代わりに `title` で指定することもできます。
タイトルは大文字・小文字を区別しない部分一致です。

```sh
curl -X POST "http://localhost:5000/tasks/status" \
  -H "Content-Type: application/json" \
  -d '{"title":"my-project","status":"running"}'
# 成功時は {"handle":123456,...,"status":"running"} を返します。
```

`handle` と `title` はどちらか一方を指定してください。
不正な指定は HTTP 400、対象がなければ 404、タイトルが複数に一致すれば候補一覧付きの 409 を返し、状態を変更しません。
タイトルが変わるアプリでは、開始時の応答に含まれる `handle` を使って終了を通知してください。
WSL から接続する場合、必要に応じて `localhost` を Windows ホストの IP アドレスに置き換えます。

実行済みの表示はクリックしても消えず、次の通知まで残ります。
状態はウィンドウ単位で保持し、ウィンドウを閉じたとき、または WpfTaskBar を再起動したときにリセットします。
ターミナル内のタブや AI セッション単位ではなく、タスクバーに表示されるウィンドウ単位の状態です。

## VSCode の AI コンテナ連携

[VSCodeExtension](VSCodeExtension/README.md) に、WSL / AI コンテナの依頼開始・応答完了を
所属する VSCode ウィンドウへ表示する拡張を用意しています。
拡張の起動時にウィンドウ共通のセッションIDを用意し、通常の統合ターミナルへ環境変数を設定します。
WpfTaskBarが未起動でもエラー通知は出さず、1秒間隔で接続・生存確認します（前回の処理完了から1秒後に再確認）。後からの起動や本体の再起動では同じIDで自動登録するため、そのIDを引き継いだコンテナは再起動せずに通知を再開できます。
更新版は `VSCodeExtension/wpftaskbar-ai-status-0.1.7.vsix` です。同じIDでの再接続にはWpfTaskBar本体も更新してください。
通常の「＋」からターミナルを開き、通知先の環境変数を `aicontainer` / `docker compose` に渡します。
コンテナ内の AI フックから共通の `python3 wpftaskbar.py running|waiting|interrupted|completed|none` を呼び出します。

AIは1ウィンドウ1セッションとし、同じウィンドウのすべてのターミナルで同じIDを使います。
セッション登録済みのタスクは通常の2倍の高さになり、ウィンドウタイトルの下にAIの最新の進捗コメント・応答文を先頭120文字まで表示します。
状態・アプリアイコンは項目全体の縦中央に配置し、タイトルとAI通知は共通の文字領域で左揃えにします。
タイトルは12px、AI通知は11pxの明るいグレーとし、4pxの間隔でまとめて表示します。
Codex / Claude Codeのフックが会話ログから本文を取り出し、ツールの実行前後や応答完了時に更新します。導入スクリプトを再実行すると、必要なヘルパーとフックを更新できます。
進捗文がまだない場合はアクティブな統合ターミナルのタイトルを表示します。
VSCode拡張が1秒ごとにタイトルの変更を確認して通知するため、質問待ちや中断中も更新されます。
タスクリストを右クリックして「オプション」を開き、「AI用の表示欄を表示する」をオフにすると、下段の文言を隠して通常の高さに戻せます。状態アイコンは引き続き表示されます。
設定は保存後すぐに反映され、再起動後も維持されます。
通知先は一覧の先頭にある VSCode タスクを自動選択します。複数のVSCodeウィンドウがある場合は「この VSCode の通知先を選択」でそれぞれの通知先を選んでください。インストール、環境変数の引き継ぎ、フック設定は上記の手順を参照してください。

# 開発

```sh
dotnet build
```

```sh
dotnet run --project WpfTaskBar
```

状態表示のテスト:

```sh
npm --prefix WebView test
dotnet test Tests/TaskStatus.Tests/TaskStatus.Tests.csproj
```

API のテストはウィンドウ列挙を差し替えるため、.NET 8 SDK があれば Windows 以外でも実行できます。

iオプションは対話シェルで、.bashrcを読み込んでくれます。
```sh
docker compose up -d --build && docker compose exec ai bash -i -c "claude -c"
```

# ビルド

```sh
wsl
make artifact
```

APP_VERSION は、取得した最新の `v` タグの末尾を 1 増やした値になります。タグがない場合は `v0.1` を使用します。
明示的に指定する場合は `make artifact APP_VERSION=v0.xx` を実行してください。
ビルドと ZIP 作成後、`master` と作成したタグを push し、エクスプローラーと GitHub のリリース作成ページを開きます。

# Gitプッシュ

```sh
git push origin master
git tag v0.xx
git push origin --tags
```

# デバッグ

```sh
wsl tail ---disable-inotify -f WpfTaskBar/bin/Debug/net8.0-windows/log/WpfTaskBar.log
```

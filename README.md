Wpfで作ったお手製のタスクバーです。
勤怠情報も追加で表示しています。

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
拡張の「AI 通知付きターミナルを開く」から起動し、通知先の環境変数を `aicontainer` / `docker compose` に渡します。
コンテナ内の AI フックから共通の `taskbar-status.sh running|waiting|interrupted|completed|none` を呼び出します。

複数のターミナルの状態はウィンドウ単位で集約し、質問・承認待ち → 実行中 → 中断 → 完了の順に優先します。
通知付きターミナルがあるタスクは通常の2倍の高さになり、下半分にAIターミナルのタイトルを表示します。
VSCode拡張が1秒ごとにタイトルの変更を確認して通知するため、質問待ちや中断中も更新されます。
VSCode が複数ある場合は初回に通知先を選択します。インストール、環境変数の引き継ぎ、フック設定は上記の手順を参照してください。

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

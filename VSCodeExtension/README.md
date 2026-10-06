# WpfTaskBar AI Status

VSCode の統合ターミナルで起動した AI コンテナから、依頼ごとの実行中 / 応答完了を WpfTaskBar に表示します。
Windows 側の VSCode にインストールする拡張です。通常の WSL ターミナルと Remote - WSL の両方を想定しています。

```text
VSCode 拡張 → 通知付きターミナルを作成（ウィンドウと通知 ID を登録）
                  ↓ 環境変数を引き継ぐ
                WSL → aicontainer / docker compose → AI のフック
                                                        ↓ HTTP
Windows の WpfTaskBar ← ターミナルごとの状態を集約 ← running / completed
```

`wsl.exe` の親 PID だけでは、同じ Electron プロセスが持つ複数の VSCode ウィンドウを区別できません。
この拡張は WpfTaskBar のウィンドウ一覧から HWND と PID を登録し、以後はタイトル変更に影響されない通知 ID を使用します。
AI の開始・終了の判定は AI 側のフックが行います。コンテナの起動時間やターミナルの出力からは推測しません。

## インストール

セッション API に対応したこのリポジトリの WpfTaskBar をビルドして起動します。
拡張をパッケージ化します（Node.js 24 LTS 推奨）。

```sh
cd VSCodeExtension
npm install
npm run check
npm test
npm run package
```

パッケージ作成にはプロジェクト内の `@vscode/vsce` を使用します。
既に導入済みの環境で最新版へ更新する場合は、`VSCodeExtension` ディレクトリで以下を実行してください。

```sh
npm install --save-dev @vscode/vsce@latest
npm exec -- vsce --version
npm run package
```

`package.json` と `package-lock.json` が更新されます。グローバルインストールは不要です。

Windows 側の VSCode の「拡張機能: VSIX からのインストール」で、生成された `wpftaskbar-ai-status-0.1.0.vsix` を選択してください。
Remote - WSL 使用時も拡張は Windows 側で動きます。

拡張本体は `src/*.ts` で実装し、`npm run build` で `dist/*.js` へコンパイルします。
`npm test` と `npm run package` は事前にビルドを実行します。VSIX にはコンパイル済みの JavaScript を含めます。

## 通知付きターミナルを開く

1. VSCode の設定で `wpftaskbar.apiUrl` を Windows 側から接続できる WpfTaskBar の URL に設定します。既定値は `http://127.0.0.1:5000` です。
2. `wpftaskbar.containerApiUrl` に、**AI コンテナ内から Windows に到達できる URL** を設定します。空欄では `apiUrl` と同じです。通常の Docker コンテナ内では `127.0.0.1` はコンテナ自身です。Docker Desktop なら `http://host.docker.internal:5000`、WSL の Docker Engine なら Windows ホストの IP アドレスなど、実際のネットワークに合わせて指定してください。
3. コマンドパレットで **WpfTaskBar: AI 通知付きターミナルを開く** を実行します。
4. VSCode が複数ある場合、初回に**この VSCode ウィンドウ**のタスクを選択します。1 つだけなら自動選択します。同じタイトルが複数ある場合は、ウィンドウタイトルを区別してから選択してください。
5. このターミナルから WSL / `aicontainer` / `docker compose` を起動します。AI のフックと環境変数の引き継ぎを次の手順で設定します。

ターミナルには次の環境変数が設定されます。

| 変数 | 内容 |
| --- | --- |
| `WPF_TASKBAR_URL` | コンテナからの通知先 URL |
| `WPF_TASKBAR_SESSION_ID` | このターミナル専用の通知 ID |

Windows のターミナルから `wsl.exe` を起動する場合に備え、`WSLENV` にも上記の変数を追加します。
WSL から Docker コンテナへの引き継ぎはランチャー側で設定が必要です。
開くターミナルのシェルには VSCode の既定プロファイルを使います。ローカルウィンドウでは WSL プロファイルを既定にするか、作成されたターミナルで `wsl.exe` を実行してください。

## aicontainer からの利用

`aicontainer` がコンテナを作成するときに、上記 2 つの環境変数を渡してください。
ホストの変数をコンテナへ引き継ぐ機能を持つ版では、その対象に両方を追加します。
このリポジトリには `aicontainer` 本体が含まれないため、拡張はランチャーの設定や AI の設定ファイルを自動変更しません。

`.aicontainer` の `env=名前=値` を使う場合は、通知付きターミナルの WSL シェルで次を実行して、その時点の設定行を取得できます。

```sh
printf 'env=WPF_TASKBAR_URL=%s\nenv=WPF_TASKBAR_SESSION_ID=%s\n' \
  "$WPF_TASKBAR_URL" "$WPF_TASKBAR_SESSION_ID"
```

出力された 2 行を `.aicontainer` に設定してから `aicontainer` を起動します。
**通知 ID はターミナルごとに異なります。** 固定値での設定は開き直すたびに更新し、Git にコミットしないでください。
同じ設定ファイルで複数のコンテナを起動する場合は、各起動時に対応する値を読み込ませてください。
ランチャーの環境変数引き継ぎ機能を利用すると、この書き換えが不要になります。
起動済みのコンテナの環境変数は設定ファイルを変更しても変わらないため、作り直しが必要です。

AI コンテナ内で `WPF_TASKBAR_URL` と `WPF_TASKBAR_SESSION_ID` が設定されていることを確認し、共通フックを登録します。

## docker compose からの利用

例えば既存の `ai` サービスに次を追加します。`curl` もコンテナへインストールしてください。

```yaml
services:
  ai:
    environment:
      WPF_TASKBAR_URL: ${WPF_TASKBAR_URL}
      WPF_TASKBAR_SESSION_ID: ${WPF_TASKBAR_SESSION_ID}
    volumes:
      - ./VSCodeExtension/scripts/taskbar-status.sh:/opt/taskbar/taskbar-status.sh:ro
```

通知付きターミナルで次のように起動すると、ターミナルごとの値を独立したコンテナに渡せます。

```sh
docker compose run --rm ai
```

既存のコンテナに接続する場合は、`exec` 時に環境変数を渡します。

```sh
docker compose exec \
  -e WPF_TASKBAR_URL="$WPF_TASKBAR_URL" \
  -e WPF_TASKBAR_SESSION_ID="$WPF_TASKBAR_SESSION_ID" \
  ai bash
```

フックスクリプトもそのコンテナ内から参照できる場所に配置してください。
異なる通知 ID で同じサービスを `up --force-recreate` すると、先に起動した AI を停止させるため、並列実行では個別の `run` または `exec` を使います。

## 共通フック

`scripts/taskbar-status.sh` をコンテナへコピーするか、読み取り専用でマウントします。
AI のフック機構に次のコマンドを登録してください。どのランチャーでも同じスクリプトを使えます。

| タイミング | コマンド |
| --- | --- |
| ユーザーの依頼を受け付けたとき | `sh /opt/taskbar/taskbar-status.sh running` |
| AI が応答を終えたとき | `sh /opt/taskbar/taskbar-status.sh completed` |
| キャンセル・表示解除 | `sh /opt/taskbar/taskbar-status.sh none` |

開始と完了は**AI の応答単位**で呼びます。CLI / コンテナの起動と終了に設定すると、その生存期間の表示になってしまいます。
通知用の環境変数がない場合は何もせず終了するため、同じフック設定を VSCode 以外でも使用できます。
通信失敗は標準エラーに出し、最大 5 秒で終了します。通知失敗で AI 本体を停止させません。
第2引数で通信の上限を1〜5秒に短縮できます。例: `sh /opt/taskbar/taskbar-status.sh none 2`。

### Codex CLI のユーザー共通フック

Codex のフックからも同じ `taskbar-status.sh` を使えます。
[公式 OpenAI Docs の Hooks](https://learn.chatgpt.com/docs/hooks) に従い、次の4イベントを登録します。
この環境では Codex CLI `0.159.2` の `hooks` 機能が有効であることを確認しています。

| Codexイベント | 通知する状態 |
| --- | --- |
| `UserPromptSubmit` | `running` |
| `Stop` | `completed` |
| `Interrupt` | `none` |
| `SessionEnd` | `none` |

#### 手動で設定する場合

設定する場所は、**Codexを動かすAIコンテナ内**です。
ユーザー共通の設定ファイルは `~/.codex/hooks.json` です。`CODEX_HOME` を指定している場合は `$CODEX_HOME/hooks.json` を使います。
同じCodex設定ディレクトリを使うプロジェクトで共通に有効になります。

まずリポジトリルートで、通知スクリプトを共通の場所へコピーします。手動設定に必要なのは `sh` と `curl` です。

```sh
taskbar_codex_dir="${CODEX_HOME:-$HOME/.codex}"
mkdir -p "$taskbar_codex_dir/hooks/wpftaskbar"
cp VSCodeExtension/scripts/taskbar-status.sh "$taskbar_codex_dir/hooks/wpftaskbar/taskbar-status.sh"
printf '設定ファイル: %s\n' "$taskbar_codex_dir/hooks.json"
printf '通知スクリプト: %s\n' "$taskbar_codex_dir/hooks/wpftaskbar/taskbar-status.sh"
```

表示された `hooks.json` をエディターで開き、次のJSONを保存します。
以下はホームが `/home/ubuntu`、`CODEX_HOME` が未設定の場合の例です。**4か所のスクリプトのパスを、上で表示された実際の絶対パスに合わせてください。**
既存の設定がある場合は上書きせず、既存の `hooks` に追加します。同じイベントが既にあれば、その配列へ今回の `{"hooks": [...]}` を追加してください。

```json
{
  "hooks": {
    "UserPromptSubmit": [{
      "hooks": [{ "type": "command", "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' running", "timeout": 10 }]
    }],
    "Stop": [{
      "hooks": [{ "type": "command", "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' completed", "timeout": 10 }]
    }],
    "Interrupt": [{
      "hooks": [{ "type": "command", "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' none 2", "timeout": 3 }]
    }],
    "SessionEnd": [{
      "hooks": [{ "type": "command", "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' none 2", "timeout": 3 }]
    }]
  }
}
```

この設定でCodexがイベントごとに `taskbar-status.sh` を実行します。
スクリプトは環境変数 `WPF_TASKBAR_URL` と `WPF_TASKBAR_SESSION_ID` を読み、WindowsのWpfTaskBarへ状態をHTTPで通知します。
URLと通知IDは通知付きターミナルからコンテナへ引き継ぎ、JSONには固定で書き込みません。

Codexを起動し直し、**`/hooks` で4つの通知フックを確認して信頼**してください。
これはCodex本体のフック実行要件です。既存の `config.toml` にも同じ通知を設定している場合は重複を解消します。
フックは標準出力に何も書かず、通知に失敗しても通常終了します。中断・終了用は通信上限を2秒、フックの期限を3秒にしています。

#### 導入スクリプトで設定する場合

`scripts/install-codex-hooks.py` は、上のコピーとJSONへの登録を自動で行う導入用スクリプトです。
既存のフックを保持し、変更前のJSONを `.bak` ファイルへ保存します。同じコマンドは二重登録しません。
通知コマンドには配置先の絶対パスを設定します。`config.toml` の変更や `/hooks` の信頼確認は行いません。

Python 3.8以降がある場合、手動設定の代わりに次のコマンドを使えます。

```sh
python3 VSCodeExtension/scripts/install-codex-hooks.py
```

実行後は手動設定と同様に、Codexを起動し直して `/hooks` で確認・信頼します。
導入用のテンプレートは [examples/codex-hooks.json](examples/codex-hooks.json) です。パスのプレースホルダーは導入スクリプトが置き換えます。
各ターンの通知は `taskbar-status.sh` が行い、Pythonの導入スクリプトは実行しません。

`aicontainer` を使う場合も設定先はコンテナ内のCodexです。
設定ディレクトリが永続化されていない構成では、コンテナを作り直すと再登録が必要です。

初回は通知付きターミナルから起動し、短い依頼を2回送り、毎回「実行中 → 完了」に変わることを確認してください。
中断と通常終了で表示が消えることも確認します。通知IDが未設定の普通のターミナルでは、フックは通知せず終了します。
`Stop` は応答の停止タイミングを示し、作業の成功を保証するイベントではありません。
他の `Stop` フックで応答を継続させる構成では、処理が続いていても完了表示になる場合があります。

### Claude Code のユーザー共通フック

Claude Codeでも同じ `taskbar-status.sh` を使います。
[公式のフック仕様](https://code.claude.com/docs/en/hooks)に合わせ、次の4イベントを登録します。

| Claude Codeイベント | 通知する状態 |
| --- | --- |
| `UserPromptSubmit` | `running` |
| `Stop` | `completed` |
| `StopFailure`（APIエラーで応答が終了） | `none` |
| `SessionEnd`（終了・会話のクリアなど） | `none` |

#### 手動で設定する場合

設定先は**Claude Codeを動かすAIコンテナ内**の `~/.claude/settings.json` です。
`CLAUDE_CONFIG_DIR` を指定している場合は `$CLAUDE_CONFIG_DIR/settings.json` を使います。
同じClaude Code設定ディレクトリを使うプロジェクトに共通で適用されます。

リポジトリルートで、通知スクリプトを共通の場所へコピーします。

```sh
taskbar_claude_dir="${CLAUDE_CONFIG_DIR:-$HOME/.claude}"
mkdir -p "$taskbar_claude_dir/hooks/wpftaskbar"
cp VSCodeExtension/scripts/taskbar-status.sh "$taskbar_claude_dir/hooks/wpftaskbar/taskbar-status.sh"
printf '設定ファイル: %s\n' "$taskbar_claude_dir/settings.json"
printf '通知スクリプト: %s\n' "$taskbar_claude_dir/hooks/wpftaskbar/taskbar-status.sh"
```

表示された `settings.json` をエディターで開き、次のJSONを追加します。
以下はホームが `/home/ubuntu`、`CLAUDE_CONFIG_DIR` が未設定の場合の例です。**4か所のスクリプトのパスを実際の絶対パスに合わせてください。**
既存の権限・環境変数・フック設定は残します。同じイベントが既にある場合は、その配列へ今回の `{"hooks": [...]}` を追加してください。

```json
{
  "hooks": {
    "UserPromptSubmit": [{
      "hooks": [{ "type": "command", "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' running", "timeout": 10 }]
    }],
    "Stop": [{
      "hooks": [{ "type": "command", "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' completed", "timeout": 10 }]
    }],
    "StopFailure": [{
      "hooks": [{ "type": "command", "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' none 2", "timeout": 3 }]
    }],
    "SessionEnd": [{
      "hooks": [{ "type": "command", "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' none 2", "timeout": 3 }]
    }]
  }
}
```

通知用の `WPF_TASKBAR_URL` と `WPF_TASKBAR_SESSION_ID` は環境変数から読みます。JSONに固定で書き込む必要はありません。
Claude Codeを起動し直し、**`/hooks` で4つのフックと配置先を確認**してください。Claude Codeの `/hooks` は設定を閲覧するためのメニューです。

#### 導入スクリプトで設定する場合

Python 3.8以降があれば、コピーとJSONへの登録を次のコマンドで行えます。

```sh
python3 VSCodeExtension/scripts/install-claude-hooks.py
```

導入スクリプトは `settings.json` の既存設定を保持し、変更前のファイルを `.bak` に保存します。
同じコマンドの重複登録を防ぎ、通知スクリプトは再実行で更新できます。
`permissions`、`env`、`disableAllHooks` などの既存設定は変更しません。
テンプレートは [examples/claude-hooks.json](examples/claude-hooks.json) です。

実行後にClaude Codeを起動し直し、`/hooks` で確認します。各ターンの通知には `sh` と `curl` を使います。
`aicontainer` やDocker ComposeではClaude Code設定ディレクトリを永続化してください。永続化されていなければコンテナ再作成後に再登録します。

#### 動作確認と中断時の扱い

この環境のCLIは `2.1.285` です。登録後は短い依頼を2回送り、毎回「実行中 → 完了」に変わることと、通常終了で表示が消えることを確認してください。
`Stop` は応答が終わったことを表し、作業成功の判定には使いません。他の停止フックが応答を継続させる場合は、処理中でも完了表示になる場合があります。

**Escなどによる中断では `Stop` は発火せず、実行中表示が残る場合があります。**
Claude Codeの公式イベント一覧には `Interrupt` がないため、この設定には登録していません。
中断直後に表示を解除する場合は、同じ通知用環境変数を引き継いだコンテナ内シェルで次を実行します。

```sh
sh "${CLAUDE_CONFIG_DIR:-$HOME/.claude}/hooks/wpftaskbar/taskbar-status.sh" none 2
```

既存設定で `disableAllHooks` が有効になっている場合や、管理設定でユーザーフックが制限されている場合は、その設定も確認してください。

他のAIでも、依頼開始・応答完了のフックへ同じコマンドを登録できます。

## 表示と通知の寿命

- 同じウィンドウ内で 1 つでも `running` があれば「実行中」を表示します。
- 実行中がなく、`completed` があれば「完了」を表示します。
- `none` とターミナルを閉じる操作は、そのターミナルの状態だけを解除します。
- **1 つの通知付きターミナルにつき、同時に実行する AI は 1 つ**にしてください。複数の AI はそれぞれ別の通知付きターミナルから起動します。
- 拡張は 30 秒ごとに生存通知を送り、2 分間届かなければ登録と状態が失効します。
- VSCode の再読み込み、拡張の停止、WpfTaskBar の再起動、長時間のスリープ後は、通知付きターミナルを開き直し、コンテナにも新しい環境変数を渡してください。
- 作成前から開いていたターミナル・コンテナには自動で接続しません。
- 「WpfTaskBar: この VSCode の通知先を選択」で通知先を選び直せます。先に通知付きターミナルを閉じてください。
- 従来の `POST /tasks/status` の状態とは独立し、集約時に `running` が優先されます。従来の API で設定した状態の解除には、従来の API から `none` を送ります。

## 疎通確認

コンテナ内から次を実行し、選択した VSCode タスクの表示が青い回転マーク → 緑のチェック → 解除に変わることを確認します。

```sh
sh /opt/taskbar/taskbar-status.sh running
sh /opt/taskbar/taskbar-status.sh completed
sh /opt/taskbar/taskbar-status.sh none
```

同じ VSCode 内で 2 つ、別の VSCode で 1 つ通知付きターミナルを開き、前者の片方が `completed` でも他方が `running` なら実行中が残ること、別ウィンドウには影響しないことも確認します。

接続できない場合は、コンテナから Windows の TCP 5000 番への到達性と URL を確認してください。
HTTP 404 は通知先の失効を示します。「WpfTaskBar: 通知ログを表示」から拡張側の接続エラーも確認できます。

## API と開発

| メソッド / パス | 用途 |
| --- | --- |
| `POST /tasks/sessions` | `{ "handle": 123, "processId": 456 }` で VSCode ウィンドウへ登録。`sessionId` を返す |
| `POST /tasks/sessions/{id}/status` | `{ "status": "running" }` などを送る |
| `PUT /tasks/sessions/{id}/heartbeat` | 拡張から有効期限を延長する |
| `DELETE /tasks/sessions/{id}` | ターミナル終了時に登録を解除する（繰り返し可能） |

API は状態の集約、ウィンドウの生存確認、有効期限を担当します。コンテナの通知だけでは有効期限は延長しません。
登録時・更新時は HWND と PID の両方を確認します。セッションはディスクに保存しません。

```sh
npm --prefix VSCodeExtension run build
npm --prefix VSCodeExtension test
npm --prefix VSCodeExtension run check
dotnet test Tests/TaskStatus.Tests/TaskStatus.Tests.csproj
```

Node.js のテストは VSCode API を差し替えた動作確認とローカル HTTP の通信確認を含みます。
ループバック通信が禁止された環境に限り、`WPF_TASKBAR_SKIP_NETWORK_TESTS=1 npm --prefix VSCodeExtension test` で通信テスト 3 件を明示的にスキップできます。
WSL の環境変数転送、実際の VSCode / WpfTaskBar の表示、AI のフック発火は Windows 実環境で上記の疎通確認を行ってください。

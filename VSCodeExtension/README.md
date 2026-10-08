# WpfTaskBar AI Status

VSCode の統合ターミナルで起動した AI コンテナから、依頼ごとの実行中 / 質問・承認待ち / 中断 / 応答完了と、最新の進捗文を WpfTaskBar に表示します。進捗文がない場合はターミナルタイトルを表示します。
Windows 側の VSCode にインストールする拡張です。通常の WSL ターミナルと Remote - WSL の両方を想定しています。

```text
VSCode 拡張 → 起動時にウィンドウ共通の通知 ID を用意（接続後に登録）
                  ↓ 通常の統合ターミナルへ環境変数を設定
                WSL → aicontainer / docker compose → AI のフック
                                                        ↓ HTTP
Windows の WpfTaskBar ← ウィンドウ共通のセッション ← running / waiting / interrupted / completed
```

`wsl.exe` の親 PID だけでは、同じ Electron プロセスが持つ複数の VSCode ウィンドウを区別できません。
この拡張は WpfTaskBar のウィンドウ一覧から HWND と PID を登録し、以後はタイトル変更に影響されない通知 ID を使用します。
AI の開始・終了の判定は AI 側のフックが行います。コンテナの起動時間やターミナルの出力からは推測しません。

## インストール

このリポジトリの WpfTaskBar をビルドします。拡張 0.1.5 以降では同じ ID での再登録に対応した本体が必要なので、旧版から更新する場合は本体も更新してください。本体と VSCode の起動順は問いません。
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

Windows 側の VSCode の「拡張機能: VSIX からのインストール」で、生成された `wpftaskbar-ai-status-0.1.7.vsix` を選択してください。
Remote - WSL 使用時も拡張は Windows 側で動きます。

ターミナルの環境変数変更による再起動要求を非表示にするには、コマンドパレットの「基本設定: ユーザー設定を開く (JSON)」から、VSCode の `settings.json` に次の設定を追加してください。

```json
"terminal.integrated.environmentChangesIndicator": "off"
```

これは VSCode の設定です。拡張の `package.json` の `contributes.configurationDefaults` に指定すると `Property terminal.integrated.environmentChangesIndicator is not allowed.` と警告されるため、0.1.6 でその指定を削除しました。拡張を更新するだけでは再起動要求は非表示になりません。
この設定は他の拡張の同種の通知にも適用されます。ワークスペース側で別の値を明示している場合は、そちらも確認してください。設定ファイルを拡張から書き換えることはありません。

拡張本体は `src/*.ts` で実装し、`npm run build` で `dist/*.js` へコンパイルします。
`npm test` と `npm run package` は事前にビルドを実行します。VSIX にはコンパイル済みの JavaScript を含めます。

## ウィンドウ共通の通知設定

**1つの VSCode ウィンドウにつき、同時に使う AI は1セッションです。**
拡張の起動時に通知 ID を用意し、本体への接続前から VSCode の環境変数コレクションに設定します。同じウィンドウの新しいターミナルはすべて同じ ID を使います。本体が未起動でも、この ID をコンテナへ引き継げます。

1. VSCode の設定で `wpftaskbar.apiUrl` を Windows 側から接続できる WpfTaskBar の URL に設定します。既定値は `http://127.0.0.1:5000` です。
2. `wpftaskbar.containerApiUrl` に、**AI コンテナ内から Windows に到達できる URL** を設定します。空欄では `apiUrl` と同じです。通常の Docker コンテナ内では `127.0.0.1` はコンテナ自身です。Docker Desktop なら `http://host.docker.internal:5000`、WSL の Docker Engine なら Windows ホストの IP アドレスなど、実際のネットワークに合わせて指定してください。
3. 通常の「＋」や「ターミナル: 新しいターミナルの作成」でターミナルを開きます。
4. そのターミナルから WSL / `aicontainer` / `docker compose` を起動します。AI のフックと環境変数の引き継ぎを次の手順で設定します。

通知先は一覧の先頭にある VSCode タスクを自動選択します。複数の VSCode ウィンドウがある場合は、**WpfTaskBar: この VSCode の通知先を選択** で各ウィンドウの通知先を確認・選択してください。同じタイトルが複数ある場合は、ウィンドウタイトルを区別してから選択してください。
通知先を変更すると古い登録を解除して ID を更新します。手動選択のキャンセルや同じ通知先の再選択では ID を維持します。変更後は通常のターミナルとコンテナを起動し直してください。

| 変数 | 内容 |
| --- | --- |
| `WPF_TASKBAR_URL` | コンテナからの通知先 URL |
| `WPF_TASKBAR_SESSION_ID` | この VSCode ウィンドウ共通の通知 ID |

Windows のターミナルから `wsl.exe` を起動する場合に備え、`WSLENV` にも上記の変数を追加します。
WSL から Docker コンテナへの引き継ぎはランチャー側で設定が必要です。
シェルは VSCode 標準のプロファイル設定を使います。専用のターミナル作成コマンド、**AI (WpfTaskBar)** プロファイル、`wpftaskbar.shellPath` / `wpftaskbar.shellArgs` は削除しました。旧版で専用プロファイルを既定にしていた場合は、VSCode の「ターミナル: 既定のプロファイルの選択」で PowerShell / WSL / bash などへ変更し、旧シェル設定を削除してください。

WpfTaskBar が未起動・接続できない場合は、接続拒否やタイムアウトのエラー通知を出さず、1秒間隔で再試行します。後から本体を起動すると、先に用意した ID で自動登録し、その ID を持つコンテナからの次の通知が届くようになります。本体の再起動や登録の失効でも同じ ID を使い、ターミナルやコンテナの再起動は不要です。停止中の通知は保存・再送しません。
接続済みでも1秒間隔で生存確認するため、タイトルが変わらない間の本体の停止・再起動も検知します。ポートの待ち受け開始イベントではなく API のポーリングで確認します。各処理の完了から1秒後に次の確認を行い、応答が遅い場合に確認処理を重ねません。ローカル API が通常どおり応答する場合、起動後おおむね1秒程度で接続を試みます。タイムアウト待ちの間はそれより時間がかかります。
「WpfTaskBar: 通知ログを表示」で接続待ちと登録結果を確認できます。停止中はタイトル送信も待機し、接続復帰後に最新タイトルを送ります。
拡張の有効化前から動いているターミナルやコンテナには ID を追加できないため、拡張のインストール・更新後は一度開き直してください。

## aicontainer からの利用

`.aicontainer` に、次の1行を設定します。既存の `env=WPF_TASKBAR_SESSION_ID=...` はこの行に置き換えてください。

```text
env=WPF_TASKBAR_SESSION_ID
```

**値を指定しないことで、起動元ターミナルの最新のIDを自動でコンテナへ渡します。**
通常のターミナルを開き直した後やVSCode再起動後も、IDをコピーしたり設定ファイルを書き換えたりする必要はありません。
末尾に `=` を追加した `env=WPF_TASKBAR_SESSION_ID=` は空の値を指定するため、使わないでください。

通知先URLを既に `env=WPF_TASKBAR_URL=http://...` で設定している場合は、そのまま使えます。
URLもVSCodeの `wpftaskbar.containerApiUrl` から引き継ぐ場合は、URLの設定行も次へ置き換えます。

```text
env=WPF_TASKBAR_URL
```

通常の統合ターミナルのWSLシェルから、通常どおり `aicontainer` を起動します。
同じウィンドウでは共通のIDが渡ります。複数のAIを同時に使う場合は、VSCodeウィンドウを分け、それぞれの通知先を選択してください。
この変更は次回のコンテナ作成から有効です。起動済みのコンテナの環境変数は自動では変わりません。

[aicontainerの実装](https://github.com/yumayo/ai-container/blob/master/.bash_ai_container)は `env` の内容を `docker run -e` に渡します。
名前だけ指定するとホストの環境変数を渡すのは、[Dockerの `--env` の動作](https://docs.docker.com/reference/cli/docker/container/run/#set-environment-variables--e---env---env-file)です。
VSCodeの外から起動する場合など、IDを引き継いでいない環境では共通フックは通知せずに終了します。

AIコンテナ内で `WPF_TASKBAR_URL` と `WPF_TASKBAR_SESSION_ID` が設定されていることを確認し、共通フックを登録します。

### ターミナルの開き直し・VSCode再起動後

1. 拡張が有効になった後、通常の「＋」から新しいターミナルを開きます（WpfTaskBar は後から起動しても構いません）。
2. そのターミナルのWSLシェルから `aicontainer` を起動します。

この2操作でウィンドウ共通のIDが自動で渡ります。ターミナルを開き直すだけではIDは変わりません。`.aicontainer` やAIのフック設定の編集は不要です。
VSCode再起動・再読み込み時はセッションを登録し直します。復元された古いターミナルの環境変数は更新できないため、上の操作で新しく開いてください。
WpfTaskBarの再起動などでIDが失効した場合は、1秒間隔の定期処理で同じ通知先・同じIDへ再登録します。この場合はターミナルとコンテナを起動し直す必要はありません。

## docker compose からの利用

例えば既存の `ai` サービスに次を追加します。`curl` もコンテナへインストールしてください。

```yaml
services:
  ai:
    environment:
      WPF_TASKBAR_URL: ${WPF_TASKBAR_URL}
      WPF_TASKBAR_SESSION_ID: ${WPF_TASKBAR_SESSION_ID}
    volumes:
      - ./VSCodeExtension/scripts/wpftaskbar.py:/opt/taskbar/wpftaskbar.py:ro
```

通常の統合ターミナルで次のように起動すると、ウィンドウ共通の通知先をコンテナに渡せます。

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

`scripts/wpftaskbar.py` だけをコンテナへコピーするか、読み取り専用でマウントします。実行にはPython 3.8以降と `curl` が必要です。
AI のフック機構に次のコマンドを登録してください。どのランチャーでも同じスクリプトを使えます。
旧版の `taskbar-status.sh` を登録済みの場合は、使用するAIの導入スクリプトを再実行すると `python3 .../wpftaskbar.py` に移行します。手動設定では、配置するファイルと各フックのコマンドを下記に置き換えてください。

| タイミング | コマンド |
| --- | --- |
| ユーザーの依頼を受け付けたとき | `python3 /opt/taskbar/wpftaskbar.py running` |
| AI が応答を終えたとき | `python3 /opt/taskbar/wpftaskbar.py completed` |
| ツール実行の直前（進捗文のみ更新） | `python3 /opt/taskbar/wpftaskbar.py activity` |
| 質問・承認待ち | `python3 /opt/taskbar/wpftaskbar.py waiting` |
| 回答・承認後の再開 | `python3 /opt/taskbar/wpftaskbar.py resume` |
| Escなどによる中断 | `python3 /opt/taskbar/wpftaskbar.py interrupted 2` |
| セッション終了・表示解除 | `python3 /opt/taskbar/wpftaskbar.py none` |

開始と完了は**AI の応答単位**で呼びます。CLI / コンテナの起動と終了に設定すると、その生存期間の表示になってしまいます。
通知用の環境変数がない場合は何もせず終了するため、同じフック設定を VSCode 以外でも使用できます。
通信失敗は標準エラーに出し、最大 5 秒で終了します。通知失敗で AI 本体を停止させません。
第2引数で通信の上限を1〜5秒に短縮できます。例: `python3 /opt/taskbar/wpftaskbar.py none 2`。

### Codex CLI のユーザー共通フック

Codex のフックからも同じ `wpftaskbar.py` を使えます。
[公式 OpenAI Docs の Hooks](https://learn.chatgpt.com/docs/hooks) を参照した既存設定に、質問・承認待ちと再開のフックを追加します。
Codex CLI の `hooks` 機能が必要です。外部ドキュメントへ接続できない開発環境では、導入済みCLIのイベント定義も確認しています。

| Codexイベント | 通知する状態 |
| --- | --- |
| `SessionStart`（開始理由を限定しない） | `none`（状態と以前の応答文を解除） |
| `UserPromptSubmit` | `running` |
| `PreToolUse`（全ツール） | `activity`（状態を変えず進捗文を更新） |
| `PreToolUse`（`request_user_input` / `request_permissions`） | `waiting` |
| `PermissionRequest` | `waiting` |
| `PostToolUse`（質問への回答・承認後を含む） | `resume`（実行中・待ちの間だけ `running` に戻す） |
| `Stop` | `completed` |
| `Interrupt`（Escなど） | `interrupted` |
| `SessionEnd` | `none` |

`SessionStart` は `matcher` を省略し、すべての開始理由を対象にします。`type` は実行方式を表すため `"command"` を指定します。この設定ではクリア以外の通常起動・再開・圧縮時にも状態と応答文を解除します。

#### 手動で設定する場合

設定する場所は、**Codexを動かすAIコンテナ内**です。
ユーザー共通の設定ファイルは `~/.codex/hooks.json` です。`CODEX_HOME` を指定している場合は `$CODEX_HOME/hooks.json` を使います。
同じCodex設定ディレクトリを使うプロジェクトで共通に有効になります。

まずリポジトリルートで、通知スクリプトを共通の場所へコピーします。`curl` とPython 3.8以降を使用します。

```sh
taskbar_codex_dir="${CODEX_HOME:-$HOME/.codex}"
mkdir -p "$taskbar_codex_dir/hooks/wpftaskbar"
cp VSCodeExtension/scripts/wpftaskbar.py "$taskbar_codex_dir/hooks/wpftaskbar/wpftaskbar.py"
printf '設定ファイル: %s\n' "$taskbar_codex_dir/hooks.json"
printf '通知スクリプト: %s\n' "$taskbar_codex_dir/hooks/wpftaskbar/wpftaskbar.py"
```

表示された `hooks.json` をエディターで開き、次のJSONを保存します。
以下はホームが `/home/ubuntu`、`CODEX_HOME` が未設定の場合の例です。**各フックのスクリプトのパスを、上で表示された実際の絶対パスに合わせてください。**
既存の設定がある場合は上書きせず、既存の `hooks` に追加します。以前のWpfTaskBar用フックは新しい定義に置き換えてください。同じイベントが既にあれば、その配列へ今回の `{"hooks": [...]}` を追加してください。

```json
{
  "hooks": {
    "SessionStart": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' none 2",
        "timeout": 3
      }]
    }],
    "UserPromptSubmit": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' running",
        "timeout": 10
      }]
    }],
    "PreToolUse": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' activity",
        "timeout": 10
      }]
    }, {
      "matcher": "(^|.*[.:/])(request_user_input|request_permissions)$",
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' waiting",
        "timeout": 10
      }]
    }],
    "PermissionRequest": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' waiting",
        "timeout": 10
      }]
    }],
    "PostToolUse": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' resume",
        "timeout": 10
      }]
    }],
    "Stop": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' completed",
        "timeout": 10
      }]
    }],
    "Interrupt": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' interrupted 2",
        "timeout": 3
      }]
    }],
    "SessionEnd": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.codex/hooks/wpftaskbar/wpftaskbar.py' none 2",
        "timeout": 3
      }]
    }]
  }
}
```

この設定でCodexがイベントごとに `wpftaskbar.py` を実行します。
スクリプトは環境変数 `WPF_TASKBAR_URL` と `WPF_TASKBAR_SESSION_ID` を読み、WindowsのWpfTaskBarへ状態をHTTPで通知します。
URLと通知IDは通常の統合ターミナルからコンテナへ引き継ぎ、JSONには固定で書き込みません。

Codexを起動し直し、**`/hooks` で8イベントの通知フックを確認して信頼**してください。
これはCodex本体のフック実行要件です。既存の `config.toml` にも同じ通知を設定している場合は重複を解消します。
フックは標準出力に何も書かず、通知に失敗しても通常終了します。クリア・中断・終了用は通信上限を2秒、フックの期限を3秒にしています。

#### 導入スクリプトで設定する場合

`scripts/install-codex-hooks.py` は、上のコピーとJSONへの登録を自動で行う導入用スクリプトです。
既存のフックを保持し、変更前のJSONを `.bak` ファイルへ保存します。同じmatcher・コマンドは二重登録しません。旧版の `Interrupt → none` は `interrupted` に置き換え、競合する通知を残しません。
既存環境でも再実行すると、開始理由を限定しない `SessionStart` フックが追加されます。以前のWpfTaskBar用 `SessionStart` に `"matcher": "clear"` がある場合は、再実行前にその行を削除してください。同じ通知の重複登録を防げます。
通知コマンドには配置先の絶対パスを設定します。`config.toml` の変更や `/hooks` の信頼確認は行いません。

Python 3.8以降がある場合、手動設定の代わりに次のコマンドを使えます。

```sh
python3 VSCodeExtension/scripts/install-codex-hooks.py
```

実行後は手動設定と同様に、Codexを起動し直して `/hooks` で確認・信頼します。
導入用のテンプレートは [examples/codex-hooks.json](examples/codex-hooks.json) です。パスのプレースホルダーは導入スクリプトが置き換えます。
各ターンの状態通知と最新の応答文の抽出は、単一の `wpftaskbar.py` が行います。導入スクリプトは各ターンには実行しません。

`aicontainer` を使う場合も設定先はコンテナ内のCodexです。
設定ディレクトリが永続化されていない構成では、コンテナを作り直すと再登録が必要です。

初回は通常の統合ターミナルから起動し、短い依頼を2回送り、毎回「実行中 → 完了」に変わることを確認してください。
質問ツール・承認ダイアログで「待ち」、回答後に「実行中」、Escで「中断」、通常終了で状態表示が解除されることも確認します。通知IDが未設定の普通のターミナルでは、フックは通知せず終了します。
`Stop` は応答の停止タイミングを示し、作業の成功を保証するイベントではありません。
他の `Stop` フックで応答を継続させる構成では、処理が続いていても完了表示になる場合があります。

`waiting` は処理を止める質問ツール・承認要求のフックで判定します。通常の応答文に含まれる問いかけは `Stop` だけでは完了と区別できません。
処理を止めずに質問を出す `request_user_input_async` は、この質問待ちmatcherの対象外です。

#### `/clear` の反映を確認する場合

1. 上の導入コマンドを再実行するか、手動設定例の `SessionStart` を既存の `hooks.json` に追加します。
2. 通常の統合ターミナルからCodexを起動し直し、`/hooks` で `SessionStart` が開始理由を限定しない設定、通知スクリプトの引数が `none 2` になっていることを確認し、フックを有効・信頼済みにします。
3. 短い依頼を送り、タスクバーに完了状態と応答文が出た後で `/clear` を実行します。**次の依頼を送る前に**、以前の応答文と状態表示が解除されるか確認します。
4. 表示が残る場合は、次の依頼を送った時点で変わるかを確認します。`UserPromptSubmit` でも以前の応答文を解除するため、次の入力後に消えただけでは `/clear` 直後の反映を確認したことにはなりません。

### Claude Code のユーザー共通フック

Claude Codeでも同じ `wpftaskbar.py` を使います。
[公式のフック仕様](https://code.claude.com/docs/en/hooks)に合わせ、次のイベントを登録します。

| Claude Codeイベント | 通知する状態 |
| --- | --- |
| `UserPromptSubmit` | `running` |
| `PreToolUse`（全ツール） | `activity`（状態を変えず進捗文を更新） |
| `PreToolUse`（`AskUserQuestion` / `ExitPlanMode`） | `waiting` |
| `PermissionRequest` | `waiting` |
| `PostToolUse` | `resume`（実行中・待ちの間だけ `running` に戻す） |
| `PostToolUseFailure` | `is_interrupt: true` なら `interrupted`、それ以外は `resume` |
| `Elicitation` / `ElicitationResult`（MCPの質問・回答） | `waiting` / `resume` |
| `Stop` | `completed` |
| `StopFailure`（APIエラーで応答が終了） | `interrupted` |
| `SessionEnd`（終了・会話のクリアなど） | `none` |

#### 手動で設定する場合

設定先は**Claude Codeを動かすAIコンテナ内**の `~/.claude/settings.json` です。
`CLAUDE_CONFIG_DIR` を指定している場合は `$CLAUDE_CONFIG_DIR/settings.json` を使います。
同じClaude Code設定ディレクトリを使うプロジェクトに共通で適用されます。

リポジトリルートで、通知スクリプトを共通の場所へコピーします。

```sh
taskbar_claude_dir="${CLAUDE_CONFIG_DIR:-$HOME/.claude}"
mkdir -p "$taskbar_claude_dir/hooks/wpftaskbar"
cp VSCodeExtension/scripts/wpftaskbar.py "$taskbar_claude_dir/hooks/wpftaskbar/wpftaskbar.py"
printf '設定ファイル: %s\n' "$taskbar_claude_dir/settings.json"
printf '通知スクリプト: %s\n' "$taskbar_claude_dir/hooks/wpftaskbar/wpftaskbar.py"
```

表示された `settings.json` をエディターで開き、次のJSONを追加します。
以下はホームが `/home/ubuntu`、`CLAUDE_CONFIG_DIR` が未設定の場合の例です。**各フックのスクリプトのパスを実際の絶対パスに合わせてください。**
既存の権限・環境変数・他のフック設定は残し、以前のWpfTaskBar用フックは新しい定義に置き換えます。同じイベントが既にある場合は、その配列へ今回の `{"hooks": [...]}` を追加してください。

```json
{
  "hooks": {
    "UserPromptSubmit": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' running",
        "timeout": 10
      }]
    }],
    "PreToolUse": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' activity",
        "timeout": 10
      }]
    }, {
      "matcher": "^(AskUserQuestion|ExitPlanMode)$",
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' waiting",
        "timeout": 10
      }]
    }],
    "PermissionRequest": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' waiting",
        "timeout": 10
      }]
    }],
    "PostToolUse": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' resume",
        "timeout": 10
      }]
    }],
    "PostToolUseFailure": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' tool-failed 2",
        "timeout": 3
      }]
    }],
    "Elicitation": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' waiting",
        "timeout": 10
      }]
    }],
    "ElicitationResult": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' resume",
        "timeout": 10
      }]
    }],
    "Stop": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' completed",
        "timeout": 10
      }]
    }],
    "StopFailure": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' interrupted 2",
        "timeout": 3
      }]
    }],
    "SessionEnd": [{
      "hooks": [{
        "type": "command",
        "command": "python3 '/home/ubuntu/.claude/hooks/wpftaskbar/wpftaskbar.py' none 2",
        "timeout": 3
      }]
    }]
  }
}
```

通知用の `WPF_TASKBAR_URL` と `WPF_TASKBAR_SESSION_ID` は環境変数から読みます。JSONに固定で書き込む必要はありません。
Claude Codeを起動し直し、**`/hooks` で10イベントのフックと配置先を確認**してください。Claude Codeの `/hooks` は設定を閲覧するためのメニューです。

#### 導入スクリプトで設定する場合

Python 3.8以降があれば、コピーとJSONへの登録を次のコマンドで行えます。

```sh
python3 VSCodeExtension/scripts/install-claude-hooks.py
```

導入スクリプトは `settings.json` の既存設定を保持し、変更前のファイルを `.bak` に保存します。
同じmatcher・コマンドの重複登録を防ぎ、通知スクリプトは再実行で更新できます。旧版の `StopFailure → none` は `interrupted` に置き換えます。
`permissions`、`env`、`disableAllHooks` などの既存設定は変更しません。
テンプレートは [examples/claude-hooks.json](examples/claude-hooks.json) です。

実行後にClaude Codeを起動し直し、`/hooks` で確認します。状態通知・進捗文の抽出・`PostToolUseFailure` のJSON判定を `wpftaskbar.py` で行い、通信には `curl` を使います。
`aicontainer` やDocker ComposeではClaude Code設定ディレクトリを永続化してください。永続化されていなければコンテナ再作成後に再登録します。

#### 動作確認と中断時の扱い

開発環境のClaude Code `2.1.291` のイベント定義で確認しています。登録後は短い依頼を2回送り、毎回「実行中 → 完了」に変わることと、通常終了で表示が消えることを確認してください。
`Stop` は応答が終わったことを表し、作業成功の判定には使いません。他の停止フックが応答を継続させる場合は、処理中でも完了表示になる場合があります。

Claude Codeには `Interrupt` フックがないため、VSCode拡張がセッション登録済みのウィンドウの統合ターミナル内でEscを補足します。
Escをターミナルへ1回送ってから、そのセッションが `running` / `waiting` の場合だけ `interrupted` を通知します。通知APIが停止していてもEscの転送を遅らせません。
検索欄・補完候補・アクセシブルバッファが開いているときや、セッション未登録時は、このキーバインドを適用しません。
ツール実行中の中断は `PostToolUseFailure.is_interrupt` でも検知します。中断後に遅れて届く通常のツール結果では実行中に戻しません。

このEsc連携はCodexでも共通です。AIの待機中にEscを押しても、未開始・完了・中断済みの状態は変更しません。
ターミナル内の別のTUIでEscを使う場合も、AIが実行中・質問待ちなら中断として扱います。拡張が接続を失った場合や独自のキーバインドで上書きした場合、Claude Codeの生成中のEscは自動検知できません。
通常の応答文に質問を書いて終了するケースは、CLIの `Stop` イベントだけでは判別できません。質問待ちの自動表示は上記の質問・承認フックが対象です。

動作確認では、質問への回答前後で「待ち → 実行中」、生成中とツール実行中のEscで「中断」、次の依頼で「実行中」に変わることを確認します。
必要に応じて、同じ通知用環境変数を引き継いだシェルから手動通知もできます。

```sh
python3 "${CLAUDE_CONFIG_DIR:-$HOME/.claude}/hooks/wpftaskbar/wpftaskbar.py" interrupted 2
```

既存設定で `disableAllHooks` が有効になっている場合や、管理設定でユーザーフックが制限されている場合は、その設定も確認してください。

他のAIでも、依頼開始・応答完了のフックへ同じコマンドを登録できます。

## 最新の進捗文とターミナルタイトルの表示

セッション登録済みのウィンドウは、通常タスク2つ分の高さ（76px）を使います。上半分はウィンドウ名、下半分は最新のAIの応答文を最大2行で表示します。
Codex / Claude Codeのフック入力にある `transcript_path` から、最新の公開メッセージを取り出します。途中の進捗コメントも対象です。ツール実行前後・質問待ち・応答終了などのフック発生時に更新し、ツールを使わず文章を生成している間は応答終了時に反映されます。
Markdownの見出し・強調や改行を整え、先頭120文字以内に切り詰めます。新たに要約を生成する処理はありません。ツール結果・思考・ユーザーの依頼文は送信しません。応答完了フックの `last_assistant_message` がある場合は、その本文を優先します。
読み取りはログ末尾512 KiBまでです。ログがない、読み取り途中、未対応の形式などで本文を取得できない場合も、状態通知は継続します。進捗文がまだない場合はターミナルタイトルを表示します。
進捗文はターミナルタイトルとは別に保持するため、VSCode側のタイトル更新では上書きされません。次の依頼開始とセッション終了時に解除し、完了・中断時は最後の文言を残します。遅れて届いたツール通知は完了・中断後の表示を変更しません。
タスクリストを右クリック → 「オプション」 → 「AI用の表示欄を表示する」で下段全体を非表示にできます。

### 進捗文がない場合のタイトル

タイトルはVSCodeのアクティブな統合ターミナルの `Terminal.name` を使います。ターミナルを切り替えると、そのタイトルへ切り替わります。シェルやAIがOSCで変更したタイトル、VSCodeで手動変更したタイトルに追従します。
安定版VSCode APIにはタイトル変更専用イベントがないため、1秒ごとに差分を確認して通知します。通信中の変更も順番に送り、失敗時は再送します。AIフックとは独立しているので、質問待ち・中断中も更新されます。
手動でターミナルを固定名に変更した場合は、その名前が表示されます。
`none` やターミナルをすべて閉じた場合も、ウィンドウのセッション登録がある間は下段を維持します。アクティブなターミナルがなければタイトルを空にします。拡張の停止や登録の失効で通常の高さに戻ります。

既にタイトル連携を導入している環境では、WpfTaskBarを再ビルドし、使用するAIのフック導入スクリプトを再実行してください。`wpftaskbar.py` が更新され、全ツール用の `PreToolUse` フックが追加されます。VSCode拡張も上記のVSIXへ更新してください。AIを再起動して `/hooks` で確認（Codexは信頼も必要）した後、通知先の登録後に通常のターミナルとコンテナを起動し直します。

## 表示と通知の寿命

- 1ウィンドウ1セッションです。ターミナルの作成・終了では ID を発行・削除しません。同じウィンドウで複数の AI を同時に動かすと、通知が同じ状態を上書きします。
- `none` はウィンドウのAI状態と進捗文を解除します。登録は維持します。
- 拡張は1秒間隔（前回の処理完了から1秒後）で生存通知を送り、2分間届かなければ登録と状態が失効します。拡張の停止時に登録を解除します。
- 起動時の接続失敗や登録の失効後は自動で再登録を試みます。選択済みウィンドウが消えた場合は別ウィンドウへ自動で切り替えず、手動選択が必要です。
- WpfTaskBarの再起動・失効後の再登録では ID と環境変数を維持します。VSCodeの再起動・再読み込み、通知先の変更では ID が変わるため、既存のターミナルとコンテナは起動し直してください。通知先URLを変更した場合も同様です。`env=WPF_TASKBAR_SESSION_ID` の設定なら、新しい ID が自動で渡ります。
- 従来の `POST /tasks/status` の状態は独立して保持し、`waiting` → `running` → `interrupted` → `completed` → `none` の順で集約します。従来のAPIで設定した状態の解除には、従来のAPIから `none` を送ります。

## 疎通確認

コンテナ内から次を実行し、選択した VSCode タスクの状態表示が順に変わることを確認します。

```sh
python3 /opt/taskbar/wpftaskbar.py running
python3 /opt/taskbar/wpftaskbar.py waiting
python3 /opt/taskbar/wpftaskbar.py running
python3 /opt/taskbar/wpftaskbar.py interrupted
python3 /opt/taskbar/wpftaskbar.py completed
python3 /opt/taskbar/wpftaskbar.py none
```

同じ VSCode ウィンドウで2つの通常のターミナルを開き、`WPF_TASKBAR_SESSION_ID` が同じこと、片方を閉じても残ったターミナルから通知できることを確認します。別の VSCode ウィンドウでは通知先をそのウィンドウに選択し、IDと状態が分かれることも確認します。

接続できない場合は、コンテナから Windows の TCP 5000 番への到達性と URL を確認してください。
セッション操作の HTTP 404 は通知先の失効を示し、拡張が自動で再登録します。「WpfTaskBar: 通知ログを表示」から接続待ち・登録結果や設定/APIのエラーを確認できます。接続拒否のエラー文字列は表示しません。

## API と開発

| メソッド / パス | 用途 |
| --- | --- |
| `POST /tasks/sessions` | `{ "handle": 123, "processId": 456, "sessionId": "32文字の小文字16進数" }` で登録。同じ ID・通知先なら状態を保持して期限を延長し、別の通知先で使用中の ID は409。同じ ID が失効済みなら再登録。`sessionId` 省略時は従来どおりサーバーが発行 |
| `POST /tasks/sessions/{id}/status` | `{ "status": "running" }` などを送る。任意の `activityText`（最大512文字）を同時に更新可能。`onlyIfActive: true` を付けると、現在が `running` / `waiting` の場合だけ変更する |
| `PUT /tasks/sessions/{id}/title` | `{ "terminalTitle": "続行しますか？" }` でタイトルだけを更新（最大4096文字、空文字で解除）。状態・有効期限は変えない |
| `PUT /tasks/sessions/{id}/activity` | `{ "activityText": "設定ファイルを確認しています。" }` で進捗文だけを更新（最大512文字、空文字で解除）。現在が `running` / `waiting` の場合のみ更新し、状態・有効期限は変えない |
| `PUT /tasks/sessions/{id}/heartbeat` | 拡張から有効期限を延長する |
| `DELETE /tasks/sessions/{id}` | 拡張の停止時・通知先の変更時に登録を解除する（繰り返し可能） |

`resume` は内部フック用コマンドで、`running` と `onlyIfActive: true` を送ります。`tool-failed` も条件付き通知のため、EscやStopより後にツール通知が届いても中断・完了を取り消しません。

API は状態の集約、ウィンドウの生存確認、有効期限を担当します。状態・進捗文・タイトルの通知だけでは有効期限は延長しません。
`activityText` の省略は原則として現在の文言を保持します。通常の `running` 通知（`onlyIfActive` なし）では新しい依頼として解除し、`none` は常に解除します。
`GET /tasks` とWebView向けのタスク一覧には `status` に加えて `hasAiTask`、`terminalTitle`、`activityText` を返します。
登録時・更新時は HWND と PID の両方を確認します。セッションはディスクに保存しません。

拡張はVSCode起動時に有効化し、ランダムなウィンドウ共通の通知 ID を用意してから接続・登録を試みます。
`ExtensionContext.environmentVariableCollection` で通常の統合ターミナルへ環境変数を注入します。古いIDを次の起動に持ち越さないよう、コレクションは永続化しません。
起動・設定変更・生存通知を直列化し、重複登録を防ぎます。拡張の停止と登録が重なった場合も、遅れて返された登録を解除します。

```sh
npm --prefix VSCodeExtension run build
npm --prefix VSCodeExtension test
npm --prefix VSCodeExtension run check
dotnet test Tests/TaskStatus.Tests/TaskStatus.Tests.csproj
```

Node.js のテストは VSCode API を差し替えた動作確認とローカル HTTP の通信確認を含みます。
ループバック通信が禁止された環境に限り、`WPF_TASKBAR_SKIP_NETWORK_TESTS=1 npm --prefix VSCodeExtension test` で通信テスト 3 件を明示的にスキップできます。
WSL の環境変数転送、実際の VSCode / WpfTaskBar の表示、AI のフック発火は Windows 実環境で上記の疎通確認を行ってください。

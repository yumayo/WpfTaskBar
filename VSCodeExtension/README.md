# WpfTaskBar AI Status

VSCode の統合ターミナルで起動した AI コンテナから、依頼ごとの実行中 / 質問・承認待ち / 中断 / 応答完了と、最新の進捗文を WpfTaskBar に表示します。進捗文がない場合はターミナルタイトルを表示します。
Windows 側の VSCode にインストールする拡張です。通常の WSL ターミナルと Remote - WSL の両方を想定しています。

```text
VSCode 拡張 → 通知付きターミナルを作成（ウィンドウと通知 ID を登録）
                  ↓ 環境変数を引き継ぐ
                WSL → aicontainer / docker compose → AI のフック
                                                        ↓ HTTP
Windows の WpfTaskBar ← ターミナルごとの状態を集約 ← running / waiting / interrupted / completed
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

Windows 側の VSCode の「拡張機能: VSIX からのインストール」で、生成された `wpftaskbar-ai-status-0.1.1.vsix` を選択してください。
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
開くシェルは `wpftaskbar.shellPath` と `wpftaskbar.shellArgs` で指定できます。この設定は専用コマンドと次のプロファイルの両方で使います。未指定の場合は VSCode のシェル選択に従います。

### 標準の「＋」やショートカットから開く

ターミナルの「＋」の横にあるメニューから **AI (WpfTaskBar)** を選択します。
専用コマンドの「AI 通知付きターミナルを開く」と同じ通知機能を持つ、通常の統合ターミナルが開きます。

普段の「＋」や「ターミナル: 新しいターミナルの作成」から使うには、コマンドパレットの **ターミナル: 既定のプロファイルの選択** で **AI (WpfTaskBar)** を選んでください。
以後、新しく開くたびに個別の通知 ID が環境変数へ設定されます。初回の通知先ウィンドウ選択、環境変数の引き継ぎ、AI のフック設定は専用コマンドと共通です。

Windows のローカルウィンドウから WSL の Ubuntu を起動する設定例です。ディストリビューション名は使用環境に合わせて変更してください。

```json
{
  "terminal.integrated.defaultProfile.windows": "AI (WpfTaskBar)",
  "wpftaskbar.shellPath": "wsl.exe",
  "wpftaskbar.shellArgs": ["-d", "Ubuntu"]
}
```

Remote - WSL のウィンドウでは、シェルにリモート側のパスを指定します。

```json
{
  "terminal.integrated.defaultProfile.linux": "AI (WpfTaskBar)",
  "wpftaskbar.shellPath": "/bin/bash",
  "wpftaskbar.shellArgs": []
}
```

既定を **AI (WpfTaskBar)** に変更しても、それまで使っていたプロファイルのシェル・引数は自動ではコピーされません。WSL のディストリビューションやシェルを固定したい場合は上記の設定を指定してください。
既存の PowerShell / WSL プロファイルを直接選んだ場合や、既に起動しているターミナルには通知用の環境変数を追加しません。
WpfTaskBar に接続できない場合はエラーを表示します。通知先の選択をキャンセルすると起動を中止します。

## aicontainer からの利用

`.aicontainer` に、次の1行を設定します。既存の `env=WPF_TASKBAR_SESSION_ID=...` はこの行に置き換えてください。

```text
env=WPF_TASKBAR_SESSION_ID
```

**値を指定しないことで、起動元ターミナルの最新のIDを自動でコンテナへ渡します。**
通知付きターミナルを開き直した後やVSCode再起動後も、IDをコピーしたり設定ファイルを書き換えたりする必要はありません。
末尾に `=` を追加した `env=WPF_TASKBAR_SESSION_ID=` は空の値を指定するため、使わないでください。

通知先URLを既に `env=WPF_TASKBAR_URL=http://...` で設定している場合は、そのまま使えます。
URLもVSCodeの `wpftaskbar.containerApiUrl` から引き継ぐ場合は、URLの設定行も次へ置き換えます。

```text
env=WPF_TASKBAR_URL
```

通知付きターミナルのWSLシェルから、通常どおり `aicontainer` を起動します。
ターミナルごとに異なるIDが渡るため、同じ `.aicontainer` を複数のターミナルで使えます。
この変更は次回のコンテナ作成から有効です。起動済みのコンテナの環境変数は自動では変わりません。

[aicontainerの実装](https://github.com/yumayo/ai-container/blob/master/.bash_ai_container)は `env` の内容を `docker run -e` に渡します。
名前だけ指定するとホストの環境変数を渡すのは、[Dockerの `--env` の動作](https://docs.docker.com/reference/cli/docker/container/run/#set-environment-variables--e---env---env-file)です。
通知付きターミナル以外ではIDが渡らず、共通フックは通知せずに終了します。

AIコンテナ内で `WPF_TASKBAR_URL` と `WPF_TASKBAR_SESSION_ID` が設定されていることを確認し、共通フックを登録します。

### ターミナルの開き直し・VSCode再起動後

1. **AI (WpfTaskBar)** プロファイル（既定に設定済みなら「＋」）または **WpfTaskBar: AI 通知付きターミナルを開く** から、新しいターミナルを開きます。
2. そのターミナルのWSLシェルから `aicontainer` を起動します。

この2操作で新しいIDが自動で渡ります。`.aicontainer` やAIのフック設定の編集は不要です。
現在の拡張は、VSCodeが復元した古いターミナルの通知登録を引き継ぎません。再起動・再読み込み後は上の操作で新しく開いてください。
WpfTaskBarの再起動などでIDが失効した場合も同じ手順です。

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
      - ./VSCodeExtension/scripts/taskbar-message.py:/opt/taskbar/taskbar-message.py:ro
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

`scripts/taskbar-status.sh` と `scripts/taskbar-message.py` をコンテナの同じディレクトリへコピーするか、読み取り専用でマウントします。進捗文の抽出にはPython 3.8以降が必要です。
AI のフック機構に次のコマンドを登録してください。どのランチャーでも同じスクリプトを使えます。

| タイミング | コマンド |
| --- | --- |
| ユーザーの依頼を受け付けたとき | `sh /opt/taskbar/taskbar-status.sh running` |
| AI が応答を終えたとき | `sh /opt/taskbar/taskbar-status.sh completed` |
| ツール実行の直前（進捗文のみ更新） | `sh /opt/taskbar/taskbar-status.sh activity` |
| 質問・承認待ち | `sh /opt/taskbar/taskbar-status.sh waiting` |
| 回答・承認後の再開 | `sh /opt/taskbar/taskbar-status.sh resume` |
| Escなどによる中断 | `sh /opt/taskbar/taskbar-status.sh interrupted 2` |
| セッション終了・表示解除 | `sh /opt/taskbar/taskbar-status.sh none` |

開始と完了は**AI の応答単位**で呼びます。CLI / コンテナの起動と終了に設定すると、その生存期間の表示になってしまいます。
通知用の環境変数がない場合は何もせず終了するため、同じフック設定を VSCode 以外でも使用できます。
通信失敗は標準エラーに出し、最大 5 秒で終了します。通知失敗で AI 本体を停止させません。
第2引数で通信の上限を1〜5秒に短縮できます。例: `sh /opt/taskbar/taskbar-status.sh none 2`。

### Codex CLI のユーザー共通フック

Codex のフックからも同じ `taskbar-status.sh` を使えます。
[公式 OpenAI Docs の Hooks](https://learn.chatgpt.com/docs/hooks) を参照した既存設定に、質問・承認待ちと再開のフックを追加します。
Codex CLI の `hooks` 機能が必要です。外部ドキュメントへ接続できない開発環境では、導入済みCLIのイベント定義も確認しています。

| Codexイベント | 通知する状態 |
| --- | --- |
| `UserPromptSubmit` | `running` |
| `PreToolUse`（全ツール） | `activity`（状態を変えず進捗文を更新） |
| `PreToolUse`（`request_user_input` / `request_permissions`） | `waiting` |
| `PermissionRequest` | `waiting` |
| `PostToolUse`（質問への回答・承認後を含む） | `resume`（実行中・待ちの間だけ `running` に戻す） |
| `Stop` | `completed` |
| `Interrupt`（Escなど） | `interrupted` |
| `SessionEnd` | `none` |

#### 手動で設定する場合

設定する場所は、**Codexを動かすAIコンテナ内**です。
ユーザー共通の設定ファイルは `~/.codex/hooks.json` です。`CODEX_HOME` を指定している場合は `$CODEX_HOME/hooks.json` を使います。
同じCodex設定ディレクトリを使うプロジェクトで共通に有効になります。

まずリポジトリルートで、通知スクリプトを共通の場所へコピーします。`sh`、`curl`、Python 3.8以降を使用します。

```sh
taskbar_codex_dir="${CODEX_HOME:-$HOME/.codex}"
mkdir -p "$taskbar_codex_dir/hooks/wpftaskbar"
cp VSCodeExtension/scripts/taskbar-status.sh "$taskbar_codex_dir/hooks/wpftaskbar/taskbar-status.sh"
cp VSCodeExtension/scripts/taskbar-message.py "$taskbar_codex_dir/hooks/wpftaskbar/taskbar-message.py"
printf '設定ファイル: %s\n' "$taskbar_codex_dir/hooks.json"
printf '通知スクリプト: %s\n' "$taskbar_codex_dir/hooks/wpftaskbar/taskbar-status.sh"
```

表示された `hooks.json` をエディターで開き、次のJSONを保存します。
以下はホームが `/home/ubuntu`、`CODEX_HOME` が未設定の場合の例です。**各フックのスクリプトのパスを、上で表示された実際の絶対パスに合わせてください。**
既存の設定がある場合は上書きせず、既存の `hooks` に追加します。以前のWpfTaskBar用フックは新しい定義に置き換えてください。同じイベントが既にあれば、その配列へ今回の `{"hooks": [...]}` を追加してください。

```json
{
  "hooks": {
    "UserPromptSubmit": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' running",
        "timeout": 10
      }]
    }],
    "PreToolUse": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' activity",
        "timeout": 10
      }]
    }, {
      "matcher": "(^|.*[.:/])(request_user_input|request_permissions)$",
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' waiting",
        "timeout": 10
      }]
    }],
    "PermissionRequest": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' waiting",
        "timeout": 10
      }]
    }],
    "PostToolUse": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' resume",
        "timeout": 10
      }]
    }],
    "Stop": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' completed",
        "timeout": 10
      }]
    }],
    "Interrupt": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' interrupted 2",
        "timeout": 3
      }]
    }],
    "SessionEnd": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.codex/hooks/wpftaskbar/taskbar-status.sh' none 2",
        "timeout": 3
      }]
    }]
  }
}
```

この設定でCodexがイベントごとに `taskbar-status.sh` を実行します。
スクリプトは環境変数 `WPF_TASKBAR_URL` と `WPF_TASKBAR_SESSION_ID` を読み、WindowsのWpfTaskBarへ状態をHTTPで通知します。
URLと通知IDは通知付きターミナルからコンテナへ引き継ぎ、JSONには固定で書き込みません。

Codexを起動し直し、**`/hooks` で7イベントの通知フックを確認して信頼**してください。
これはCodex本体のフック実行要件です。既存の `config.toml` にも同じ通知を設定している場合は重複を解消します。
フックは標準出力に何も書かず、通知に失敗しても通常終了します。中断・終了用は通信上限を2秒、フックの期限を3秒にしています。

#### 導入スクリプトで設定する場合

`scripts/install-codex-hooks.py` は、上のコピーとJSONへの登録を自動で行う導入用スクリプトです。
既存のフックを保持し、変更前のJSONを `.bak` ファイルへ保存します。同じmatcher・コマンドは二重登録しません。旧版の `Interrupt → none` は `interrupted` に置き換え、競合する通知を残しません。
通知コマンドには配置先の絶対パスを設定します。`config.toml` の変更や `/hooks` の信頼確認は行いません。

Python 3.8以降がある場合、手動設定の代わりに次のコマンドを使えます。

```sh
python3 VSCodeExtension/scripts/install-codex-hooks.py
```

実行後は手動設定と同様に、Codexを起動し直して `/hooks` で確認・信頼します。
導入用のテンプレートは [examples/codex-hooks.json](examples/codex-hooks.json) です。パスのプレースホルダーは導入スクリプトが置き換えます。
各ターンの通知は `taskbar-status.sh` が行い、`taskbar-message.py` が最新の応答文を抽出します。導入スクリプトは各ターンには実行しません。

`aicontainer` を使う場合も設定先はコンテナ内のCodexです。
設定ディレクトリが永続化されていない構成では、コンテナを作り直すと再登録が必要です。

初回は通知付きターミナルから起動し、短い依頼を2回送り、毎回「実行中 → 完了」に変わることを確認してください。
質問ツール・承認ダイアログで「待ち」、回答後に「実行中」、Escで「中断」、通常終了で状態表示が解除されることも確認します。通知IDが未設定の普通のターミナルでは、フックは通知せず終了します。
`Stop` は応答の停止タイミングを示し、作業の成功を保証するイベントではありません。
他の `Stop` フックで応答を継続させる構成では、処理が続いていても完了表示になる場合があります。

`waiting` は処理を止める質問ツール・承認要求のフックで判定します。通常の応答文に含まれる問いかけは `Stop` だけでは完了と区別できません。
処理を止めずに質問を出す `request_user_input_async` は、この質問待ちmatcherの対象外です。

### Claude Code のユーザー共通フック

Claude Codeでも同じ `taskbar-status.sh` を使います。
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
cp VSCodeExtension/scripts/taskbar-status.sh "$taskbar_claude_dir/hooks/wpftaskbar/taskbar-status.sh"
cp VSCodeExtension/scripts/taskbar-message.py "$taskbar_claude_dir/hooks/wpftaskbar/taskbar-message.py"
printf '設定ファイル: %s\n' "$taskbar_claude_dir/settings.json"
printf '通知スクリプト: %s\n' "$taskbar_claude_dir/hooks/wpftaskbar/taskbar-status.sh"
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
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' running",
        "timeout": 10
      }]
    }],
    "PreToolUse": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' activity",
        "timeout": 10
      }]
    }, {
      "matcher": "^(AskUserQuestion|ExitPlanMode)$",
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' waiting",
        "timeout": 10
      }]
    }],
    "PermissionRequest": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' waiting",
        "timeout": 10
      }]
    }],
    "PostToolUse": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' resume",
        "timeout": 10
      }]
    }],
    "PostToolUseFailure": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' tool-failed 2",
        "timeout": 3
      }]
    }],
    "Elicitation": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' waiting",
        "timeout": 10
      }]
    }],
    "ElicitationResult": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' resume",
        "timeout": 10
      }]
    }],
    "Stop": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' completed",
        "timeout": 10
      }]
    }],
    "StopFailure": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' interrupted 2",
        "timeout": 3
      }]
    }],
    "SessionEnd": [{
      "hooks": [{
        "type": "command",
        "command": "sh '/home/ubuntu/.claude/hooks/wpftaskbar/taskbar-status.sh' none 2",
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

実行後にClaude Codeを起動し直し、`/hooks` で確認します。通知には `sh` と `curl` を使い、進捗文の抽出と `PostToolUseFailure` のJSON判定にはPython 3も使います。
`aicontainer` やDocker ComposeではClaude Code設定ディレクトリを永続化してください。永続化されていなければコンテナ再作成後に再登録します。

#### 動作確認と中断時の扱い

開発環境のClaude Code `2.1.291` のイベント定義で確認しています。登録後は短い依頼を2回送り、毎回「実行中 → 完了」に変わることと、通常終了で表示が消えることを確認してください。
`Stop` は応答が終わったことを表し、作業成功の判定には使いません。他の停止フックが応答を継続させる場合は、処理中でも完了表示になる場合があります。

Claude Codeには `Interrupt` フックがないため、VSCode拡張が通知付きターミナル内のEscを補足します。
Escをターミナルへ1回送ってから、そのセッションが `running` / `waiting` の場合だけ `interrupted` を通知します。通知APIが停止していてもEscの転送を遅らせません。
検索欄・補完候補・アクセシブルバッファが開いているときや通常のターミナルでは、このキーバインドを適用しません。
ツール実行中の中断は `PostToolUseFailure.is_interrupt` でも検知します。中断後に遅れて届く通常のツール結果では実行中に戻しません。

このEsc連携はCodexでも共通です。AIの待機中にEscを押しても、未開始・完了・中断済みの状態は変更しません。
ターミナル内の別のTUIでEscを使う場合も、AIが実行中・質問待ちなら中断として扱います。拡張が接続を失った場合や独自のキーバインドで上書きした場合、Claude Codeの生成中のEscは自動検知できません。
通常の応答文に質問を書いて終了するケースは、CLIの `Stop` イベントだけでは判別できません。質問待ちの自動表示は上記の質問・承認フックが対象です。

動作確認では、質問への回答前後で「待ち → 実行中」、生成中とツール実行中のEscで「中断」、次の依頼で「実行中」に変わることを確認します。
必要に応じて、同じ通知用環境変数を引き継いだシェルから手動通知もできます。

```sh
sh "${CLAUDE_CONFIG_DIR:-$HOME/.claude}/hooks/wpftaskbar/taskbar-status.sh" interrupted 2
```

既存設定で `disableAllHooks` が有効になっている場合や、管理設定でユーザーフックが制限されている場合は、その設定も確認してください。

他のAIでも、依頼開始・応答完了のフックへ同じコマンドを登録できます。

## 最新の進捗文とターミナルタイトルの表示

通知付きターミナルがあるウィンドウは、通常タスク2つ分の高さ（76px）を使います。上半分はウィンドウ名、下半分は最新のAIの応答文を最大2行で表示します。
Codex / Claude Codeのフック入力にある `transcript_path` から、最新の公開メッセージを取り出します。途中の進捗コメントも対象です。ツール実行前後・質問待ち・応答終了などのフック発生時に更新し、ツールを使わず文章を生成している間は応答終了時に反映されます。
Markdownの見出し・強調や改行を整え、先頭120文字以内に切り詰めます。新たに要約を生成する処理はありません。ツール結果・思考・ユーザーの依頼文は送信しません。応答完了フックの `last_assistant_message` がある場合は、その本文を優先します。
読み取りはログ末尾512 KiBまでです。ログがない、読み取り途中、未対応の形式などで本文を取得できない場合も、状態通知は継続します。進捗文がまだない場合はターミナルタイトルを表示します。
進捗文はターミナルタイトルとは別に保持するため、VSCode側のタイトル更新では上書きされません。次の依頼開始とセッション終了時に解除し、完了・中断時は最後の文言を残します。遅れて届いたツール通知は完了・中断後の表示を変更しません。
タスクリストを右クリック → 「オプション」 → 「AI用の表示欄を表示する」で下段全体を非表示にできます。

### 進捗文がない場合のタイトル

タイトルはVSCodeの `Terminal.name` を使い、固定の `AI (WpfTaskBar)` 名で上書きしません。シェルやAIがOSCで変更したタイトル、VSCodeで手動変更したタイトルに追従します。
安定版VSCode APIにはタイトル変更専用イベントがないため、1秒ごとに差分を確認して通知します。通信中の変更も順番に送り、失敗時は再送します。AIフックとは独立しているので、質問待ち・中断中も更新されます。
手動でターミナルを固定名に変更した場合は、その名前が表示されます。
`none` でも通知付きターミナルが残っていれば下段を維持し、最後のターミナルを閉じるか登録が失効すると通常の高さに戻ります。

既にタイトル連携を導入している環境では、WpfTaskBarを再ビルドし、使用するAIのフック導入スクリプトを再実行してください。`taskbar-status.sh` と `taskbar-message.py` が一緒に更新され、全ツール用の `PreToolUse` フックが追加されます。VSCode拡張の更新は不要です。AIを再起動して `/hooks` で確認（Codexは信頼も必要）した後、WpfTaskBarの再起動で失効した通知付きターミナルを開き直します。

## 表示と通知の寿命

- 状態は `waiting` → `running` → `interrupted` → `completed` → `none` の順に優先します。別ターミナルが実行中でも質問・承認待ちに気付けます。
- 下段の進捗文・タイトルも同じ優先順で選び、同じ状態なら最後に状態通知を受けたターミナルを表示します。生存通知・進捗文・タイトルの更新だけでは表示対象を切り替えません。
- `none` とターミナルを閉じる操作は、そのターミナルの状態だけを解除します。
- **1 つの通知付きターミナルにつき、同時に実行する AI は 1 つ**にしてください。複数の AI はそれぞれ別の通知付きターミナルから起動します。
- 拡張は 30 秒ごとに生存通知を送り、2 分間届かなければ登録と状態が失効します。
- VSCodeの再読み込み、拡張の停止、WpfTaskBarの再起動、長時間のスリープなどでIDが失効した場合は、通知付きターミナルを開き直してコンテナを起動します。上記の `env=WPF_TASKBAR_SESSION_ID` 設定なら、新しいIDが自動で渡ります。
- 作成前から開いていたターミナル・コンテナには自動で接続しません。
- 「WpfTaskBar: この VSCode の通知先を選択」で通知先を選び直せます。先に通知付きターミナルを閉じてください。
- 従来の `POST /tasks/status` の状態とは独立し、集約時には同じ状態優先順を使います。従来の API で設定した状態の解除には、従来の API から `none` を送ります。

## 疎通確認

コンテナ内から次を実行し、選択した VSCode タスクの状態表示が順に変わることを確認します。

```sh
sh /opt/taskbar/taskbar-status.sh running
sh /opt/taskbar/taskbar-status.sh waiting
sh /opt/taskbar/taskbar-status.sh running
sh /opt/taskbar/taskbar-status.sh interrupted
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
| `POST /tasks/sessions/{id}/status` | `{ "status": "running" }` などを送る。任意の `activityText`（最大512文字）を同時に更新可能。`onlyIfActive: true` を付けると、現在が `running` / `waiting` の場合だけ変更する |
| `PUT /tasks/sessions/{id}/title` | `{ "terminalTitle": "続行しますか？" }` でタイトルだけを更新（最大4096文字、空文字で解除）。状態・有効期限は変えない |
| `PUT /tasks/sessions/{id}/activity` | `{ "activityText": "設定ファイルを確認しています。" }` で進捗文だけを更新（最大512文字、空文字で解除）。現在が `running` / `waiting` の場合のみ更新し、状態・有効期限は変えない |
| `PUT /tasks/sessions/{id}/heartbeat` | 拡張から有効期限を延長する |
| `DELETE /tasks/sessions/{id}` | ターミナル終了時に登録を解除する（繰り返し可能） |

`resume` は内部フック用コマンドで、`running` と `onlyIfActive: true` を送ります。`tool-failed` も条件付き通知のため、EscやStopより後にツール通知が届いても中断・完了を取り消しません。

API は状態の集約、ウィンドウの生存確認、有効期限を担当します。状態・進捗文・タイトルの通知だけでは有効期限は延長しません。
`activityText` の省略は原則として現在の文言を保持します。通常の `running` 通知（`onlyIfActive` なし）では新しい依頼として解除し、`none` は常に解除します。
`GET /tasks` とWebView向けのタスク一覧には `status` に加えて `hasAiTask`、`terminalTitle`、`activityText` を返します。
登録時・更新時は HWND と PID の両方を確認します。セッションはディスクに保存しません。

標準 UI 向けには `TerminalProfileProvider` で、セッションを登録済みの起動設定を返します。
`onDidOpenTerminal` で起動設定の ID とターミナルを紐付け、その後は専用コマンドと同じ生存通知・終了時の解除を行います。
起動前のキャンセル時は登録を解除します。起動を確認できなかった登録は延命せず、2 分経過後の定期処理で破棄します。

```sh
npm --prefix VSCodeExtension run build
npm --prefix VSCodeExtension test
npm --prefix VSCodeExtension run check
dotnet test Tests/TaskStatus.Tests/TaskStatus.Tests.csproj
```

Node.js のテストは VSCode API を差し替えた動作確認とローカル HTTP の通信確認を含みます。
ループバック通信が禁止された環境に限り、`WPF_TASKBAR_SKIP_NETWORK_TESTS=1 npm --prefix VSCodeExtension test` で通信テスト 3 件を明示的にスキップできます。
WSL の環境変数転送、実際の VSCode / WpfTaskBar の表示、AI のフック発火は Windows 実環境で上記の疎通確認を行ってください。

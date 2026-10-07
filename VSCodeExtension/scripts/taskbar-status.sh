#!/bin/sh
# AI の開始 / 応答完了フックから呼ぶ。通知失敗で AI 本体を停止させない。
set -u

taskbar_condition=''
case "${1:-}" in
  running|waiting|interrupted|completed|none) status=$1 ;;
  activity) status='' ;;
  resume|tool-failed) status=running; taskbar_condition=',"onlyIfActive":true' ;;
  *) echo "Usage: taskbar-status.sh running|waiting|interrupted|completed|none|resume|tool-failed|activity [timeout-seconds: 1-5]" >&2; exit 2 ;;
esac

# 終了・中断フックの短い実行期限にも収まるよう、呼び出し側で短縮できる。
case "${2:-5}" in
  [1-5]) taskbar_timeout=${2:-5} ;;
  *) echo "WpfTaskBar: timeout must be between 1 and 5 seconds" >&2; exit 2 ;;
esac

# 通知付きターミナル以外では何もしない。
if [ -z "${WPF_TASKBAR_URL:-}" ] || [ -z "${WPF_TASKBAR_SESSION_ID:-}" ]; then
  exit 0
fi

case "$WPF_TASKBAR_SESSION_ID" in
  *[!a-f0-9]*|'') echo "WpfTaskBar: invalid session ID" >&2; exit 0 ;;
esac
if [ "${#WPF_TASKBAR_SESSION_ID}" -ne 32 ]; then
  echo "WpfTaskBar: invalid session ID" >&2
  exit 0
fi

taskbar_payload="{\"status\":\"$status\"$taskbar_condition}"
taskbar_script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ -f "$taskbar_script_dir/taskbar-message.py" ] && command -v python3 >/dev/null 2>&1; then
  if ! taskbar_payload=$(python3 "$taskbar_script_dir/taskbar-message.py" "$1"); then
    exit 0
  fi
elif [ "$1" = activity ]; then
  echo "WpfTaskBar: 作業内容の通知には同じディレクトリの taskbar-message.py とPython 3が必要です。" >&2
  exit 0
elif [ "$1" = tool-failed ]; then
  # 旧版と同じく、shだけ配置した場合も状態通知を維持する。
  if ! status=$(python3 -c 'import json, sys; data = json.load(sys.stdin); print("interrupted" if data.get("is_interrupt") is True else "running")'); then
    echo "WpfTaskBar: ツール失敗フックの入力を読み取れませんでした（Python 3が必要です）。" >&2
    exit 0
  fi
  taskbar_payload="{\"status\":\"$status\"$taskbar_condition}"
fi

[ -n "$taskbar_payload" ] || exit 0
taskbar_method=POST
taskbar_endpoint=status
if [ "$1" = activity ]; then
  taskbar_method=PUT
  taskbar_endpoint=activity
fi

if ! curl --silent --show-error --fail --connect-timeout 2 --max-time "$taskbar_timeout" \
  --output /dev/null --request "$taskbar_method" \
  --header 'Content-Type: application/json' \
  --data "$taskbar_payload" \
  "${WPF_TASKBAR_URL%/}/tasks/sessions/$WPF_TASKBAR_SESSION_ID/$taskbar_endpoint"; then
  echo "WpfTaskBar: 状態を通知できませんでした。接続先と通知付きターミナルを確認してください。" >&2
fi
exit 0

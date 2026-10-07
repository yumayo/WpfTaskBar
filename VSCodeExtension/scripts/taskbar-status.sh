#!/bin/sh
# AI の開始 / 応答完了フックから呼ぶ。通知失敗で AI 本体を停止させない。
set -u

taskbar_condition=''
case "${1:-}" in
  running|waiting|interrupted|completed|none) status=$1 ;;
  resume|tool-failed) status=running; taskbar_condition=',"onlyIfActive":true' ;;
  *) echo "Usage: taskbar-status.sh running|waiting|interrupted|completed|none|resume|tool-failed [timeout-seconds: 1-5]" >&2; exit 2 ;;
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

# Claudeの失敗フックは通常のツールエラーとユーザー中断の両方で発火する。
# 中断以外は再開し、中断・完了後に遅れて届いたツール通知では状態を戻さない。
if [ "$1" = tool-failed ]; then
  if ! status=$(python3 -c 'import json, sys; data = json.load(sys.stdin); print("interrupted" if data.get("is_interrupt") is True else "running")'); then
    echo "WpfTaskBar: ツール失敗フックの入力を読み取れませんでした（Python 3が必要です）。" >&2
    exit 0
  fi
fi

if ! curl --silent --show-error --fail --connect-timeout 2 --max-time "$taskbar_timeout" \
  --output /dev/null --request POST \
  --header 'Content-Type: application/json' \
  --data "{\"status\":\"$status\"$taskbar_condition}" \
  "${WPF_TASKBAR_URL%/}/tasks/sessions/$WPF_TASKBAR_SESSION_ID/status"; then
  echo "WpfTaskBar: 状態を通知できませんでした。接続先と通知付きターミナルを確認してください。" >&2
fi
exit 0

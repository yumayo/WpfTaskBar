#!/bin/sh
# AI の開始 / 応答完了フックから呼ぶ。通知失敗で AI 本体を停止させない。
set -u

case "${1:-}" in
  running|completed|none) status=$1 ;;
  *) echo "Usage: taskbar-status.sh running|completed|none" >&2; exit 2 ;;
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

if ! curl --silent --show-error --fail --connect-timeout 2 --max-time 5 \
  --output /dev/null --request POST \
  --header 'Content-Type: application/json' \
  --data "{\"status\":\"$status\"}" \
  "${WPF_TASKBAR_URL%/}/tasks/sessions/$WPF_TASKBAR_SESSION_ID/status"; then
  echo "WpfTaskBar: 状態を通知できませんでした。接続先と通知付きターミナルを確認してください。" >&2
fi
exit 0

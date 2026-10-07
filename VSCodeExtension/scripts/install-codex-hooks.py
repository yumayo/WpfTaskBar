#!/usr/bin/env python3
"""共通通知スクリプトを配置し、既存のCodexフックを残して登録する。"""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shlex
import shutil
import tempfile


def install(target_dir: Path) -> None:
    source_dir = Path(__file__).resolve().parent
    target_dir = target_dir.expanduser().resolve()
    config_path = target_dir / "hooks.json"
    script_path = target_dir / "hooks" / "wpftaskbar" / "taskbar-status.sh"
    source_script = source_dir / "taskbar-status.sh"
    # 書き込み前に、設定とコピー元の両方を確認する。
    source_script.read_bytes()
    (source_dir / "taskbar-message.py").read_bytes()
    template = json.loads((source_dir.parent / "examples" / "codex-hooks.json").read_text(encoding="utf-8"))
    original = config_path.read_text(encoding="utf-8-sig") if config_path.exists() else None
    config = json.loads(original) if original is not None else {}
    if not isinstance(config, dict):
        raise ValueError("既存の hooks.json はJSONオブジェクトである必要があります。")
    hooks = config.setdefault("hooks", {})
    if not isinstance(hooks, dict):
        raise ValueError("既存の hooks はJSONオブジェクトである必要があります。")

    added = 0
    # 旧版の中断解除フックだけを置き換え、none と interrupted の競合を防ぐ。
    legacy_interrupt = "sh " + shlex.quote(str(script_path)) + " none 2"
    changed = False
    for event, additions in template["hooks"].items():
        groups = hooks.setdefault(event, [])
        if not isinstance(groups, list) or any(
            not isinstance(group, dict) or not isinstance(group.get("hooks"), list)
            or any(not isinstance(handler, dict) for handler in group["hooks"])
            for group in groups
        ):
            raise ValueError(f"既存の {event} の設定形式を確認してください。ファイルは変更していません。")
        if event == "Interrupt":
            retained = []
            for group in groups:
                handlers = [handler for handler in group["hooks"]
                            if not (group.get("matcher") in (None, "", "*")
                                    and handler.get("type") == "command"
                                    and handler.get("command") == legacy_interrupt)]
                if len(handlers) != len(group["hooks"]):
                    changed = True
                    if handlers:
                        retained.append({**group, "hooks": handlers})
                else:
                    retained.append(group)
            groups = hooks[event] = retained
        for addition in additions:
            handler = addition["hooks"][0]
            handler["command"] = handler["command"].replace("__TASKBAR_STATUS_SCRIPT__", shlex.quote(str(script_path)))
            # 再実行しても同じコマンドを二重登録しない。既存のカスタマイズも保持する。
            if any(existing.get("type") == "command" and existing.get("command") == handler["command"]
                   for group in groups if group.get("matcher") == addition.get("matcher")
                   for existing in group["hooks"]):
                continue
            groups.append(addition)
            added += 1

    script_path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source_script, script_path)
    shutil.copyfile(source_dir / "taskbar-message.py", script_path.with_name("taskbar-message.py"))
    if added or changed:
        if original is not None:
            timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
            backup = config_path.with_name(f"hooks.json.wpftaskbar-{timestamp}.bak")
            shutil.copy2(config_path, backup)
            print(f"既存設定のバックアップ: {backup}")
        # 途中まで書き込まれたJSONをCodexが読まないよう、同じディレクトリ内で置き換える。
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=target_dir, delete=False) as output:
            temporary_path = Path(output.name)
            try:
                json.dump(config, output, ensure_ascii=False, indent=2)
                output.write("\n")
            except BaseException:
                temporary_path.unlink(missing_ok=True)
                raise
        try:
            temporary_path.replace(config_path)
        finally:
            temporary_path.unlink(missing_ok=True)
    print(f"通知スクリプト: {script_path}")
    print(f"Codexフック: {config_path}（追加 {added} 件）")
    print("Codexを起動し直し、/hooks で追加したフックを確認して信頼してください。")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--codex-dir", type=Path,
                        default=Path(os.environ.get("CODEX_HOME") or Path.home() / ".codex"),
                        help="Codex設定ディレクトリ（既定: CODEX_HOME または ~/.codex）")
    args = parser.parse_args()
    if os.name == "nt":
        parser.error("Codexを実行するLinuxのAIコンテナ内で実行してください。")
    try:
        install(args.codex_dir)
    except (OSError, ValueError) as error:
        parser.exit(1, f"WpfTaskBar: {error}\n")


if __name__ == "__main__":
    main()

.PHONY: setup-claude

setup-claude:
	mkdir -p .claude.local
	[ ! -f .claude.local/.claude.json ] && echo '{}' > .claude.local/.claude.json || true
	mkdir -p .claude.local/.claude

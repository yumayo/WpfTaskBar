.PHONY: setup-claude run

setup-claude:
	mkdir -p .claude.local
	[ ! -f .claude.local/.claude.json ] && echo '{}' > .claude.local/.claude.json || true
	mkdir -p .claude.local/.claude

run:
	cd WpfTaskBar/Web && npm run build
	dotnet.exe run --project WpfTaskBar

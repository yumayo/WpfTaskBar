.PHONY: initialize artifact

initialize:
	mkdir -p .claude.local
	[ ! -f .claude.local/.claude.json ] && echo '{}' > .claude.local/.claude.json || true
	mkdir -p .claude.local/.claude

DEFAULT_GUAL := initialize

artifact:
	rm -rf dist
	rm -rf WpfTaskBar/log
	dotnet.exe build WpfTaskBar --configuration Release -o dist
	cp -r ChromeExtension dist
	(cd dist && zip -r ../WpfTaskBar_${APP_VERSION}.zip .)
	git.exe tag ${APP_VERSION} || true
	git.exe push origin master
	git.exe push origin --tags

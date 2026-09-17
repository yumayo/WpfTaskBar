.PHONY: artifact clock-in clock-out

GITHUB_RELEASE_URL := https://github.com/yumayo/WpfTaskBar/releases/new
RUNTIME_IDENTIFIER := win-x64
APP_VERSION ?= $(shell git fetch origin --tags >/dev/null 2>&1; latest=$$(git tag --list 'v[0-9]*' --sort=-v:refname | head -n 1); test -n "$$latest" && echo "$$latest" | awk -F. 'BEGIN { OFS="." } { $$NF = $$NF + 1; print }' || echo v0.1)

artifact:
	rm -rf dist
	rm -rf WpfTaskBar/log
	rm -rf WpfTaskBar/bin
	dotnet.exe build WpfTaskBar --configuration Release --runtime $(RUNTIME_IDENTIFIER) --no-self-contained -o dist
	(cd WebView && npm run build)
	mkdir -p dist/WebView
	cp -r WebView/dist/* dist/WebView
	(cd dist && zip -r ../WpfTaskBar_${APP_VERSION}.zip .)
	git tag ${APP_VERSION}
	git push origin master
	git push origin ${APP_VERSION}
	explorer.exe . || true
	cmd.exe /c start "" "$(GITHUB_RELEASE_URL)" || true

clock-in:
	WINDOWS_IP=$$(grep nameserver /etc/resolv.conf | awk '{print $$2}'); \
	DATETIME=$$(date +"%Y-%m-%dT%H:%M:%S"); \
	curl -X POST "http://$$WINDOWS_IP:5000/clock-in" -H "Content-Type: application/json" -d "{\"date\": \"$$DATETIME\"}"

clock-out:
	WINDOWS_IP=$$(grep nameserver /etc/resolv.conf | awk '{print $$2}'); \
	DATETIME=$$(date +"%Y-%m-%dT%H:%M:%S"); \
	curl -X POST "http://$$WINDOWS_IP:5000/clock-out" -H "Content-Type: application/json" -d "{\"date\": \"$$DATETIME\"}"

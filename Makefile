.PHONY: artifact clock-in clock-out

artifact:
	rm -rf dist
	rm -rf WpfTaskBar/log
	rm -rf WpfTaskBar/bin
	dotnet.exe build WpfTaskBar --configuration Release -o dist
	(cd WebView && npm run build)
	mkdir -p dist/WebView
	cp -r WebView/dist/* dist/WebView
	(cd ChromeExtension && npm run build)
	mkdir -p dist/ChromeExtension
	cp -r ChromeExtension/dist/* dist/ChromeExtension
	(cd dist && zip -r ../WpfTaskBar_${APP_VERSION}.zip .)
	git.exe tag ${APP_VERSION} || true
	git.exe push origin master
	git.exe push origin --tags
	explorer.exe . || true
	echo https://github.com/yumayo/WpfTaskBar/releases/new

clock-in:
	WINDOWS_IP=$$(grep nameserver /etc/resolv.conf | awk '{print $$2}'); \
	DATETIME=$$(date +"%Y-%m-%dT%H:%M:%S"); \
	curl -X POST "http://$$WINDOWS_IP:5000/clock-in" -H "Content-Type: application/json" -d "{\"date\": \"$$DATETIME\"}"

clock-out:
	WINDOWS_IP=$$(grep nameserver /etc/resolv.conf | awk '{print $$2}'); \
	DATETIME=$$(date +"%Y-%m-%dT%H:%M:%S"); \
	curl -X POST "http://$$WINDOWS_IP:5000/clock-out" -H "Content-Type: application/json" -d "{\"date\": \"$$DATETIME\"}"

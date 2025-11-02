.PHONY: artifact clock-in clock-out

artifact:
	rm -rf dist
	rm -rf WpfTaskBar/log
	dotnet.exe build WpfTaskBar --configuration Release -o dist
	cp -r ChromeExtension dist
	(cd dist && zip -r ../WpfTaskBar_${APP_VERSION}.zip .)
	git.exe tag ${APP_VERSION} || true
	git.exe push origin master
	git.exe push origin --tags

clock-in:
	WINDOWS_IP=$$(grep nameserver /etc/resolv.conf | awk '{print $$2}'); \
	DATETIME=$$(date +"%Y-%m-%dT%H:%M:%S"); \
	curl -X POST "http://$$WINDOWS_IP:5000/clock-in" -H "Content-Type: application/json" -d "{\"date\": \"$$DATETIME\"}"

clock-out:
	WINDOWS_IP=$$(grep nameserver /etc/resolv.conf | awk '{print $$2}'); \
	DATETIME=$$(date +"%Y-%m-%dT%H:%M:%S"); \
	curl -X POST "http://$$WINDOWS_IP:5000/clock-out" -H "Content-Type: application/json" -d "{\"date\": \"$$DATETIME\"}"

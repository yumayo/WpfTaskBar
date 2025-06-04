Wpfで作ったお手製のタスクバーです。
勤怠情報も追加で表示しています。

# 出勤

```sh
curl -X POST "http://localhost:5000/clock-in" -H "Content-Type: application/json" -d "{\"date\": \"2025-06-04T09:55:00\"}"
```

# 退勤

```sh
curl -X POST "http://localhost:5000/clock-out" -H "Content-Type: application/json" -d "{\"date\": \"2025-06-04T19:34:00\"}"
```

# 勤怠のクリア

```sh
curl -X POST http://localhost:5000/clear
```

Wpfで作ったお手製のタスクバーです。
勤怠情報も追加で表示しています。

# 出勤

```sh
curl -X POST http://localhost:5000/api/TimeRecord/clock-in
```

# 退勤

```sh
curl -X POST http://localhost:5000/api/TimeRecord/clock-out
```

# 勤怠のクリア

```sh
curl -X POST http://localhost:5000/api/TimeRecord/clear
```

Wpfで作ったお手製のタスクバーです。
勤怠情報も追加で表示しています。

# API

## 出勤

```sh
curl -X POST "http://localhost:5000/clock-in" -H "Content-Type: application/json" -d "{\"date\": \"2025-06-04T09:55:00\"}"
```

## 退勤

```sh
curl -X POST "http://localhost:5000/clock-out" -H "Content-Type: application/json" -d "{\"date\": \"2025-06-04T19:34:00\"}"
```

## 勤怠のクリア

```sh
curl -X POST http://localhost:5000/clear
```

呼び出さなくても午前0時にリセットされます。

## 通知

```sh
curl -X POST "http://localhost:5000/notification" -H "Content-Type: application/json" -d "{\"title\": \"通知タイトル\", \"message\": \"通知内容\"}"
```

# 開発

```sh
dotnet build
```

```sh
dotnet run --project WpfTaskBar
```

iオプションは対話シェルで、.bashrcを読み込んでくれます。
```sh
docker compose up -d --build && docker compose exec ai bash -i -c "claude -c"
```

# ビルド

```sh
rm -rf dist
dotnet build WpfTaskBar --configuration Release -o dist
(cd dist && zip -r ../WpfTaskBar_v0.1.zip .)
```

# WpfTaskBar WebSocket Test Extension

WpfTaskBarアプリケーションのWebSocket通信機能をテストするためのChrome拡張機能です。

## 機能

- WpfTaskBarへのWebSocket接続 (ws://127.0.0.1:5000/ws)
- タブ情報の自動登録
- テスト通知の送信
- タブフォーカス機能のテスト
- 接続状況の表示

## インストール方法

1. Chromeで `chrome://extensions/` を開く
2. 右上の「デベロッパーモード」を有効にする
3. 「パッケージ化されていない拡張機能を読み込む」をクリック
4. このフォルダ (`/var/app/ChromeExtension`) を選択

## 使用方法

1. WpfTaskBarアプリケーションを起動する
2. Chrome拡張機能のアイコンをクリックしてポップアップを開く
3. 接続状況を確認する（緑色 = 接続済み、赤色 = 未接続）
4. 「テスト通知を送信」ボタンでWpfTaskBarに通知を送信
5. WpfTaskBar側で通知をクリックするとChromeのタブが自動的にフォーカスされる

## ファイル構成

- `manifest.json` - 拡張機能の設定ファイル
- `background.js` - バックグラウンドスクリプト（WebSocket通信の中核）
- `popup.html` - ポップアップのHTML
- `popup.js` - ポップアップのJavaScript
- `content.js` - コンテンツスクリプト（将来的な拡張用）

## 通信プロトコル

### 送信メッセージ

1. **タブ情報登録**
```json
{
  "action": "registerTab",
  "data": {
    "tabId": 123,
    "windowId": 456,
    "url": "https://example.com",
    "title": "Page Title"
  }
}
```

2. **通知送信**
```json
{
  "action": "sendNotification",
  "data": {
    "title": "通知タイトル",
    "message": "通知メッセージ",
    "tabId": 123,
    "windowId": 456,
    "url": "https://example.com",
    "tabTitle": "Page Title",
    "timestamp": "2023-12-01T10:30:00Z"
  }
}
```

3. **Ping応答**
```json
{
  "action": "pong",
  "data": {}
}
```

### 受信メッセージ

1. **タブフォーカス要求**
```json
{
  "action": "focusTab",
  "data": {
    "tabId": 123,
    "windowId": 456
  }
}
```

2. **Ping**
```json
{
  "action": "ping",
  "data": {}
}
```

## トラブルシューティング

### 接続できない場合
1. WpfTaskBarアプリケーションが起動していることを確認
2. WebSocketサーバーがポート5000で起動していることを確認
3. Windows Defenderやファイアウォールがポート5000をブロックしていないか確認

### 通知が表示されない場合
1. WebSocket接続が確立されていることを確認
2. WpfTaskBarのログを確認
3. Chrome拡張機能のコンソール（デベロッパーツール）でエラーメッセージを確認

## 開発者向け情報

### ログ確認方法
- Chrome拡張機能のバックグラウンドスクリプトのログ: `chrome://extensions/` → 拡張機能の「詳細」→「バックグラウンドページ」→「検証」
- WpfTaskBarのログ: `/var/app/log/WpfTaskBar.log`

### 拡張機能の更新
コードを変更した後、`chrome://extensions/` で拡張機能の「更新」ボタンをクリックしてください。
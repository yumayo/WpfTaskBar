# 概要

Windowsの垂直タスクバーを実装します。
現在起動中のプロセスの内、ウィンドウを持っているものをタスクバーに表示します。

Windows11の標準タスクバーから改良した点
- プロセスごとにグループ化されていないところ
- 垂直タスクバーに対応しているところ
- マウスの中クリックでプロセスを閉じること
- ウィジェットを配置可能 (勤怠管理)

タスクの起動などは、Windowsのハンドラーを使用して捉えようとしましたが、上手く行かなかったため、100msごとにポーリングしています。

逆にWindows11のタスクバーに実装されていて、未実装な機能は下記のとおりです。
- 進行度の表示
- UWPのアイコン
- Chromeのプロフィールアイコンの表示
- システムトレイ

# ルール

デバッグ用の出力はLoggerクラスを使用してください。
Loggerクラスはモノステートパターンとなっているため、Logger.Info形式で呼び出すことができます。

# フォルダ構成

## ClassLibrary

WindowsのNativeメソッド系を置く場所です。
AppxPackageでUWPのアイコンを表示しようとしましたが苦戦中です。

## ConsoleApp

AppxPackage等のデバッグに使用しています。

## WpfTaskBar

プロジェクトの本体です。
基本的にはここを弄ります。

### Views/ - UI層
- Views/MainWindow.xaml / MainWindow.xaml.cs - メインウィンドウ
- Views/Controls/ - カスタムコントロール
  - DateTimeItem.cs - 日時表示コントロール
  - IconListBoxItem.cs - アイコンリストアイテム
  - TaskBarItem.cs - タスクバーアイテム
- Views/Converters/ - データ変換
  - BoolToThicknessConverter.cs
  - IndexToMarginConverter.cs
  - StringToVisibilityConverter.cs

### Services/ - ビジネスロジック層
- ApplicationOrderService.cs - アプリケーション順序管理
- WindowManager.cs - ウィンドウ管理
- ConsoleManager.cs - コンソール管理
- TabManager.cs - タブ管理
- WebSocketHandler.cs - WebSocket通信処理

### Api/ - REST API層
- Api/Controllers/ - APIコントローラー
  - TimeRecordController.cs - 勤怠記録API
  - NotificationController.cs - 通知API
- Api/Models/ - APIモデル
  - TimeRecordModel.cs - 勤怠記録モデル
  - NotificationModel.cs - 通知モデル

### Infrastructure/ - インフラストラクチャ層
- Logger.cs - ログ機能
- Startup.cs - DIコンテナ設定

### アプリケーション設定
- App.xaml / App.xaml.cs - アプリケーション初期化

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

# フォルダ構成

## ClassLibrary

WindowsのNativeメソッド系を置く場所です。
AppxPackageでUWPのアイコンを表示しようとしましたが苦戦中です。

## ConsoleApp

AppxPackage等のデバッグに使用しています。

## WpfTaskBar

プロジェクトの本体です。
基本的にはここを弄ります。

### UI系
- MainWindow.xaml
- MainWindow.xaml.cs
- App.xaml
- App.xaml.cs
- DateTimeItem.cs
- IconListBoxItem.cs
- TaskBarItem.cs
- Converters/*.cs

### REST API系
- Rest/Controllers/TimeRecordController.cs
- Rest/Models/TimeRecordModel.cs
- Rest/Controllers/NotifactionController.cs
- Rest/Models/NotificationModel.cs
- Startup.cs

### タスクバーを構成するクラス群

- ApplicationOrderService.cs
- ConsoleManager.cs
- WindowManager.cs

### Utility
- Logger.cs

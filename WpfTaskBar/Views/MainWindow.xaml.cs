using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Collections.Specialized;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Window = System.Windows.Window;

namespace WpfTaskBar;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	private IHost? _host;
	private ChromeTabManager? _tabManager;
	private WebView2Handler? _webView2;

	public MainWindow()
	{
		InitializeComponent();

		Logger.Info("MainWindow initialized with WebView2");
	}

	private void SendNotificationUpdate()
	{
		_webView2!.SendMessageToWebView(new
		{
			type = "notification_update",
			notifications = NotificationModel.Notifications.Select(n => new
			{
				id = n.Id.ToString(),
				title = n.Title,
				message = n.Message,
				timestamp = n.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
				windowHandle = 0
			}).ToArray()
		});
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		// まずサービスを初期化
		Logger.Info("MainWindow loaded, initializing services...");
		InitializeServices();
		
		// タスクバーの領域を確保する。
		SetTaskBarRect();
	}

	private void SetTaskBarRect()
	{
		int height = (int)SystemParameters.PrimaryScreenHeight;
		int width = (int)SystemParameters.PrimaryScreenWidth;

		int myTaskBarWidth = 400;

		IntPtr handle = new WindowInteropHelper(this).Handle;

		// 登録領域から外されないように属性を変更する
		ulong style = NativeMethods.GetWindowLongA(handle, NativeMethods.GWL_EXSTYLE);
		style ^= NativeMethods.WS_EX_APPWINDOW;
		style |= NativeMethods.WS_EX_TOOLWINDOW;
		style |= NativeMethods.WS_EX_NOACTIVATE;
		NativeMethods.SetWindowLongA(handle, NativeMethods.GWL_EXSTYLE, style);

		// タスクバーは表示しないほうが分かりやすそうなので高さ0にしておきます。
		var taskBarHeight = NativeMethodUtility.GetTaskbarHeight();
		taskBarHeight = 0;

		NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, myTaskBarWidth, (int)(height * NativeMethodUtility.GetPixelsPerDpi() - taskBarHeight), NativeMethods.SWP_SHOWWINDOW);

		// AppBarの登録
		NativeMethods.APPBARDATA barData = new NativeMethods.APPBARDATA();
		barData.cbSize = Marshal.SizeOf(barData);
		barData.hWnd = handle;
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref barData);

		// 左端に登録する
		barData.uEdge = NativeMethods.ABE_LEFT;
		barData.rc.Top = 0;
		barData.rc.Left = 0;
		barData.rc.Right = myTaskBarWidth;
		barData.rc.Bottom = (int)SystemParameters.PrimaryScreenHeight;

		NativeMethods.GetWindowRect(handle, out barData.rc);
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref barData);
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref barData);
	}

	public void InitializeServices()
	{
		Logger.Info("Starting MainWindow service initialization");

		Logger.Info("Starting REST API server...");
		
		var builder = Host.CreateDefaultBuilder();
		builder.ConfigureWebHostDefaults(webBuilder =>
		{
			webBuilder.UseUrls("http://0.0.0.0:5000");
			webBuilder.UseStartup<Startup>();
			// HTTP/2を有効化（HTTPでもh2cとして動作可能）
			webBuilder.ConfigureKestrel(options =>
			{
				options.ConfigureEndpointDefaults(listenOptions =>
				{
					listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
				});
			});
		});
		
		_host = builder.Build();
		_host.StartAsync();
		Logger.Info("REST API server started");

		// DIコンテナからサービスを取得
		_tabManager = _host.Services.GetRequiredService<ChromeTabManager>();
		_webView2 = _host.Services.GetRequiredService<WebView2Handler>();

		_webView2.Initialize(App.Current.Dispatcher, webView2);

		Logger.Info("Services obtained from DI container");

		// 通知リストのイベントハンドラーを設定
		NotificationModel.Notifications.CollectionChanged += OnNotificationsChanged;
		
		Logger.Info("MainWindow services initialized successfully from DI container");
	}

	public void ShowNotification(NotificationData notification)
	{
		try
		{
			var notificationItem = new NotificationItem
			{
				Id = Guid.NewGuid(),
				Title = notification.Title,
				Message = notification.Message,
				Timestamp = DateTime.Now
			};

			// 通知をUIに追加
			Application.Current.Dispatcher.Invoke(() =>
			{
				NotificationModel.Notifications.Insert(0, notificationItem);

				// 500件を超える場合は古いものを削除
				while (NotificationModel.Notifications.Count > 500)
				{
					NotificationModel.Notifications.RemoveAt(NotificationModel.Notifications.Count - 1);
				}

				// WebView2に通知更新を送信
				SendNotificationUpdate();
			});

			// 通知とタブIDの関連付けを保存
			_tabManager?.AssociateNotificationWithTab(notificationItem.Id.ToString(), notification);

			Logger.Info($"Notification added: {notification.Title}");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "通知表示時にエラーが発生しました。");
		}
	}

	private async void MainWindow_OnClosed(object? sender, EventArgs e)
	{
		if (_host != null)
		{
			await _host.StopAsync();
			_host.Dispose();
		}
		
		Logger.Close();
	}

	private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		Dispatcher.Invoke(() =>
		{
			try
			{
				SendNotificationUpdate();
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "通知表示の更新中にエラーが発生しました。");
			}
		});
	}
}
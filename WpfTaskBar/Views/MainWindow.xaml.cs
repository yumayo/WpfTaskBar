using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Window = System.Windows.Window;

namespace WpfTaskBar;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	private WebSocketHandler? _webSocketHandler;
	private ChromeTabManager? _tabManager;
	private WebView2? _webView2;

	public MainWindow()
	{
		InitializeComponent();

		Logger.Info("MainWindow initialized with WebView2");
	}

	private void SendNotificationUpdate()
	{
		try
		{
			var notificationData = new
			{
				type = "notification_update",
				notifications = NotificationModel.Notifications.Select(n => new
				{
					id = n.Id.ToString(),
					title = n.Title,
					message = n.Message,
					timestamp = n.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
					windowHandle = IntPtr.Zero // 必要に応じて設定
				}).ToArray()
			};

			_webView2!.SendMessageToWebView(notificationData);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "通知更新送信時にエラーが発生しました。");
		}
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		// まずサービスを初期化
		Logger.Info("MainWindow loaded, initializing services...");
		InitializeServices();

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
		if (App.ServiceProvider == null)
		{
			Logger.Info("ServiceProvider not yet available, services will be initialized later");
			return;
		}

		try
		{
			Logger.Info("Starting MainWindow service initialization");

			// DIコンテナからサービスを取得
			_webSocketHandler = App.ServiceProvider.GetRequiredService<WebSocketHandler>();
			_tabManager = App.ServiceProvider.GetRequiredService<ChromeTabManager>();

			Logger.Info("Services obtained from DI container");

			_webView2 = new WebView2(webView);

			Application.Current.Dispatcher.Invoke(async () =>
			{
				await _webView2.Initialize();
			});
			
			// 通知リストのイベントハンドラーを設定
			NotificationModel.Notifications.CollectionChanged += OnNotificationsChanged;
			
			Logger.Info("MainWindow services initialized successfully from DI container");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to initialize services from DI container");
		}
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

	private void MainWindow_OnClosed(object? sender, EventArgs e)
	{
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
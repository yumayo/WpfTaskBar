using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using Window = System.Windows.Window;

namespace WpfTaskBar;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	private const int TaskBarWidth = 400;

	private IHost? _host;
	private WebView2Handler? _webView2;
	private Forms.NotifyIcon? _trayIcon;
	private bool _isAppBarRegistered;
	private bool _isMinimizedToTray;

	public MainWindow()
	{
		InitializeComponent();
		InitializeTrayIcon();

		Logger.Info("MainWindow initialized with WebView2");
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

		IntPtr handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
		{
			return;
		}

		// 登録領域から外されないように属性を変更する
		ulong style = NativeMethods.GetWindowLongA(handle, NativeMethods.GWL_EXSTYLE);
		style &= ~NativeMethods.WS_EX_APPWINDOW;
		style |= NativeMethods.WS_EX_TOOLWINDOW;
		style |= NativeMethods.WS_EX_NOACTIVATE;
		NativeMethods.SetWindowLongA(handle, NativeMethods.GWL_EXSTYLE, style);

		// タスクバーは表示しないほうが分かりやすそうなので高さ0にしておきます。
		var taskBarHeight = NativeMethodUtility.GetTaskbarHeight();
		taskBarHeight = 0;

		NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, TaskBarWidth, (int)(height * NativeMethodUtility.GetPixelsPerDpi() - taskBarHeight), NativeMethods.SWP_SHOWWINDOW);

		// AppBarの登録
		NativeMethods.APPBARDATA barData = new NativeMethods.APPBARDATA();
		barData.cbSize = Marshal.SizeOf(barData);
		barData.hWnd = handle;
		if (!_isAppBarRegistered)
		{
			var result = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref barData);
			if (result == 0)
			{
				Logger.Warning("AppBarの登録に失敗しました。");
				return;
			}

			_isAppBarRegistered = true;
		}

		// 左端に登録する
		barData.uEdge = NativeMethods.ABE_LEFT;
		barData.rc.Top = 0;
		barData.rc.Left = 0;
		barData.rc.Right = TaskBarWidth;
		barData.rc.Bottom = (int)SystemParameters.PrimaryScreenHeight;

		NativeMethods.GetWindowRect(handle, out barData.rc);
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref barData);
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref barData);
	}

	private void ReleaseTaskBarRect()
	{
		if (!_isAppBarRegistered)
		{
			return;
		}

		IntPtr handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
		{
			return;
		}

		NativeMethods.APPBARDATA barData = new NativeMethods.APPBARDATA();
		barData.cbSize = Marshal.SizeOf(barData);
		barData.hWnd = handle;
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref barData);
		_isAppBarRegistered = false;
		Logger.Info("AppBarの登録を解除しました。");
	}

	private void InitializeTrayIcon()
	{
		var menu = new Forms.ContextMenuStrip();
		menu.Items.Add("最大化", null, (_, _) => RestoreFromTray());
		menu.Items.Add("終了", null, (_, _) => WpfApplication.Current.Shutdown());

		_trayIcon = new Forms.NotifyIcon
		{
			Icon = System.Drawing.SystemIcons.Application,
			Text = "WpfTaskBar",
			ContextMenuStrip = menu,
			Visible = false
		};
		_trayIcon.MouseClick += (_, e) =>
		{
			if (e.Button == Forms.MouseButtons.Left)
			{
				RestoreFromTray();
			}
		};
		_trayIcon.DoubleClick += (_, _) => RestoreFromTray();
	}

	public void MinimizeToTray()
	{
		if (_isMinimizedToTray)
		{
			return;
		}

		_isMinimizedToTray = true;
		ReleaseTaskBarRect();
		if (_trayIcon != null)
		{
			_trayIcon.Visible = true;
		}

		Hide();
		Logger.Info("タスクバーを通知領域へ最小化しました。");
	}

	private void RestoreFromTray()
	{
		if (!Dispatcher.CheckAccess())
		{
			Dispatcher.BeginInvoke((Action)RestoreFromTray);
			return;
		}

		_isMinimizedToTray = false;
		Show();
		WindowState = WindowState.Normal;
		SetTaskBarRect();

		if (_trayIcon != null)
		{
			_trayIcon.Visible = false;
		}

		Logger.Info("タスクバーを通知領域から復元しました。");
	}

	public void InitializeServices()
	{
		Logger.Info("Starting MainWindow service initialization");

		Logger.Info("Starting REST API server...");

		// TimeRecordModelのデータを読み込む
		TimeRecordModel.Load();
		Logger.Info("TimeRecordModel data loaded");

		var builder = Host.CreateDefaultBuilder();
		builder.ConfigureWebHostDefaults(webBuilder =>
		{
			webBuilder.UseUrls("http://0.0.0.0:5000");
			webBuilder.UseStartup<Startup>();
		});
		
		_host = builder.Build();
		_host.StartAsync();
		Logger.Info("REST API server started");

		// DIコンテナからサービスを取得
		_webView2 = _host.Services.GetRequiredService<WebView2Handler>();

		_ = _webView2!.InitializeAsync(App.Current.Dispatcher, webView2);

		Logger.Info("Services obtained from DI container");
		
		Logger.Info("MainWindow services initialized successfully from DI container");
	}

	private void MainWindow_OnStateChanged(object? sender, EventArgs e)
	{
		if (WindowState == WindowState.Minimized)
		{
			MinimizeToTray();
		}
	}

	private async void MainWindow_OnClosed(object? sender, EventArgs e)
	{
		ReleaseTaskBarRect();

		if (_trayIcon != null)
		{
			_trayIcon.Visible = false;
			_trayIcon.Dispose();
			_trayIcon = null;
		}

		if (_host != null)
		{
			await _host.StopAsync();
			_host.Dispose();
		}
		
		Logger.Close();
	}
}

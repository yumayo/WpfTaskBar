using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
	private WebView2Handler? _webView2;

	public MainWindow()
	{
		InitializeComponent();

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

	private async void MainWindow_OnClosed(object? sender, EventArgs e)
	{
		if (_host != null)
		{
			await _host.StopAsync();
			_host.Dispose();
		}
		
		Logger.Close();
	}
}

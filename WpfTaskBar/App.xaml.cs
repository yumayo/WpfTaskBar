using System.Windows;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace WpfTaskBar;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
	private IHost? _host;
	public static IServiceProvider? ServiceProvider { get; private set; }

	protected override async void OnStartup(StartupEventArgs e)
	{
		ConsoleManager.Setup();

		Logger.Setup();
		Logger.Info("Application startup beginning");

		// グローバル例外ハンドラーを設定
		SetupGlobalExceptionHandlers();
		Logger.Info("Global exception handlers set up");

		base.OnStartup(e);
		Logger.Info("WPF base.OnStartup completed");

		// REST APIサーバーを起動
		Logger.Info("Starting REST API server...");
		_host = CreateHostBuilder(e.Args).Build();
		await _host.StartAsync();
		Logger.Info("REST API server started");

		// 静的ServiceProviderを設定
		ServiceProvider = _host.Services;
		Logger.Info("Static ServiceProvider set");

		Logger.Info("Application startup completed - MainWindow services will be initialized when MainWindow is loaded");
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		// REST APIサーバーを停止
		if (_host != null)
		{
			await _host.StopAsync();
			_host.Dispose();
		}

		// 静的ServiceProviderをクリア
		ServiceProvider = null;

		base.OnExit(e);
	}

	private static IHostBuilder CreateHostBuilder(string[] args)
	{
		var builder = Host.CreateDefaultBuilder(args);
		builder.ConfigureWebHostDefaults(webBuilder =>
		{
			webBuilder.UseUrls("http://0.0.0.0:5000");
			webBuilder.UseStartup<Startup>();
		});
		return builder;
	}

	private void SetupGlobalExceptionHandlers()
	{
		// UIスレッドの未処理例外をキャッチ
		DispatcherUnhandledException += (sender, e) =>
		{
			HandleException(e.Exception, sender, "UIスレッド");
			e.Handled = true; // アプリケーションの終了を防ぐ
		};

		// バックグラウンドスレッドの未処理例外をキャッチ
		TaskScheduler.UnobservedTaskException += (sender, e) =>
		{
			HandleException(e.Exception, sender, "バックグラウンドタスク");
			e.SetObserved(); // 例外を観測済みとしてマーク
		};

		// その他の未処理例外をキャッチ
		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			var exception = e.ExceptionObject as Exception;
			HandleException(exception, sender, "AppDomain");
		};
	}

	private void HandleException(Exception? exception, object? sender, string source)
	{
		if (exception == null)
		{
			return;
		}

		try
		{
			Logger.Error(exception, source);
		}
		catch
		{
			// 例外処理中にエラーが発生した場合は最低限のログ出力
			Logger.Debug($"[{source}] 例外処理中にエラーが発生しました");
		}
	}
}

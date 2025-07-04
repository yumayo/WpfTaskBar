﻿using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace WpfTaskBar;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	private IHost? _host;

	protected override async void OnStartup(StartupEventArgs e)
	{
		ConsoleManager.Setup();

		// グローバル例外ハンドラーを設定
		SetupGlobalExceptionHandlers();

		base.OnStartup(e);

		// REST APIサーバーを起動
		_host = CreateHostBuilder(e.Args).Build();
		await _host.StartAsync();
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		// REST APIサーバーを停止
		if (_host != null)
		{
			await _host.StopAsync();
			_host.Dispose();
		}

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
			HandleException(e.Exception, "UIスレッド");
			e.Handled = true; // アプリケーションの終了を防ぐ
		};

		// バックグラウンドスレッドの未処理例外をキャッチ
		TaskScheduler.UnobservedTaskException += (sender, e) =>
		{
			HandleException(e.Exception, "バックグラウンドタスク");
			e.SetObserved(); // 例外を観測済みとしてマーク
		};

		// その他の未処理例外をキャッチ
		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			var exception = e.ExceptionObject as Exception;
			HandleException(exception, "AppDomain");
		};
	}

	private void HandleException(Exception? exception, string source)
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

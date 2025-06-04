using System;
using System.Windows;
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

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://0.0.0.0:5000");
                webBuilder.UseStartup<Startup>();
            });
}
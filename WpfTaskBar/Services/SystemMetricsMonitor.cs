using Microsoft.Extensions.Hosting;

namespace WpfTaskBar;

/// <summary>画面のリクエスト頻度とは独立して、1 秒間隔で計測する。</summary>
public sealed class SystemMetricsMonitor(WebView2Handler webView) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var reader = new WindowsSystemMetricsReader();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            // 最初の await より前にはネイティブ API を呼ばない。
            // ConfigureAwait(false) により、計測は WPF の UI スレッドから切り離す。
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                SystemMetricsSample sample;
                try
                {
                    sample = reader.Read();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "システム使用率の取得に失敗しました。");
                    sample = new SystemMetricsSample(DateTimeOffset.UtcNow, null, null, null, null, null, null);
                }

                webView.SendMessageToWebView(new
                {
                    type = "system_metrics_update",
                    timestamp = sample.Timestamp,
                    cpuPercent = sample.CpuPercent,
                    memoryPercent = sample.MemoryPercent,
                    diskPercent = sample.DiskPercent,
                    swapPercent = sample.SwapPercent,
                    memoryUsedBytes = sample.MemoryUsedBytes,
                    memoryTotalBytes = sample.MemoryTotalBytes
                });
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // ホスト終了時に PDH クエリーとタイマーを解放する。
        }
    }
}

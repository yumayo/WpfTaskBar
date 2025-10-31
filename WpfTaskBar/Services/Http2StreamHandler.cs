using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.IO;

namespace WpfTaskBar
{
    public class Http2StreamHandler : IDisposable
    {
        private readonly ConcurrentDictionary<string, StreamWriter> _connections = new();
        private readonly ConcurrentDictionary<int, IntPtr> _windowIdToHwndMap = new();
        private readonly ChromeTabManager _tabManager;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public Http2StreamHandler(ChromeTabManager tabManager)
        {
            _tabManager = tabManager;

            Logger.Info("Http2StreamHandler initialized");
        }

        // サーバーからクライアントへのストリーム接続を処理
        public async Task HandleStreamAsync(HttpContext context)
        {
            var connectionId = context.Connection.Id;
            var remotePort = context.Connection.RemotePort;

            Logger.Info($"HandleStreamAsync: ConnectionId={connectionId}, RemotePort={remotePort}");

            context.Response.Headers["Content-Type"] = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["Connection"] = "keep-alive";

            try
            {
                var writer = new StreamWriter(context.Response.Body, Encoding.UTF8, leaveOpen: true)
                {
                    AutoFlush = false  // 同期フラッシュを無効化
                };

                _connections[connectionId] = writer;

                // 接続確認メッセージを送信
                await SendMessage(writer, new Payload
                {
                    Action = "connected",
                    Data = new { connectionId }
                });

                // 接続を維持（クライアントが切断するまで）
                var tcs = new TaskCompletionSource<bool>();
                context.RequestAborted.Register(() => tcs.TrySetResult(true));
                await tcs.Task;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"HTTP/2 stream error for {connectionId}");
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
                Logger.Info($"HTTP/2 stream connection closed: {connectionId}");
            }
        }

        // クライアントからサーバーへのメッセージを処理
        public async Task HandleMessageAsync(HttpContext context)
        {
            try
            {
                var remotePort = context.Connection.RemotePort;
                var connectionId = context.Connection.Id;

                Logger.Info($"HandleMessageAsync: ConnectionId='{connectionId}', RemotePort={remotePort}");

                // リクエストボディの最大サイズチェック（1MB）
                const int maxSize = 1 * 1024 * 1024;
                if (context.Request.ContentLength > maxSize)
                {
                    Logger.Warning($"Message size exceeded the limit. Received: {context.Request.ContentLength} bytes, Limit: {maxSize} bytes");
                    context.Response.StatusCode = 413; // Payload Too Large
                    await context.Response.WriteAsync("Message too large");
                    return;
                }

                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                var message = await reader.ReadToEndAsync();

                await ProcessMessage(context, message);

                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("OK");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling message");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal Server Error");
            }
        }

        private async Task ProcessMessage(HttpContext context, string message)
        {
            string connectionId = context.Connection.Id;
            try
            {
                Logger.Info($"Raw message from {connectionId}: {message}");

                var payload = JsonSerializer.Deserialize<Payload>(message, JsonOptions);
                if (payload == null)
                {
                    Logger.Info($"Invalid message format from {connectionId}: {message}");
                    return;
                }

                Logger.Info($"Parsed message from {connectionId}: Action='{payload.Action}', Data={JsonSerializer.Serialize(payload.Data, JsonOptions)}");

                switch (payload.Action)
                {
                    case "registerTab":
                        HandleRegisterTab(payload.Data);
                        break;
                    case "unregisterTab":
                        HandleUnregisterTab(payload.Data);
                        break;
                    case "sendNotification":
                        HandleSendNotification(payload.Data);
                        break;
                    case "updateTabs":
                        HandleUpdateTabs(payload.Data);
                        break;
                    case "bindWindowHandle":
                        HandleBindWindowHandle(context, payload.Data);
                        break;
                    case "ping":
                        await HandlePing();
                        break;
                    default:
                        Logger.Info($"Unknown action: {payload.Action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error processing message from {connectionId}");
            }
        }

        private Task HandlePing()
        {
            // ブラウザで双方向ストリームに対応していなさそうなので、書き込みはブラウザから/messageへ、サーバーからブラウザへは/streamとしています。
            // このときコネクションIDは/messageと/streamで分かれているため、両方に送る必要があります。
            // もともと、ブラウザ側がアクティブなウィンドウでない場合、Keep-Aliveが行われないため、/streamが意図せず閉じてしまうため、/messageを受け取ったらサーバー側から/streamに返して通信を維持しているというトリックです。
            return BroadcastMessage(new Payload { Action = "pong", Data = new { } });
        }

        private void HandleRegisterTab(object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var tabInfo = JsonSerializer.Deserialize<TabInfo>(json, JsonOptions);
                if (tabInfo != null)
                {
                    _tabManager.RegisterTab(tabInfo);
                    Logger.Info($"Tab registered: {tabInfo.TabId} - {tabInfo.Title}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error registering tab");
            }
        }

        private void HandleUnregisterTab(object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var tabInfo = JsonSerializer.Deserialize<TabInfo>(json, JsonOptions);
                if (tabInfo != null)
                {
                    _tabManager.UnregisterTab(tabInfo.TabId);
                    Logger.Info($"Tab unregistered: {tabInfo.TabId} - {tabInfo.Title}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error unregistering tab");
            }
        }

        private void HandleSendNotification(object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var notification = JsonSerializer.Deserialize<NotificationData>(json, JsonOptions);
                if (notification != null)
                {
                    // 通知を送信してきた接続のChromeウィンドウハンドルを設定
                    if (_windowIdToHwndMap.TryGetValue(notification.WindowId, out var hwnd))
                    {
                        notification.WindowHandle = hwnd;
                        Logger.Info($"Notification received: {notification.Title}, WindowHandle: {notification.WindowHandle}");
                    }
                    else
                    {
                        Logger.Info($"Notification received: {notification.Title}, WindowHandle not found");
                    }

                    ShowNotification(notification);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling notification");
            }
        }

        private void HandleUpdateTabs(object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var tabsData = JsonSerializer.Deserialize<TabsUpdateData>(json, JsonOptions);
                if (tabsData?.Tabs != null)
                {
                    foreach (var tab in tabsData.Tabs)
                    {
                        _tabManager.RegisterTab(tab);
                    }
                    Logger.Info($"Tabs updated: {tabsData.Tabs.Count} tabs registered");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling tabs update");
            }
        }

        private void HandleBindWindowHandle(HttpContext context, object data)
        {
            string connectionId = context.Connection.Id;
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var tabInfo = JsonSerializer.Deserialize<TabInfo>(json, JsonOptions);
                if (tabInfo != null)
                {
                    // Chrome プロセスを特定
                    var chromeHandle = FindChromeProcessByConnection(context);
                    if (chromeHandle != IntPtr.Zero)
                    {
                        // WindowIdとWindowHandleを紐づける
                        _windowIdToHwndMap[tabInfo.WindowId] = chromeHandle;
                        Logger.Info($"HTTP/2 connection established: {connectionId}, Chrome Handle: {chromeHandle}, WindowId={tabInfo.WindowId}");
                    }
                    else
                    {
                        Logger.Info($"HTTP/2 connection established: {connectionId}, Chrome Handle not found, WindowId={tabInfo.WindowId}");
                    }
                }
                else
                {
                    Logger.Warning($"Failed to bind window handle: Invalid tabInfo or WindowId is null");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling bindWindowHandle");
            }
        }

        public async Task QueryAllTabs()
        {
            var message = new Payload
            {
                Action = "queryAllTabs",
                Data = new { }
            };

            await BroadcastMessage(message);
        }

        public async Task FocusTab(int tabId, int windowId)
        {
            var message = new Payload
            {
                Action = "focusTab",
                Data = new
                {
                    tabId,
                    windowId
                }
            };

            await BroadcastMessage(message);
            Logger.Info($"Focus tab request sent: TabId={tabId}, WindowId={windowId}");
        }

        public async Task CloseTab(int tabId, int windowId)
        {
            var message = new Payload
            {
                Action = "closeTab",
                Data = new
                {
                    tabId,
                    windowId
                }
            };

            await BroadcastMessage(message);
            Logger.Info($"Close tab request sent: TabId={tabId}, WindowId={windowId}");
        }

        private async Task BroadcastMessage(Payload payload)
        {
            var tasks = new List<Task>();

            foreach (var connection in _connections.Values)
            {
                if (connection != null)
                {
                    tasks.Add(SendMessage(connection, payload));
                }
            }

            await Task.WhenAll(tasks);
        }

        private async Task SendMessage(StreamWriter writer, Payload payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                await writer.WriteAsync($"data: {json}\n\n");
                await writer.FlushAsync();  // 非同期的にフラッシュ
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error sending message");
            }
        }

        private IntPtr FindChromeProcessByConnection(HttpContext context)
        {
            try
            {
                // リモートIPアドレスとポートを取得
                var remoteEndPoint = context.Connection.RemoteIpAddress;
                var remotePort = context.Connection.RemotePort;

                Logger.Info($"HTTP/2 connection from: {remoteEndPoint}:{remotePort}");

                // netstatの情報を使ってプロセスIDを特定
                var processId = GetProcessIdByPort(remotePort);
                if (processId > 0)
                {
                    // プロセスIDからChromeのメインウィンドウハンドルを取得
                    var chromeHandle = GetChromeMainWindowByProcessId(processId);
                    if (chromeHandle != IntPtr.Zero)
                    {
                        Logger.Info($"Chrome process found: PID={processId}, Handle={chromeHandle}");
                        return chromeHandle;
                    }
                }

                Logger.Info("Chrome process not found for this connection");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding Chrome process by connection");
                return IntPtr.Zero;
            }
        }

        private int GetProcessIdByPort(int port)
        {
            try
            {
                // netstatコマンドを実行してプロセスIDを取得
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return 0;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && parts[0] == "TCP")
                    {
                        var localEndPoint = parts[1];
                        if (localEndPoint.EndsWith($":{port}"))
                        {
                            if (int.TryParse(parts[4], out var processId))
                            {
                                // プロセスがChromeかチェック
                                try
                                {
                                    var proc = Process.GetProcessById(processId);
                                    if (proc.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase))
                                    {
                                        Logger.Info($"Found Chrome process for port {port}: PID={processId}");
                                        return processId;
                                    }
                                }
                                catch
                                {
                                    continue;
                                }
                            }
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting process ID by port using netstat");
                return 0;
            }
        }

        private IntPtr GetChromeMainWindowByProcessId(int processId)
        {
            try
            {
                // 同じプロセス名のすべてのプロセスをチェック
                var processes = Process.GetProcessesByName("chrome");

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(process.MainWindowTitle))
                        {
                            Logger.Info($"Found Chrome window: PID={process.Id}, Handle={process.MainWindowHandle}, Title={process.MainWindowTitle}");
                            return process.MainWindowHandle;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                // フォールバック：プロセス一覧から最初のChromeウィンドウを取得
                var chromeWindows = GetChromeWindowsFromProcessList();
                if (chromeWindows.Any())
                {
                    var firstChrome = chromeWindows.First();
                    Logger.Info($"Fallback Chrome window: Handle={firstChrome}");
                    return firstChrome;
                }

                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting Chrome main window for PID {processId}");
                return IntPtr.Zero;
            }
        }

        private List<IntPtr> GetChromeWindowsFromProcessList()
        {
            try
            {
                // プロセス一覧からChromeウィンドウを取得
                var chromeHandles = new List<IntPtr>();
                var processes = Process.GetProcessesByName("chrome");

                foreach (var process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        chromeHandles.Add(process.MainWindowHandle);
                    }
                }

                return chromeHandles;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting Chrome windows from process list");
                return new List<IntPtr>();
            }
        }

        public IntPtr GetHwndByWindowId(int windowId)
        {
            if (_windowIdToHwndMap.TryGetValue(windowId, out var hwnd))
            {
                return hwnd;
            }
            return IntPtr.Zero;
        }

        private void ShowNotification(NotificationData notification)
        {
            // MainWindowへの通知表示処理を実装
            App.Current.Dispatcher.Invoke(() =>
            {
                if (App.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ShowNotification(notification);
                }
            });
        }

        public void Dispose()
        {
            // 全ての接続を閉じる
            foreach (var connection in _connections.Values)
            {
                connection?.Dispose();
            }

            Logger.Info("Http2StreamHandler disposed");
        }
    }
}

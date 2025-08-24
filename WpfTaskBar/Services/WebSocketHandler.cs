using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.Net.NetworkInformation;
using System.Diagnostics;

namespace WpfTaskBar
{
    public class WebSocketHandler : IDisposable
    {
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly ConcurrentDictionary<string, IntPtr> _connectionWindowHandles = new();
        private readonly ChromeTabManager _tabManager;
        
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public WebSocketHandler(ChromeTabManager tabManager)
        {
            _tabManager = tabManager;
            
            Logger.Info("WebSocketHandler initialized");
        }

        public async Task HandleWebSocketAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            var connectionId = Guid.NewGuid().ToString();
            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            _connections[connectionId] = webSocket;

            // WebSocket接続からChromeプロセスを特定
            var chromeHandle = FindChromeProcessByConnection(context);
            if (chromeHandle != IntPtr.Zero)
            {
                _connectionWindowHandles[connectionId] = chromeHandle;
                Logger.Info($"WebSocket connection established: {connectionId}, Chrome Handle: {chromeHandle}");
            }
            else
            {
                Logger.Info($"WebSocket connection established: {connectionId}, Chrome Handle not found");
            }

            try
            {
                await HandleConnectionAsync(connectionId, webSocket);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"WebSocket error for {connectionId}");
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
                _connectionWindowHandles.TryRemove(connectionId, out _);
                Logger.Info($"WebSocket connection closed: {connectionId}");
            }
        }

        private async Task HandleConnectionAsync(string connectionId, WebSocket webSocket)
        {
            var buffer = new byte[4096];

            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessMessage(connectionId, webSocket, message);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                    break;
                }
            }
        }

        private async Task ProcessMessage(string connectionId, WebSocket webSocket, string message)
        {
            try
            {
                Logger.Info($"Raw message from {connectionId}: {message}");
                
                var webSocketMessage = JsonSerializer.Deserialize<WebSocketMessage>(message, JsonOptions);
                if (webSocketMessage == null)
                {
                    Logger.Info($"Invalid message format from {connectionId}: {message}");
                    return;
                }

                Logger.Info($"Parsed message from {connectionId}: Action='{webSocketMessage.Action}', Data={JsonSerializer.Serialize(webSocketMessage.Data, JsonOptions)}");

                switch (webSocketMessage.Action)
                {
                    case "registerTab":
                        HandleRegisterTab(webSocketMessage.Data);
                        break;
                    case "sendNotification":
                        HandleSendNotification(webSocketMessage.Data);
                        break;
                    case "ping":
                        await SendMessage(webSocket, new WebSocketMessage { Action = "pong", Data = new {} });
                        break;
                    default:
                        Logger.Info($"Unknown action: {webSocketMessage.Action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error processing message from {connectionId}");
            }
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

        private void HandleSendNotification(object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var notification = JsonSerializer.Deserialize<NotificationData>(json, JsonOptions);
                if (notification != null)
                {
                    // 通知を送信してきた接続のChromeウィンドウハンドルを設定
                    var connectionId = _connections.FirstOrDefault(c => c.Value.State == WebSocketState.Open).Key;
                    if (!string.IsNullOrEmpty(connectionId) && _connectionWindowHandles.TryGetValue(connectionId, out var handle))
                    {
                        notification.WindowHandle = handle;
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

        public async Task FocusTab(int tabId, int windowId)
        {
            var message = new WebSocketMessage
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

        private async Task BroadcastMessage(WebSocketMessage message)
        {
            var tasks = new List<Task>();

            foreach (var connection in _connections.Values)
            {
                if (connection.State == WebSocketState.Open)
                {
                    tasks.Add(SendMessage(connection, message));
                }
            }

            await Task.WhenAll(tasks);
        }

        private async Task SendMessage(WebSocket webSocket, WebSocketMessage message)
        {
            try
            {
                var json = JsonSerializer.Serialize(message, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error sending message");
            }
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

        private IntPtr FindChromeProcessByConnection(HttpContext context)
        {
            try
            {
                // リモートIPアドレスとポートを取得
                var remoteEndPoint = context.Connection.RemoteIpAddress;
                var remotePort = context.Connection.RemotePort;
                
                Logger.Info($"WebSocket connection from: {remoteEndPoint}:{remotePort}");

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

                // フォールバック：WindowManagerから最初のChromeウィンドウを取得
                var chromeWindows = GetChromeWindowsFromWindowManager();
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

        private List<IntPtr> GetChromeWindowsFromWindowManager()
        {
            try
            {
                // WindowManagerが利用できない場合のフォールバック
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
                Logger.Error(ex, "Error getting Chrome windows from WindowManager");
                return new List<IntPtr>();
            }
        }

        public void Dispose()
        {
            
            // 全ての接続を閉じる
            foreach (var connection in _connections.Values)
            {
                if (connection.State == WebSocketState.Open)
                {
                    connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
                }
            }
            
            Logger.Info("WebSocketHandler disposed");
        }
    }

    public class WebSocketMessage
    {
        public string Action { get; set; } = "";
        public object Data { get; set; } = new {};
    }

    public class TabInfo
    {
        public int TabId { get; set; }
        public int WindowId { get; set; }
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string LastActivity { get; set; } = DateTime.UtcNow.ToString("O");
    }

    public class NotificationData
    {
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public int TabId { get; set; }
        public int WindowId { get; set; }
        public string Url { get; set; } = "";
        public string TabTitle { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public IntPtr WindowHandle { get; set; } = IntPtr.Zero;
    }
}
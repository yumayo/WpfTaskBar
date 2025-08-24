using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace WpfTaskBar
{
    public class WebSocketHandler : IDisposable
    {
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly ChromeTabManager _tabManager;
        private readonly Timer _pingTimer;
        
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public WebSocketHandler(ChromeTabManager tabManager)
        {
            _tabManager = tabManager;
            
            // 60秒ごとに全ての接続にpingを送信
            _pingTimer = new Timer(SendPingToAllConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            Logger.Info("WebSocketHandler initialized with ping timer");
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

            Logger.Info($"WebSocket connection established: {connectionId}");

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
                    ShowNotification(notification);
                    Logger.Info($"Notification received: {notification.Title}");
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

        private async void SendPingToAllConnections(object? state)
        {
            var disconnectedConnections = new List<string>();
            var pingMessage = new WebSocketMessage { Action = "ping", Data = new {} };

            foreach (var kvp in _connections)
            {
                var connectionId = kvp.Key;
                var webSocket = kvp.Value;

                if (webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await SendMessage(webSocket, pingMessage);
                        Logger.Debug($"Ping sent to connection {connectionId}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"Failed to send ping to {connectionId}: {ex.Message}");
                        disconnectedConnections.Add(connectionId);
                    }
                }
                else
                {
                    disconnectedConnections.Add(connectionId);
                }
            }

            // 切断された接続をクリーンアップ
            foreach (var connectionId in disconnectedConnections)
            {
                _connections.TryRemove(connectionId, out _);
                Logger.Info($"Removed disconnected connection: {connectionId}");
            }

            if (disconnectedConnections.Count > 0)
            {
                Logger.Info($"Cleaned up {disconnectedConnections.Count} disconnected connections");
            }
        }

        public void Dispose()
        {
            _pingTimer?.Dispose();
            
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
    }
}
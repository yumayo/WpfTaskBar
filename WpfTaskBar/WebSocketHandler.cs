using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace WpfTaskBar
{
    public class WebSocketHandler
    {
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly TabManager _tabManager;

        public WebSocketHandler(TabManager tabManager)
        {
            _tabManager = tabManager;
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
                var webSocketMessage = JsonSerializer.Deserialize<WebSocketMessage>(message);
                if (webSocketMessage == null)
                {
                    Logger.Info($"Invalid message format from {connectionId}: {message}");
                    return;
                }

                Logger.Info($"Received message from {connectionId}: {webSocketMessage.Action}");

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
                var json = JsonSerializer.Serialize(data);
                var tabInfo = JsonSerializer.Deserialize<TabInfo>(json);
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
                var json = JsonSerializer.Serialize(data);
                var notification = JsonSerializer.Deserialize<NotificationData>(json);
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
                var json = JsonSerializer.Serialize(message);
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
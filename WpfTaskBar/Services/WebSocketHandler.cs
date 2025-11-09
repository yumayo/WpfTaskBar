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

        private readonly ChromeHelper _chromeHelper;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public WebSocketHandler(ChromeHelper chromeHelper)
        {
            _chromeHelper = chromeHelper;
            Logger.Info("WebSocketHandler initialized");
        }

        // WebSocket接続を処理
        public async Task HandleWebSocketAsync(HttpContext context)
        {
            var connectionId = context.Connection.Id;
            var remotePort = context.Connection.RemotePort;

            Logger.Info($"HandleWebSocketAsync: ConnectionId={connectionId}, RemotePort={remotePort}");

            WebSocket? webSocket = null;
            try
            {
                webSocket = await context.WebSockets.AcceptWebSocketAsync();
                _connections[connectionId] = webSocket;

                // 接続確認メッセージを送信
                await SendMessage(webSocket, new Payload
                {
                    Action = "connected",
                    Data = new { connectionId }
                });

                // メッセージ受信ループ
                var buffer = new byte[64 * 1024 * 1024];
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessMessage(context, message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"WebSocket error for {connectionId}");
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
                if (webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                webSocket?.Dispose();
                Logger.Info($"WebSocket connection closed: {connectionId}");
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
                    case "ping":
                        await HandlePing();
                        break;
                    case "bindWindowHandle":
                        HandleBindWindowHandle(context, payload.Data);
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
                    _chromeHelper.UpdateTab(tabInfo);
                    Logger.Info($"Tab registered: {tabInfo.TabId} - {json}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error registering tab");
            }
        }
        
        private void HandleBindWindowHandle(HttpContext context, object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var tabInfo = JsonSerializer.Deserialize<TabInfo>(json, JsonOptions);
                if (tabInfo != null)
                {
                    _chromeHelper.UpdateTab(tabInfo, context);
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

        private async Task BroadcastMessage(Payload payload)
        {
            var tasks = new List<Task>();

            foreach (var connection in _connections.Values)
            {
                if (connection.State == WebSocketState.Open)
                {
                    tasks.Add(SendMessage(connection, payload));
                }
            }

            await Task.WhenAll(tasks);
        }

        private async Task SendMessage(WebSocket webSocket, Payload payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);
                await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error sending message");
            }
        }

        public void Dispose()
        {
            // 全ての接続を閉じる
            foreach (var connection in _connections.Values)
            {
                if (connection.State == WebSocketState.Open)
                {
                    connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None).Wait();
                }
                connection?.Dispose();
            }

            Logger.Info("WebSocketHandler disposed");
        }
    }
}

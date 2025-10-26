using System.Collections.Concurrent;

namespace WpfTaskBar
{
    public class ChromeTabManager : IDisposable
    {
        private readonly ConcurrentDictionary<int, TabInfo> _tabs = new();
        private readonly ConcurrentDictionary<string, NotificationData> _notificationTabMap = new();
        private WebSocketHandler? _webSocketHandler;

        public ChromeTabManager()
        {
            Logger.Info("ChromeTabManager initialized");
        }

        public void SetWebSocketHandler(WebSocketHandler webSocketHandler)
        {
            _webSocketHandler = webSocketHandler;
            Logger.Info("WebSocketHandler set in ChromeTabManager");
        }

        public void RegisterTab(TabInfo tabInfo)
        {
            _tabs.AddOrUpdate(tabInfo.TabId, tabInfo, (key, oldValue) => tabInfo);
            Logger.Info($"Tab registered: ID={tabInfo.TabId}, Title={tabInfo.Title}, URL={tabInfo.Url}");
        }

        public void UnregisterTab(int tabId)
        {
            if (_tabs.TryRemove(tabId, out var removedTab))
            {
                Logger.Info($"Tab unregistered: ID={tabId}, Title={removedTab.Title}");
            }
        }

        public TabInfo? GetTab(int tabId)
        {
            return _tabs.TryGetValue(tabId, out var tab) ? tab : null;
        }

        public IEnumerable<TabInfo> GetAllTabs()
        {
            return _tabs.Values;
        }

        public IEnumerable<TabInfo> GetAllTabsSorted()
        {
            return _tabs.Values.OrderBy(tab => tab.WindowId).ThenBy(tab => tab.Index);
        }

        public IEnumerable<TabInfo> GetTabsByWindow(int windowId)
        {
            return _tabs.Values.Where(tab => tab.WindowId == windowId).OrderBy(tab => tab.Index);
        }

        public void AssociateNotificationWithTab(string notificationId, NotificationData notification)
        {
            _notificationTabMap[notificationId] = notification;
            Logger.Info($"Notification associated with tab: NotificationId={notificationId}, TabId={notification.TabId}");
        }

        public NotificationData? GetNotificationData(string notificationId)
        {
            return _notificationTabMap.TryGetValue(notificationId, out var notification) ? notification : null;
        }

        public void RemoveNotificationAssociation(string notificationId)
        {
            if (_notificationTabMap.TryRemove(notificationId, out var removedNotification))
            {
                Logger.Info($"Notification association removed: NotificationId={notificationId}, TabId={removedNotification.TabId}");
            }
        }

        public void ClearExpiredTabs(TimeSpan expiredTime)
        {
            var expiredTabIds = new List<int>();
            var cutoffTime = DateTime.UtcNow - expiredTime;

            foreach (var tab in _tabs.Values)
            {
                if (DateTime.TryParse(tab.LastActivity, out var lastActivity) && lastActivity < cutoffTime)
                {
                    expiredTabIds.Add(tab.TabId);
                }
            }

            foreach (var tabId in expiredTabIds)
            {
                UnregisterTab(tabId);
            }

            if (expiredTabIds.Count > 0)
            {
                Logger.Info($"Expired tabs removed: {expiredTabIds.Count} tabs");
            }
        }

        public int GetTabCount()
        {
            return _tabs.Count;
        }

        public bool HasTab(int tabId)
        {
            return _tabs.ContainsKey(tabId);
        }

        public void Dispose()
        {
            Logger.Info("ChromeTabManager disposed");
        }
    }
}
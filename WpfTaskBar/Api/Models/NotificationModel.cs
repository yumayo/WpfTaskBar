using System.Collections.ObjectModel;
using System.Windows;

namespace WpfTaskBar
{
    public class NotificationModel
    {
        private static readonly ObservableCollection<NotificationItem> _notifications = new ObservableCollection<NotificationItem>();

        public static ObservableCollection<NotificationItem> Notifications => _notifications;

        public static void AddNotification(string title, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var notification = new NotificationItem
                {
                    Id = Guid.NewGuid(),
                    Title = title,
                    Message = message,
                    Timestamp = DateTime.Now
                };

                // 新しいものを先頭に追加
                _notifications.Insert(0, notification);

                // 500件を超える場合は古いものを削除
                while (_notifications.Count > 500)
                {
                    _notifications.RemoveAt(_notifications.Count - 1);
                }
            });
        }

        public static void ClearNotifications()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _notifications.Clear();
            });
        }
    }

    public class NotificationItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
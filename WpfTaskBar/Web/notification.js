const notifications = [];

// 通知エリアの更新
function updateNotifications(notificationData) {
    notifications = notificationData || [];
    const notificationArea = document.getElementById('notificationArea');

    if (notifications.length === 0) {
        notificationArea.classList.remove('visible');
        return;
    }

    notificationArea.classList.add('visible');
    notificationArea.innerHTML = '';

    notifications.forEach(notification => {
        const notificationItem = createNotificationItem(notification);
        notificationArea.appendChild(notificationItem);
    });
}

// 通知アイテムの作成
function createNotificationItem(notification) {
    const item = document.createElement('div');
    item.className = 'notification-item';

    const title = document.createElement('div');
    title.className = 'notification-title';

    const titleText = document.createElement('span');
    titleText.textContent = notification.title;

    const time = document.createElement('span');
    time.className = 'notification-time';
    time.textContent = formatTime(new Date(notification.timestamp));

    title.appendChild(titleText);
    title.appendChild(time);

    const message = document.createElement('div');
    message.className = 'notification-message';
    message.textContent = notification.message;

    item.appendChild(title);
    item.appendChild(message);

    // クリックイベント
    item.addEventListener('click', () => {
        sendMessageToHost('notification_click', {
            id: notification.id,
            windowHandle: notification.windowHandle
        });
    });

    return item;
}

window.chrome?.webview?.addEventListener('message', function(event) {
    let data;
    try {
        if (typeof event.data === 'string') {
            data = JSON.parse(event.data);
        } else {
            data = event.data;
        }
    } catch (error) {
        console.error('❌ JSONパースエラー:', error);
        console.error('受信データ:', event.data);
        return;
    }

    if (!data) {
        console.error('❌ 受信データがnullまたはundefinedです');
        return;
    }

    switch (data.type) {
        case 'notification_update':
            updateNotifications(data.notifications);
            break;
        default:
            break;
    }
});

let notifications = [];
let pinnedTabs = [];

// 通知エリアの更新
function updateNotifications(notificationData) {
    notifications = notificationData || [];
    renderNotificationArea();
}

// ピン留めされたタブを更新
function updatePinnedTabs(tabsData) {
    pinnedTabs = tabsData || [];
    renderNotificationArea();
}

// 通知エリアをレンダリング
function renderNotificationArea() {
    const notificationArea = document.getElementById('notificationArea');

    if (notifications.length === 0 && pinnedTabs.length === 0) {
        notificationArea.classList.remove('visible');
        return;
    }

    notificationArea.classList.add('visible');
    notificationArea.innerHTML = '';

    // ピン留めされたタブのアイコンを表示
    if (pinnedTabs.length > 0) {
        const pinnedTabsContainer = document.createElement('div');
        pinnedTabsContainer.className = 'pinned-tabs-container';

        pinnedTabs.forEach(tab => {
            const tabIcon = createPinnedTabIcon(tab);
            pinnedTabsContainer.appendChild(tabIcon);
        });

        notificationArea.appendChild(pinnedTabsContainer);
    }

    // 通知を表示
    notifications.forEach(notification => {
        const notificationItem = createNotificationItem(notification);
        notificationArea.appendChild(notificationItem);
    });
}

// ピン留めされたタブのアイコンを作成
function createPinnedTabIcon(tab) {
    const icon = document.createElement('div');
    icon.className = 'pinned-tab-icon';
    icon.style.userSelect = 'none';
    icon.title = tab.title || 'Pinned Tab';

    const img = document.createElement('img');
    if (tab.favIconData) {
        img.src = tab.favIconData;
    } else if (tab.favIconUrl) {
        img.src = tab.favIconUrl;
    } else {
        img.src = 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect fill="%23999" width="16" height="16"/></svg>';
    }
    img.style.width = '24px';
    img.style.height = '24px';
    img.style.borderRadius = '4px';

    icon.appendChild(img);

    return icon;
}

// 通知アイテムの作成
function createNotificationItem(notification) {
    const item = document.createElement('div');
    item.className = 'notification-item';
    item.style.userSelect = 'none';

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

    // マウスイベント
    item.addEventListener('mouseenter', () => {
        item.style.backgroundColor = 'rgba(255, 255, 255, 0.1)';
    });

    item.addEventListener('mouseleave', () => {
        item.style.backgroundColor = '';
    });

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

    if (typeof event.data === 'string') {
        data = JSON.parse(event.data);
    } else {
        data = event.data;
    }

    if (!data) {
        return;
    }

    if (data.type === 'notification_update') {
        console.log('notification_update');
        updateNotifications(data.notifications);
    } else if (data.type === 'pinned_tabs_response') {
        console.log('pinned_tabs_response');
        updatePinnedTabs(data.tabs);
    }
});

// ピン留めされたタブを定期的に更新（500ms間隔）
setInterval(() => {
    if (window.chrome?.webview) {
        window.chrome.webview.postMessage({
            type: 'request_pinned_tabs'
        });
    }
}, 500);

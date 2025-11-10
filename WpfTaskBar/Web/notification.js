let pinnedTabs = [];

// ピン留めされたタブを更新
function updatePinnedTabs(tabsData) {
    pinnedTabs = tabsData || [];
    renderNotificationArea();
}

// 通知エリアをレンダリング
function renderNotificationArea() {
    const notificationArea = document.getElementById('notificationArea');

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
    
    switch (data.type) {
        case 'pinned_tabs_response':
            console.log('pinned_tabs_response');
            updatePinnedTabs(data.tabs);
            break;
        default:
            break;
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

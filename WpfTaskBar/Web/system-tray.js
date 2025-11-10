let pinnedTabs = [];

// ピン留めされたタブを更新
function updatePinnedTabs(tabsData) {
    pinnedTabs = tabsData || [];
    renderSystemTray();
}

// システムトレイをレンダリング
function renderSystemTray() {
    const systemTray = document.getElementById('systemTray');
    systemTray.classList.add('visible');

    // ピン留めされたタブのアイコンを表示
    if (pinnedTabs.length > 0) {
        let pinnedTabsContainer = systemTray.querySelector('.pinned-tabs-container');

        if (!pinnedTabsContainer) {
            pinnedTabsContainer = document.createElement('div');
            pinnedTabsContainer.className = 'pinned-tabs-container';
            systemTray.appendChild(pinnedTabsContainer);
        }

        // 既存のタブIDを取得
        const existingTabIds = new Set(
            Array.from(pinnedTabsContainer.children).map(child => child.dataset.tabId)
        );

        // 新しいタブIDのセット
        const newTabIds = new Set(pinnedTabs.map(tab => String(tab.tabId)));

        // 不要なタブアイコンを削除
        Array.from(pinnedTabsContainer.children).forEach(child => {
            if (!newTabIds.has(child.dataset.tabId)) {
                pinnedTabsContainer.removeChild(child);
            }
        });

        // tab.indexでソートしてからタブアイコンを更新または追加
        const sortedTabs = [...pinnedTabs].sort((a, b) => (a.index || 0) - (b.index || 0));
        sortedTabs.forEach((tab, index) => {
            const tabId = String(tab.tabId);
            let tabIcon = pinnedTabsContainer.querySelector(`[data-tab-id="${tabId}"]`);

            if (tabIcon) {
                // 既存のアイコンを更新
                updatePinnedTabIcon(tabIcon, tab);
            } else {
                // 新しいアイコンを作成
                tabIcon = createPinnedTabIcon(tab);
                pinnedTabsContainer.appendChild(tabIcon);
            }

            // 順序を調整
            const currentIndex = Array.from(pinnedTabsContainer.children).indexOf(tabIcon);
            if (currentIndex !== index) {
                if (index < pinnedTabsContainer.children.length) {
                    pinnedTabsContainer.insertBefore(tabIcon, pinnedTabsContainer.children[index]);
                } else {
                    pinnedTabsContainer.appendChild(tabIcon);
                }
            }
        });
    } else {
        // ピン留めされたタブがない場合はコンテナを削除
        const pinnedTabsContainer = systemTray.querySelector('.pinned-tabs-container');
        if (pinnedTabsContainer) {
            systemTray.removeChild(pinnedTabsContainer);
        }
    }
}

// ピン留めされたタブのアイコンを作成
function createPinnedTabIcon(tab) {
    const icon = document.createElement('div');
    icon.className = 'pinned-tab-icon';
    icon.style.userSelect = 'none';
    icon.dataset.tabId = String(tab.tabId);
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

    // クリックイベントを追加
    icon.addEventListener('click', () => {
        activateTab(tab.tabId);
    });

    return icon;
}

// タブをアクティブ化
function activateTab(tabId) {
    if (window.chrome?.webview) {
        window.chrome.webview.postMessage({
            type: 'activate_tab',
            tabId: tabId
        });
    }
}

// ピン留めされたタブのアイコンを更新
function updatePinnedTabIcon(icon, tab) {
    icon.title = tab.title || 'Pinned Tab';

    const img = icon.querySelector('img');
    if (img) {
        let newSrc;
        if (tab.favIconData) {
            newSrc = tab.favIconData;
        } else if (tab.favIconUrl) {
            newSrc = tab.favIconUrl;
        } else {
            newSrc = 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect fill="%23999" width="16" height="16"/></svg>';
        }

        // srcが変更されている場合のみ更新（不要な再読み込みを避ける）
        if (img.src !== newSrc) {
            img.src = newSrc;
        }
    }
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

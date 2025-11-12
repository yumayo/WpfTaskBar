// Chrome内部ページのアイコンを取得（SVGで定義）
function getChromeInternalPageIcon(hostname) {
    // chrome:// ページのアイコンをSVGで定義
    const iconMap = {
        'extensions': 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="%23c1c9d3" d="M20.5 11H19V7c0-1.1-.9-2-2-2h-4V3.5C13 2.12 11.88 1 10.5 1S8 2.12 8 3.5V5H4c-1.1 0-1.99.9-1.99 2v3.8H3.5c1.49 0 2.7 1.21 2.7 2.7s-1.21 2.7-2.7 2.7H2V20c0 1.1.9 2 2 2h3.8v-1.5c0-1.49 1.21-2.7 2.7-2.7 1.49 0 2.7 1.21 2.7 2.7V22H17c1.1 0 2-.9 2-2v-4h1.5c1.38 0 2.5-1.12 2.5-2.5S21.88 11 20.5 11z"/></svg>',
        'settings': 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="%23c1c9d3" d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>',
        'history': 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="%23c1c9d3" d="M13 3c-4.97 0-9 4.03-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42C8.27 19.99 10.51 21 13 21c4.97 0 9-4.03 9-9s-4.03-9-9-9zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z"/></svg>',
        'downloads': 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="%23c1c9d3" d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>',
        'bookmarks': 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="%23c1c9d3" d="M17 3H7c-1.1 0-1.99.9-1.99 2L5 21l7-3 7 3V5c0-1.1-.9-2-2-2z"/></svg>',
        'newtab': 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="%23c1c9d3" d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z"/></svg>',
        'apps': 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="%235f6368" d="M4 8h4V4H4v4zm6 12h4v-4h-4v4zm-6 0h4v-4H4v4zm0-6h4v-4H4v4zm6 0h4v-4h-4v4zm6-10v4h4V4h-4zm-6 4h4V4h-4v4zm6 6h4v-4h-4v4zm0 6h4v-4h-4v4z"/></svg>',
    };

    return iconMap[hostname] || 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle fill="%234285f4" cx="12" cy="12" r="10"/><circle fill="%23ffffff" cx="12" cy="12" r="6"/></svg>'; // デフォルトはChromeロゴ風
}

// タブ情報を登録（リトライ処理付き）
export function webSocketRequestUpdateTab(webSocketClient, tab, status = null, retryCount = 0) {
    const maxRetries = 3;
    const retryDelay = 1000; // 1秒

    if (!webSocketClient.getConnectionStatus()) {
        if (retryCount < maxRetries) {
            console.log(`WebSocket not connected. Retrying in ${retryDelay}ms... (${retryCount + 1}/${maxRetries})`);
            setTimeout(() => {
                webSocketRequestUpdateTab(webSocketClient, tab, retryCount + 1);
            }, retryDelay);
        } else {
            console.warn(`Failed to register tab after ${maxRetries} retries:`, tab);
        }
        return;
    }

    // FavIconUrlが空の場合、URLから生成する
    let favIconUrl = tab.favIconUrl;
    if (!favIconUrl && tab.url) {
        try {
            const url = new URL(tab.url);
            // chrome:// ページの場合、Chrome内部アイコンを使用
            if (url.protocol === 'chrome:') {
                favIconUrl = getChromeInternalPageIcon(url.hostname);
            }
        } catch (e) {
            console.warn('Failed to generate favicon URL:', e);
        }
    }

    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title,
        favIconUrl: favIconUrl,
        active: tab.active,
        pinned: tab.pinned,
        index: tab.index,
    };

    webSocketClient.sendMessage({
        action: 'updateTab',
        data: tabInfo
    });
}

// タブ削除を通知
export function webSocketRequestRemoveTab(webSocketClient, tabId, windowId) {
    if (!webSocketClient.getConnectionStatus()) {
        console.warn('WebSocket not connected. Cannot send remove tab notification.');
        return;
    }

    webSocketClient.sendMessage({
        action: 'removeTab',
        data: {
            tabId: tabId,
            windowId: windowId
        }
    });
}

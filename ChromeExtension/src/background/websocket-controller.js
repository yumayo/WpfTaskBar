// タブ情報を登録（リトライ処理付き）
export function webSocketRequestRegisterTab(webSocketClient, tab, status = null, retryCount = 0) {
    const maxRetries = 3;
    const retryDelay = 1000; // 1秒

    if (!webSocketClient.getConnectionStatus()) {
        if (retryCount < maxRetries) {
            console.log(`WebSocket not connected. Retrying in ${retryDelay}ms... (${retryCount + 1}/${maxRetries})`);
            setTimeout(() => {
                webSocketRequestRegisterTab(webSocketClient, tab, retryCount + 1);
            }, retryDelay);
        } else {
            console.warn(`Failed to register tab after ${maxRetries} retries:`, tab);
        }
        return;
    }

    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title,
        favIconUrl: tab.favIconUrl,
        active: tab.active,
        pinned: tab.pinned,
        index: tab.index,
        status: tab.status,
    };

    webSocketClient.sendMessage({
        action: 'registerTab',
        data: tabInfo
    });
}

// タブ情報を登録
export function webSocketRequestRegisterTab(webSocketClient, tab) {
    if (!webSocketClient.getConnectionStatus()) return;

    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title || 'Untitled',
        favIconUrl: tab.favIconUrl || '',
        active: tab.active || false,
        index: tab.index || 0,
    };

    webSocketClient.sendMessage({
        action: 'registerTab',
        data: tabInfo
    });

    console.log('Tab registered:', tabInfo);
}

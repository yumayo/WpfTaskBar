// タブ情報を登録
export function webSocketRequestRegisterTab(webSocketClient, tab) {
    if (!webSocketClient.getConnectionStatus()) return;

    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title,
        favIconUrl: tab.favIconUrl,
        active: tab.active,
        pinned: tab.pinned,
        index: tab.index,
    };

    webSocketClient.sendMessage({
        action: 'registerTab',
        data: tabInfo
    });

    console.log('Tab registered:', tabInfo);
}

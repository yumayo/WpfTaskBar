// タブイベントリスナーを設定
export function setupTabEventListeners(webSocketClient) {
    chrome.tabs.onCreated.addListener((tab) => {
        registerTab(webSocketClient, tab);
    });

    chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
        console.log('【OnUpdated】 Tab updated:', tab, 'changeInfo:', changeInfo);

        if (changeInfo.url || changeInfo.title || changeInfo.favIconUrl) {
            console.log('【OnUpdated】Tab property changed (url/title/favicon), registering tab:', tab);
            registerTab(webSocketClient, tab);
        }

        // ページの読み込みが完了した時にfaviconが確定するのでタブを再登録
        if (changeInfo.status === 'complete') {
            console.log('【OnUpdated】Tab loading complete, notifying tabs change and re-registering:', tab);
            registerTab(webSocketClient, tab);
        }
    });

    // アクティブなタブが変更された時にタブ情報を再登録し、すぐに通知
    chrome.tabs.onActivated.addListener((activeInfo) => {
        chrome.tabs.get(activeInfo.tabId, (tab) => {
            if (chrome.runtime.lastError) {
                console.error('Failed to get tab:', chrome.runtime.lastError);
                return;
            }

            registerTab(webSocketClient, tab);
        });
    });
}

// 現在のタブ情報を登録
export function registerCurrentTabs(webSocketClient) {
    chrome.tabs.query({}, (tabs) => {
        tabs.forEach(tab => {
            registerTab(webSocketClient, tab);
        });
    });
}

// タブ情報を登録
export function registerTab(webSocketClient, tab) {
    if (!webSocketClient.getConnectionStatus()) return;

    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title || 'Untitled',
        favIconUrl: tab.favIconUrl || '',
        active: tab.active || false,
        index: tab.index || 0
    };

    webSocketClient.sendMessage({
        action: 'registerTab',
        data: tabInfo
    });

    console.log('Tab registered:', tabInfo);
}

// ポップアップとの通信を処理するモジュール

// メッセージリスナー（ポップアップからのメッセージを受信）
export function setupPopupMessageListener(getConnectionStatus, sendTestNotification) {
    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
        switch (message.action) {
            case 'sendTestNotification':
                chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
                    if (tabs[0]) {
                        sendTestNotification(tabs[0].id);
                        sendResponse({ success: true });
                    }
                });
                return true; // 非同期レスポンス
                
            case 'getConnectionStatus':
                sendResponse({ connected: getConnectionStatus() });
                break;
                
            default:
                sendResponse({ error: 'Unknown action' });
                break;
        }
    });
}
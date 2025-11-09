// ポップアップとの通信を処理するモジュール

// メッセージリスナー（ポップアップからのメッセージを受信）
export function setupPopupMessageListener(getConnectionStatus) {
    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
        switch (message.action) {
            case 'getConnectionStatus':
                sendResponse({ connected: getConnectionStatus() });
                break;
                
            default:
                break;
        }
    });
}

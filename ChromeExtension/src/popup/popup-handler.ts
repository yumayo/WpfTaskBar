// ポップアップとの通信を処理するモジュール

// メッセージリスナー(ポップアップからのメッセージを受信)
export function setupPopupMessageListener(getConnectionStatus: () => boolean): void {
    chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
        switch (message.action) {
            case 'getConnectionStatus':
                sendResponse({ connected: getConnectionStatus() });
                break;

            default:
                break;
        }
    });
}

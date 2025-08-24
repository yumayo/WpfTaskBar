// WebSocketメッセージハンドラーを処理するモジュール

// メッセージ処理のメインハンドラー
export function handleMessage(message) {
    switch (message.action) {
        case 'focusTab':
            handleFocusTab(message.data);
            break;
        default:
            console.log('Unknown message action:', message.action);
            break;
    }
}

// タブフォーカス処理
function handleFocusTab(data) {
    console.log('Focus tab request:', data);
    
    chrome.tabs.update(data.tabId, { active: true }, (tab) => {
        if (chrome.runtime.lastError) {
            console.error('Failed to focus tab:', chrome.runtime.lastError);
        } else {
            console.log('Successfully focused tab and tab');
        }
    });
}
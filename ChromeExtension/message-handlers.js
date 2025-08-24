// WebSocketメッセージハンドラーを処理するモジュール

import { sendMessage } from './websocket-client.js';

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
            return;
        }
        
        // ウィンドウも最前面に表示
        chrome.windows.update(data.windowId, { focused: true }, (window) => {
            if (chrome.runtime.lastError) {
                console.error('Failed to focus window:', chrome.runtime.lastError);
            } else {
                console.log('Successfully focused tab and window');
            }
        });
    });
}
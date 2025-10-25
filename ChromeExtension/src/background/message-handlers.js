// WebSocketメッセージハンドラーを処理するモジュール

import { sendMessage } from '../utils/websocket-client.js';

// メッセージ処理のメインハンドラー
export function handleMessage(message) {
    switch (message.action) {
        case 'focusTab':
            handleFocusTab(message.data);
            break;
        case 'closeTab':
            handleCloseTab(message.data);
            break;
        case 'queryAllTabs':
            handleQueryAllTabs();
            break;
        default:
            console.log('Unknown message action:', message.action);
            break;
    }
}

// タブクエリ処理
async function handleQueryAllTabs() {
    try {
        // すべてのタブを取得
        const tabs = await chrome.tabs.query({});

        // タブ情報を整形
        const tabsInfo = tabs.map(tab => ({
            tabId: tab.id,
            windowId: tab.windowId,
            url: tab.url || '',
            title: tab.title || '',
            faviconUrl: tab.favIconUrl || '',
            isActive: tab.active,
            lastActivity: new Date().toISOString()
        }));

        // WebSocket経由でタブ情報を送信
        sendMessage({
            action: 'updateTabs',
            data: {
                tabs: tabsInfo
            }
        });
    } catch (error) {
        console.error('Failed to query tabs:', error);
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

// タブクローズ処理
function handleCloseTab(data) {
    console.log('Close tab request:', data);

    chrome.tabs.remove(data.tabId, () => {
        if (chrome.runtime.lastError) {
            console.error('Failed to close tab:', chrome.runtime.lastError);
        } else {
            console.log('Successfully closed tab:', data.tabId);
        }
    });
}
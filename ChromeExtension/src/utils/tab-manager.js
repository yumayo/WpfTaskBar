// タブ管理とイベント処理を行うモジュール

import { sendMessage, getConnectionStatus } from './websocket-client.js';
import { registerTab, unregisterTab } from './tab-registration.js';

// 通知を送信（テスト用）
export function sendTestNotification(tabId) {
    chrome.tabs.get(tabId, (tab) => {
        if (chrome.runtime.lastError) {
            console.error('Failed to get tab info:', chrome.runtime.lastError);
            return;
        }

        if (!getConnectionStatus()) {
            console.log('WebSocket not connected, skipping page loaded notification');
            return;
        }
        
        const notification = {
            title: 'テスト通知',
            message: `${tab.title} からの通知です`,
            tabId: tab.id,
            windowId: tab.windowId,
            url: tab.url,
            tabTitle: tab.title,
            timestamp: new Date().toISOString()
        };
        
        sendMessage({
            action: 'sendNotification',
            data: notification
        });
        
        console.log('Test notification sent:', notification);
    });
}

// タブイベントリスナーを設定
export function setupTabEventListeners() {
    chrome.tabs.onCreated.addListener((tab) => {
        registerTab(tab);
    });

    chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
        if (changeInfo.url || changeInfo.title || changeInfo.favIconUrl) {
            registerTab(tab);
        }

        // ページの読み込みが完了した時にfaviconが確定するのでタブを再登録
        if (changeInfo.status === 'complete') {
            registerTab(tab);
        }
    });

    // アクティブなタブが変更された時にタブ情報を再登録
    chrome.tabs.onActivated.addListener((activeInfo) => {
        chrome.tabs.get(activeInfo.tabId, (tab) => {
            if (chrome.runtime.lastError) {
                console.error('Failed to get tab:', chrome.runtime.lastError);
                return;
            }

            registerTab(tab);
        });
    });

    chrome.tabs.onRemoved.addListener((tabId) => {
        console.log('Tab removed:', tabId);
        unregisterTab(tabId);
    });
}

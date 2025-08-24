// タブ管理とイベント処理を行うモジュール

import { sendMessage, getConnectionStatus } from './websocket-client.js';
import { registerTab } from './tab-registration.js';

// 通知を送信（テスト用）
export function sendTestNotification(tabId) {
    chrome.tabs.get(tabId, (tab) => {
        if (chrome.runtime.lastError) {
            console.error('Failed to get tab info:', chrome.runtime.lastError);
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

// タブ用のテスト通知を送信
function sendTestNotificationForTab(tab) {
    if (!getConnectionStatus()) {
        console.log('WebSocket not connected, skipping tab notification');
        return;
    }

    const notification = {
        title: '新しいタブが開かれました',
        message: `${tab.title || tab.url || 'New Tab'} が開かれました`,
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        tabTitle: tab.title || 'New Tab',
        timestamp: new Date().toISOString()
    };

    sendMessage({
        action: 'sendNotification',
        data: notification
    });

    console.log('Tab opened notification sent:', notification);
}

// ページ読み込み完了通知
function sendPageLoadedNotification(tab) {
    if (!getConnectionStatus()) {
        console.log('WebSocket not connected, skipping page loaded notification');
        return;
    }

    const notification = {
        title: 'ページが読み込まれました',
        message: `${tab.title || tab.url} の読み込みが完了しました`,
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        tabTitle: tab.title || 'Untitled',
        timestamp: new Date().toISOString()
    };

    sendMessage({
        action: 'sendNotification',
        data: notification
    });

    console.log('Page loaded notification sent:', notification);
}

// タブイベントリスナーを設定
export function setupTabEventListeners() {
    chrome.tabs.onCreated.addListener((tab) => {
        if (tab.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('chrome-extension://')) {
            registerTab(tab);
            
            // テスト用: 新しいタブが開かれた時に通知を送信
            setTimeout(() => {
                sendTestNotificationForTab(tab);
            }, 1000); // 1秒後に送信（タブの読み込みを待つ）
        }
    });

    chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
        if (changeInfo.url || changeInfo.title) {
            if (tab.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('chrome-extension://')) {
                registerTab(tab);
            }
        }
        
        // ページの読み込みが完了した時にテスト通知を送信
        if (changeInfo.status === 'complete' && tab.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('chrome-extension://')) {
            setTimeout(() => {
                sendPageLoadedNotification(tab);
            }, 500);
        }
    });

    chrome.tabs.onRemoved.addListener((tabId) => {
        console.log('Tab removed:', tabId);
        // タブ削除の通知は必要に応じて実装
    });
}
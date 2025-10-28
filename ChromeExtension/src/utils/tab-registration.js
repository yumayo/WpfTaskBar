// タブ登録機能を処理するモジュール

import { wsClient } from '../background/background.js';
import { sanitizeTabInfo } from './sanitizer.js';

// 現在のタブ情報を登録
export function registerCurrentTabs() {
    chrome.tabs.query({}, (tabs) => {
        tabs.forEach(tab => {
            registerTab(tab);
        });
    });
}

// タブ情報を登録
export function registerTab(tab) {
    if (!wsClient.getConnectionStatus()) return;

    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title || 'Untitled',
        faviconUrl: tab.favIconUrl || '',
        isActive: tab.active || false,
        index: tab.index || 0,
        lastActivity: new Date().toISOString()
    };

    // サニタイゼーション適用
    const sanitized = sanitizeTabInfo(tabInfo);

    wsClient.sendMessage({
        action: 'registerTab',
        data: sanitized
    });

    console.log('Tab registered:', sanitized);
}

// タブ情報を登録解除
export function unregisterTab(tabId) {
    if (!wsClient.getConnectionStatus()) return;

    wsClient.sendMessage({
        action: 'unregisterTab',
        data: {
            tabId: tabId
        }
    });

    console.log('Tab unregistered:', tabId);
}
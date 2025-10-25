// タブ登録機能を処理するモジュール

import { sendMessage, getConnectionStatus } from './websocket-client.js';

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
    if (!getConnectionStatus()) return;

    // faviconUrlをデバッグ
    console.log('Tab object:', tab);
    console.log('favIconUrl:', tab.favIconUrl);

    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title || 'Untitled',
        faviconUrl: tab.favIconUrl || '',
        isActive: tab.active || false
    };

    sendMessage({
        action: 'registerTab',
        data: tabInfo
    });

    console.log('Tab registered:', tabInfo);
}
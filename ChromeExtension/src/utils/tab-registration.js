// タブ登録機能を処理するモジュール

import { steamClient } from '../background/background.js';

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
    if (!steamClient.getConnectionStatus()) return;

    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title || 'Untitled',
        faviconUrl: tab.favIconUrl || '',
        isActive: tab.active || false,
        index: tab.index || 0
    };

    steamClient.sendMessage({
        action: 'registerTab',
        data: tabInfo
    });

    console.log('Tab registered:', tabInfo);
}

// タブ情報を登録解除
export function unregisterTab(tabId) {
    if (!steamClient.getConnectionStatus()) return;

    steamClient.sendMessage({
        action: 'unregisterTab',
        data: {
            tabId: tabId
        }
    });

    console.log('Tab unregistered:', tabId);
}
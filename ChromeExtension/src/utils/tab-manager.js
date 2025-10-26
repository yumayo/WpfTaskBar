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
        // タブ作成時にすべてのタブ情報を送信
        notifyTabsChange();
    });

    chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
        if (changeInfo.url || changeInfo.title || changeInfo.favIconUrl) {
            registerTab(tab);
        }

        // ページの読み込みが完了した時にfaviconが確定するのでタブを再登録
        if (changeInfo.status === 'complete') {
            registerTab(tab);
            // タブ更新時にすべてのタブ情報を送信
            notifyTabsChange();
        }
    });

    // アクティブなタブが変更された時にタブ情報を再登録し、すぐに通知
    chrome.tabs.onActivated.addListener((activeInfo) => {
        chrome.tabs.get(activeInfo.tabId, (tab) => {
            if (chrome.runtime.lastError) {
                console.error('Failed to get tab:', chrome.runtime.lastError);
                return;
            }

            registerTab(tab);

            // アクティブタブの変更を即座にWpfTaskBarに通知（全タブ情報を送信）
            notifyTabsChange();
        });
    });

    chrome.tabs.onRemoved.addListener((tabId) => {
        console.log('Tab removed:', tabId);
        unregisterTab(tabId);
        // タブ削除時にすべてのタブ情報を送信
        notifyTabsChange();
    });

    // タブが並び替えられた時に通知
    chrome.tabs.onMoved.addListener((tabId, moveInfo) => {
        console.log('Tab moved:', tabId, 'from index', moveInfo.fromIndex, 'to index', moveInfo.toIndex);
        // タブの並び替え時にすべてのタブ情報を送信
        notifyTabsChange();
    });
}

// すべてのタブ情報を送信する汎用関数
function notifyTabsChange() {
    if (!getConnectionStatus()) {
        console.log('WebSocket not connected, skipping tabs change notification');
        return;
    }

    chrome.tabs.query({}, (tabs) => {
        const tabsInfo = tabs.map(t => ({
            tabId: t.id,
            windowId: t.windowId,
            url: t.url || '',
            title: t.title || '',
            faviconUrl: t.favIconUrl || '',
            isActive: t.active,
            lastActivity: new Date().toISOString(),
            index: t.index || 0
        }));

        sendMessage({
            action: 'updateTabs',
            data: {
                tabs: tabsInfo
            }
        });

        console.log('Tabs change notification sent:', tabsInfo.length, 'tabs');
    });
}

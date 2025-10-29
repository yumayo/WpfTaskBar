// メイン背景スクリプト - モジュール化されたファイルをまとめる

import { HttpStreamClient } from '../utils/http-stream-client.js';
import { setupTabEventListeners, sendTestNotification } from '../utils/tab-manager.js';
import { setupPopupMessageListener } from '../popup/popup-handler.js';
import { handleMessage } from './message-handlers.js';
import { startHeartbeat, stopHeartbeat } from './heartbeat.js';
import { registerCurrentTabs } from '../utils/tab-registration.js';

// HTTP/2 Streamクライアントのインスタンスを作成
const steamClient = new HttpStreamClient();

// 拡張機能の初期化
chrome.runtime.onStartup.addListener(() => {
    steamClient.initialize();
});

chrome.runtime.onInstalled.addListener(() => {
    steamClient.initialize();
});

// ポップアップとの通信設定
setupPopupMessageListener(() => steamClient.getConnectionStatus(), sendTestNotification);

// タブイベントリスナー設定
setupTabEventListeners();

// HTTP/2コールバックを登録
steamClient.onConnected(() => {
    // ハートビートを開始
    startHeartbeat();

    // 既存のタブ情報を登録
    registerCurrentTabs();
});

steamClient.onMessage((message) => {
    handleMessage(message);
});

steamClient.onDisconnected(() => {
    // ハートビートを停止
    stopHeartbeat();
});

// content scriptからのメッセージを処理
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    switch (message.action) {
        case 'getTabInfo':
            // content scriptに現在のタブ情報を返す
            if (sender.tab) {
                sendResponse({
                    tabId: sender.tab.id,
                    windowId: sender.tab.windowId,
                    url: sender.tab.url,
                    title: sender.tab.title
                });
            } else {
                sendResponse(null);
            }
            break;
        case 'getConnectionId':
            // content scriptにconnectionIdを返す
            const connectionId = steamClient.connectionId;
            console.log('[TCP調査] [Background] connectionId requested by content script:', connectionId);
            sendResponse({ connectionId });
            break;
        default:
            // 他のメッセージは既存のハンドラーに任せる
            break;
    }
    return true; // 非同期レスポンスを許可
});

// 初期化
steamClient.initialize();

// wsClientをグローバルに公開して他のモジュールから参照可能にする
export { steamClient };
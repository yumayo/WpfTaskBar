// メイン背景スクリプト - モジュール化されたファイルをまとめる

import { WebSocketClient } from '../utils/websocket-client.js';
import { setupTabEventListeners, sendTestNotification } from '../utils/tab-manager.js';
import { setupPopupMessageListener } from '../popup/popup-handler.js';
import { handleMessage } from './message-handlers.js';
import { startHeartbeat, stopHeartbeat } from './heartbeat.js';
import { registerCurrentTabs } from '../utils/tab-registration.js';

// WebSocketクライアントのインスタンスを作成
const webSocketClient = new WebSocketClient();

// 拡張機能の初期化
chrome.runtime.onStartup.addListener(() => {
    webSocketClient.initialize();
});

chrome.runtime.onInstalled.addListener(() => {
    webSocketClient.initialize();
});

// ポップアップとの通信設定
setupPopupMessageListener(() => webSocketClient.getConnectionStatus(), sendTestNotification);

// タブイベントリスナー設定
setupTabEventListeners();

// WebSocketコールバックを登録
webSocketClient.onConnected(() => {
    // ハートビートを開始
    startHeartbeat();

    // 既存のタブ情報を登録
    registerCurrentTabs();
});

webSocketClient.onMessage((message) => {
    handleMessage(message);
});

webSocketClient.onDisconnected(() => {
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
        default:
            // 他のメッセージは既存のハンドラーに任せる
            break;
    }
    return true; // 非同期レスポンスを許可
});

// 初期化
webSocketClient.initialize();

// wsClientをグローバルに公開して他のモジュールから参照可能にする
export { webSocketClient };
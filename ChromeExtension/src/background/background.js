// メイン背景スクリプト - モジュール化されたファイルをまとめる

import { WebSocketClient } from './websocket-client.js';
import { registerCurrentTabs, setupTabEventListeners } from './tab-manager.js';
import { setupPopupMessageListener } from '../popup/popup-handler.js';
import { startHeartbeat, stopHeartbeat } from './heartbeat.js';

// WebSocketクライアントのインスタンスを作成
const webSocketClient = new WebSocketClient();

// ポップアップとの通信設定
setupPopupMessageListener(() => webSocketClient.getConnectionStatus());

// タブイベントリスナー設定
setupTabEventListeners(webSocketClient);

// WebSocketコールバックを登録
webSocketClient.onConnected(() => {
    // ハートビートを開始
    startHeartbeat(webSocketClient);

    // 既存のタブ情報を登録
    registerCurrentTabs(webSocketClient);
});

webSocketClient.onMessage((message) => {
    switch (message.action) {
        case 'pong':
            console.log('pong received.');
            break;
        default:
            console.log('Unknown message action:', message.action);
            break;
    }
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    switch (message.action) {
        case 'getTabInfo':
            // content scriptに現在のタブ情報を返す
            if (sender.tab) {
                sendResponse({
                    tabId: sender.tab.id,
                    windowId: sender.tab.windowId,
                    url: sender.tab.url,
                    title: sender.tab.title,
                    favIconUrl: sender.tab.favIconUrl,
                    active: sender.tab.active,
                });
            } else {
                sendResponse(null);
            }
            break;
        default:
            break;
    }
    return true;
});

webSocketClient.onDisconnected(() => {
    // ハートビートを停止
    stopHeartbeat();
});

// 初期化
webSocketClient.initialize();

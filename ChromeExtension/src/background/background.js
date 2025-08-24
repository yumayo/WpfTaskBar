// メイン背景スクリプト - モジュール化されたファイルをまとめる

import { initializeWebSocket, getConnectionStatus } from './websocket-client.js';
import { setupTabEventListeners, sendTestNotification } from './tab-manager.js';
import { setupPopupMessageListener } from './popup-handler.js';

// 拡張機能の初期化
chrome.runtime.onStartup.addListener(() => {
    initializeWebSocket();
});

chrome.runtime.onInstalled.addListener(() => {
    initializeWebSocket();
});

// ポップアップとの通信設定
setupPopupMessageListener(getConnectionStatus, sendTestNotification);

// タブイベントリスナー設定
setupTabEventListeners();

// 初期化
initializeWebSocket();
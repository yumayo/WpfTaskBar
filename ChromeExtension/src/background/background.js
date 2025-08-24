// メイン背景スクリプト - モジュール化されたファイルをまとめる

import { initializeWebSocket, getConnectionStatus, onConnected, onMessage, onDisconnected } from '../utils/websocket-client.js';
import { setupTabEventListeners, sendTestNotification } from '../utils/tab-manager.js';
import { setupPopupMessageListener } from '../popup/popup-handler.js';
import { handleMessage } from './message-handlers.js';
import { startHeartbeat, stopHeartbeat } from './heartbeat.js';
import { registerCurrentTabs } from '../utils/tab-registration.js';

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

// WebSocketコールバックを登録
onConnected(() => {
    // ハートビートを開始
    startHeartbeat();
    
    // 既存のタブ情報を登録
    registerCurrentTabs();
});

onMessage((message) => {
    handleMessage(message);
});

onDisconnected(() => {
    // ハートビートを停止
    stopHeartbeat();
});

// 初期化
initializeWebSocket();
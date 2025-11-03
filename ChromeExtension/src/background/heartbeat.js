// ハートビート機能を処理するモジュール

import { webSocketClient } from './background.js';

let heartbeatTimer = null;
let heartbeatInterval = 10000; // 10秒ごとにping送信

// ハートビートを開始
export function startHeartbeat() {
    stopHeartbeat(); // 既存のタイマーをクリア

    heartbeatTimer = setInterval(() => {
        if (webSocketClient.getConnectionStatus()) {
            console.log('Sending heartbeat ping...');
            webSocketClient.sendMessage({ action: 'ping', data: {} });
        }
    }, heartbeatInterval);

    console.log(`Heartbeat started with ${heartbeatInterval}ms interval`);
}

// ハートビートを停止
export function stopHeartbeat() {
    if (heartbeatTimer) {
        clearInterval(heartbeatTimer);
        heartbeatTimer = null;
        console.log('Heartbeat stopped');
    }
}
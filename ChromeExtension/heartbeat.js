// ハートビート機能を処理するモジュール

import { sendMessage, getConnectionStatus } from './websocket-client.js';

let heartbeatTimer = null;
let heartbeatInterval = 30000; // 30秒ごとにping送信

// ハートビートを開始
export function startHeartbeat() {
    stopHeartbeat(); // 既存のタイマーをクリア
    
    heartbeatTimer = setInterval(() => {
        if (getConnectionStatus()) {
            console.log('Sending heartbeat ping...');
            sendMessage({ action: 'ping', data: {} });
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
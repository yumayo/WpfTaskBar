import type { WebSocketClient } from './websocket-client';

let heartbeatTimer: number | null = null;
const heartbeatInterval = 10000; // 10秒ごとにping送信

// ハートビートを開始
export function heartbeatStart(webSocketClient: WebSocketClient): void {
    heartbeatStop(); // 既存のタイマーをクリア

    heartbeatTimer = setInterval(() => {
        if (webSocketClient.getConnectionStatus()) {
            console.log('Sending heartbeat ping...');
            webSocketClient.sendMessage({ action: 'ping', data: {} }, false);
        }
    }, heartbeatInterval) as unknown as number;

    console.log(`Heartbeat started with ${heartbeatInterval}ms interval`);
}

// ハートビートを停止
export function heartbeatStop(): void {
    if (heartbeatTimer) {
        clearInterval(heartbeatTimer);
        heartbeatTimer = null;
        console.log('Heartbeat stopped');
    }
}

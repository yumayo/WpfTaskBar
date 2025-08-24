// WebSocket接続とメッセージング機能を処理するモジュール

import { handleMessage } from './message-handlers.js';
import { startHeartbeat, stopHeartbeat } from './heartbeat.js';
import { registerCurrentTabs } from './tab-registration.js';

let ws = null;
let isConnected = false;
let reconnectTimer = null;

// WebSocket接続を初期化
export function initializeWebSocket() {
    // 既存のWebSocketがあり、接続中または接続中の場合は何もしない
    if (ws && (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN)) {
        return;
    }
    
    try {
        ws = new WebSocket('ws://127.0.0.1:5000/ws');
        
        ws.onopen = () => {
            console.log('WebSocket connected to WpfTaskBar');
            isConnected = true;
            
            // 接続成功時に再接続タイマーをクリア
            if (reconnectTimer) {
                clearTimeout(reconnectTimer);
                reconnectTimer = null;
            }
            
            // ハートビートを開始
            startHeartbeat();
            
            // 既存のタブ情報を登録
            registerCurrentTabs();
        };
        
        ws.onmessage = (event) => {
            console.log('WebSocket message received:', event.data);
            try {
                const message = JSON.parse(event.data);
                handleMessage(message);
            } catch (error) {
                console.error('Failed to parse WebSocket message:', error);
            }
        };
        
        ws.onclose = () => {
            console.log('WebSocket connection closed');
            isConnected = false;
            
            // ハートビートを停止
            stopHeartbeat();
            
            // 5秒後に再接続を試行
            reconnectTimer = setTimeout(() => {
                console.log('Attempting to reconnect...');
                initializeWebSocket();
            }, 5000);
        };
        
        ws.onerror = (error) => {
            console.error('WebSocket error:', error);
            isConnected = false;
        };
        
    } catch (error) {
        console.error('Failed to initialize WebSocket:', error);
        // 5秒後に再試行
        reconnectTimer = setTimeout(initializeWebSocket, 5000);
    }
}



// WebSocketメッセージを送信
export function sendMessage(message) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        const jsonMessage = JSON.stringify(message);
        console.log('Sending WebSocket message:', jsonMessage);
        ws.send(jsonMessage);
    } else {
        console.warn('WebSocket is not connected, message not sent:', message);
    }
}

// 接続状態を取得
export function getConnectionStatus() {
    return isConnected;
}



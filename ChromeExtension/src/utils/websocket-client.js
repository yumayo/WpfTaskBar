// WebSocket接続とメッセージング機能を処理するモジュール

let ws = null;
let isConnected = false;
let reconnectTimer = null;

// コールバック関数を格納
let onConnectedCallback = null;
let onMessageCallback = null;
let onDisconnectedCallback = null;
let onErrorCallback = null;

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
            
            // 接続コールバックを実行
            if (onConnectedCallback) {
                onConnectedCallback();
            }
        };
        
        ws.onmessage = (event) => {
            try {
                const message = JSON.parse(event.data);
                // メッセージコールバックを実行
                if (onMessageCallback) {
                    onMessageCallback(message);
                }
            } catch (error) {
                console.error('Failed to parse WebSocket message:', error);
            }
        };
        
        ws.onclose = () => {
            console.log('WebSocket connection closed');
            isConnected = false;
            
            // 切断コールバックを実行
            if (onDisconnectedCallback) {
                onDisconnectedCallback();
            }
            
            // 5秒後に再接続を試行
            reconnectTimer = setTimeout(() => {
                console.log('Attempting to reconnect...');
                initializeWebSocket();
            }, 5000);
        };
        
        ws.onerror = (error) => {
            console.error('WebSocket error:', error);
            isConnected = false;
            
            // エラーコールバックを実行
            if (onErrorCallback) {
                onErrorCallback(error);
            }
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
        ws.send(jsonMessage);
    } else {
        console.warn('WebSocket is not connected, message not sent:', message);
    }
}

// 接続状態を取得
export function getConnectionStatus() {
    return isConnected;
}

// コールバック登録関数
export function onConnected(callback) {
    onConnectedCallback = callback;
}

export function onMessage(callback) {
    onMessageCallback = callback;
}

export function onDisconnected(callback) {
    onDisconnectedCallback = callback;
}

export function onError(callback) {
    onErrorCallback = callback;
}



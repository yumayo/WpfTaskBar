// WebSocket接続とメッセージング機能を処理するモジュール

let ws = null;
let isConnected = false;
let reconnectTimer = null;
let heartbeatTimer = null;
let heartbeatInterval = 30000; // 30秒ごとにping送信

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

// メッセージ処理
function handleMessage(message) {
    switch (message.action) {
        case 'focusTab':
            handleFocusTab(message.data);
            break;
        case 'ping':
            sendMessage({ action: 'pong', data: {} });
            break;
        default:
            console.log('Unknown message action:', message.action);
            break;
    }
}

// タブフォーカス処理
function handleFocusTab(data) {
    console.log('Focus tab request:', data);
    
    chrome.tabs.update(data.tabId, { active: true }, (tab) => {
        if (chrome.runtime.lastError) {
            console.error('Failed to focus tab:', chrome.runtime.lastError);
            return;
        }
        
        // ウィンドウも最前面に表示
        chrome.windows.update(data.windowId, { focused: true }, (window) => {
            if (chrome.runtime.lastError) {
                console.error('Failed to focus window:', chrome.runtime.lastError);
            } else {
                console.log('Successfully focused tab and window');
            }
        });
    });
}

// 現在のタブ情報を登録
function registerCurrentTabs() {
    chrome.tabs.query({}, (tabs) => {
        tabs.forEach(tab => {
            if (tab.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('chrome-extension://')) {
                registerTab(tab);
            }
        });
    });
}

// タブ情報を登録
function registerTab(tab) {
    if (!isConnected) return;
    
    const tabInfo = {
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        title: tab.title || 'Untitled'
    };
    
    sendMessage({
        action: 'registerTab',
        data: tabInfo
    });
    
    console.log('Tab registered:', tabInfo);
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

// ハートビート機能
function startHeartbeat() {
    stopHeartbeat(); // 既存のタイマーをクリア
    
    heartbeatTimer = setInterval(() => {
        if (isConnected) {
            console.log('Sending heartbeat ping...');
            sendMessage({ action: 'ping', data: {} });
        }
    }, heartbeatInterval);
    
    console.log(`Heartbeat started with ${heartbeatInterval}ms interval`);
}

function stopHeartbeat() {
    if (heartbeatTimer) {
        clearInterval(heartbeatTimer);
        heartbeatTimer = null;
        console.log('Heartbeat stopped');
    }
}

// 外部から呼び出せるように公開
export { registerTab };
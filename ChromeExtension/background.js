let ws = null;
let isConnected = false;
let reconnectTimer = null;
let heartbeatTimer = null;
let heartbeatInterval = 30000; // 30秒ごとにping送信

// WebSocket接続を初期化
function initializeWebSocket() {
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
function sendMessage(message) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        const jsonMessage = JSON.stringify(message);
        console.log('Sending WebSocket message:', jsonMessage);
        ws.send(jsonMessage);
    } else {
        console.warn('WebSocket is not connected, message not sent:', message);
    }
}

// 通知を送信（テスト用）
function sendTestNotification(tabId) {
    chrome.tabs.get(tabId, (tab) => {
        if (chrome.runtime.lastError) {
            console.error('Failed to get tab info:', chrome.runtime.lastError);
            return;
        }
        
        const notification = {
            title: 'テスト通知',
            message: `${tab.title} からの通知です`,
            tabId: tab.id,
            windowId: tab.windowId,
            url: tab.url,
            tabTitle: tab.title,
            timestamp: new Date().toISOString()
        };
        
        sendMessage({
            action: 'sendNotification',
            data: notification
        });
        
        console.log('Test notification sent:', notification);
    });
}

// タブ用のテスト通知を送信
function sendTestNotificationForTab(tab) {
    if (!isConnected) {
        console.log('WebSocket not connected, skipping tab notification');
        return;
    }

    const notification = {
        title: '新しいタブが開かれました',
        message: `${tab.title || tab.url || 'New Tab'} が開かれました`,
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        tabTitle: tab.title || 'New Tab',
        timestamp: new Date().toISOString()
    };

    sendMessage({
        action: 'sendNotification',
        data: notification
    });

    console.log('Tab opened notification sent:', notification);
}

// タブイベントリスナー
chrome.tabs.onCreated.addListener((tab) => {
    if (tab.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('chrome-extension://')) {
        registerTab(tab);
        
        // テスト用: 新しいタブが開かれた時に通知を送信
        setTimeout(() => {
            sendTestNotificationForTab(tab);
        }, 1000); // 1秒後に送信（タブの読み込みを待つ）
    }
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
    if (changeInfo.url || changeInfo.title) {
        if (tab.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('chrome-extension://')) {
            registerTab(tab);
        }
    }
    
    // ページの読み込みが完了した時にテスト通知を送信
    if (changeInfo.status === 'complete' && tab.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('chrome-extension://')) {
        setTimeout(() => {
            sendPageLoadedNotification(tab);
        }, 500);
    }
});

// ページ読み込み完了通知
function sendPageLoadedNotification(tab) {
    if (!isConnected) {
        console.log('WebSocket not connected, skipping page loaded notification');
        return;
    }

    const notification = {
        title: 'ページが読み込まれました',
        message: `${tab.title || tab.url} の読み込みが完了しました`,
        tabId: tab.id,
        windowId: tab.windowId,
        url: tab.url,
        tabTitle: tab.title || 'Untitled',
        timestamp: new Date().toISOString()
    };

    sendMessage({
        action: 'sendNotification',
        data: notification
    });

    console.log('Page loaded notification sent:', notification);
}

chrome.tabs.onRemoved.addListener((tabId) => {
    console.log('Tab removed:', tabId);
    // タブ削除の通知は必要に応じて実装
});

// 拡張機能の初期化
chrome.runtime.onStartup.addListener(() => {
    initializeWebSocket();
});

chrome.runtime.onInstalled.addListener(() => {
    initializeWebSocket();
});

// メッセージリスナー（ポップアップからのメッセージを受信）
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    switch (message.action) {
        case 'sendTestNotification':
            chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
                if (tabs[0]) {
                    sendTestNotification(tabs[0].id);
                    sendResponse({ success: true });
                }
            });
            return true; // 非同期レスポンス
            
        case 'getConnectionStatus':
            sendResponse({ connected: isConnected });
            break;
            
        default:
            sendResponse({ error: 'Unknown action' });
            break;
    }
});

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

// Chrome拡張機能のService Worker維持
function keepAlive() {
    console.log('Keep-alive ping');
    // Service Workerを維持するための処理
    chrome.runtime.getPlatformInfo(() => {
        // 何もしないが、このAPIコールによってService Workerが維持される
    });
}

// Service Workerを定期的に維持（5分ごと）
setInterval(keepAlive, 5 * 60 * 1000);

// 初期化
initializeWebSocket();
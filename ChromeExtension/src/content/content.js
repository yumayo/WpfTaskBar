(async () => {
    // 動的インポートを使用してWebSocketClientを読み込む
    const { WebSocketClient } = await import(chrome.runtime.getURL('src/utils/websocket-client.js'));

    // スリープ関数
    const sleep = (ms) => new Promise(resolve => setTimeout(resolve, ms));

    // WebSocketクライアントを作成
    const wsClient = new WebSocketClient();

    // ページアンロード時のクリーンアップ
    window.addEventListener('beforeunload', () => {
        if (wsClient) {
            console.log('[Content] Closing WebSocket');
            wsClient.close();
        }
    });

    // WebSocketコールバックを登録
    wsClient.onConnected(() => {
        console.log('[Content] WebSocket connected');
    });

    wsClient.onMessage((message) => {
        console.log('[Content] WebSocket message received:', message);
    });

    wsClient.onDisconnected(() => {
        console.log('[Content] WebSocket disconnected');
    });

    wsClient.onError((error) => {
        console.error('[Content] WebSocket error:', error);
    });

    // WebSocket接続を開始
    wsClient.initialize();

    // WebSocket接続を待機
    let i = 0;
    for (; i < 30; ++i) {
        if (wsClient.getConnectionStatus()) {
            // 接続成功
            break;
        }
        console.log(`[Content] Waiting for WebSocket connection... (attempt ${i + 1}/30)`);
        await sleep(100);
    }

    if (i >= 30) {
        console.error('[Content] Failed to establish WebSocket connection after 30 attempts');
        return;
    }

    // ウィンドウIDを取得
    async function getTabInfo() {
        return new Promise((resolve) => {
            chrome.runtime.sendMessage({ action: 'getTabInfo' }, (response) => {
                if (response) {
                    resolve(response);
                } else {
                    resolve(null);
                }
            });
        });
    }

    const tabInfo = await getTabInfo();

    if (tabInfo) {
        // WindowHandleをバインドする
        const message = {
            action: 'bindWindowHandle',
            data: {
                tabId: tabInfo.tabId,
                windowId: tabInfo.windowId
            }
        };

        wsClient.sendMessage(message);
        console.log('Sent bindWindowHandle message:', message);
        console.log('[Content] Bound window handle for tab:', tabInfo);
    } else {
        console.warn('[Content] Failed to get tab info for binding window handle');
    }

    // TODO: bindWindowHandleのレスポンスを受け取ってから閉じる
    await sleep(5000);

    console.log('[Content] Closing WebSocket');
    wsClient.close();
})();

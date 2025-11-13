(async () => {
    interface TabInfo {
        tabId: number;
        windowId: number;
        url?: string;
        title?: string;
        favIconUrl?: string;
        active?: boolean;
    }

    // ウィンドウIDを取得
    async function getTabInfo(): Promise<TabInfo> {
        return new Promise((resolve, reject) => {
            chrome.runtime.sendMessage({ action: 'getTabInfo' }, (response: TabInfo | null) => {
                if (response) {
                    if (response.tabId && response.windowId) {
                        resolve(response);
                        return;
                    }
                }
                reject(new Error('[Content] Fail getTabInfo'));
            });
        });
    }

    async function connectWebSocket(url: string, timeout: number = 5000): Promise<WebSocket> {
        return new Promise((resolve, reject) => {
            console.log('[Content] Connecting WebSocket');
            const ws = new WebSocket(url);

            const timer = setTimeout(() => {
                ws.close();
                reject(new Error('[Content] Timeout WebSocket'));
            }, timeout);

            ws.onopen = () => {
                clearTimeout(timer);
                console.log('[Content] Connected WebSocket');
                resolve(ws);
            };

            ws.onerror = (error) => {
                clearTimeout(timer);
                reject(error);
            };
        });
    }

    async function closeWebSocket(ws: WebSocket, timeout: number = 5000): Promise<WebSocket> {
        return new Promise((resolve, reject) => {
            if (ws.readyState !== WebSocket.CLOSING && ws.readyState !== WebSocket.CLOSED) {
                console.log('[Content] Closing WebSocket');
                ws.close();

                const timer = setTimeout(() => {
                    ws.close();
                    reject(new Error('[Content] Timeout WebSocket'));
                }, timeout);

                ws.onclose = () => {
                    clearTimeout(timer);
                    console.log('[Content] Closed WebSocket');
                    resolve(ws);
                };

                ws.onerror = (error) => {
                    clearTimeout(timer);
                    reject(error);
                };
            } else {
                console.log('[Content] Already Closed WebSocket');
                resolve(ws);
            }
        });
    }

    const ws = await connectWebSocket('ws://127.0.0.1:5000/ws');

    // ページアンロード時のクリーンアップ
    window.addEventListener('beforeunload', () => {
        if (ws.readyState !== WebSocket.CLOSING && ws.readyState !== WebSocket.CLOSED) {
            console.log('[Content] Closing WebSocket');
            ws.close();
        }
    });

    const tabInfo = await getTabInfo();

    // WindowHandleをバインドする
    const message = {
        action: 'bindWindowHandle',
        data: tabInfo
    };
    const jsonMessage = JSON.stringify(message);
    ws.send(jsonMessage);
    console.log('[Content] Request bindWindowHandle tabInfo:', tabInfo);

    await closeWebSocket(ws);
})();

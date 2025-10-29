(async () => {

    // ウィンドウIDを取得
    async function getTabInfo() {
        return new Promise((resolve, reject) => {
            chrome.runtime.sendMessage({ action: 'getTabInfo' }, (response) => {
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

    // ConnectionIDを取得
    async function getConnectionId() {
        return new Promise((resolve, reject) => {
            chrome.runtime.sendMessage({ action: 'getConnectionId' }, (response) => {
                if (response && response.connectionId) {
                    console.log('[TCP調査] [Content] ConnectionId received from background:', response.connectionId);
                    resolve(response.connectionId);
                } else {
                    console.warn('[Content] ConnectionId not available');
                    resolve(null);
                }
            });
        });
    }

    // GUIDを生成
    function generateGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
            const r = Math.random() * 16 | 0;
            const v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    // HTTP/2でメッセージを送信
    async function sendMessage(url, message, timeout = 5000) {
        try {
            // background.jsからconnectionIdを取得
            // const connectionId = await getConnectionId();

            // content.js独自のGUIDを生成
            const connectionId = generateGuid();

            console.log('[TCP調査] [Content] Sending HTTP/2 message with ConnectionId:', connectionId, 'Message:', message);

            const controller = new AbortController();
            const timer = setTimeout(() => {
                controller.abort();
            }, timeout);

            const headers = {
                'Content-Type': 'application/json',
            };

            // connectionIdがあればヘッダーに追加
            if (connectionId) {
                headers['X-Connection-Id'] = connectionId;
            }

            const response = await fetch(url, {
                method: 'POST',
                headers: headers,
                body: JSON.stringify(message),
                signal: controller.signal
            });

            clearTimeout(timer);

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            console.log('[Content] HTTP/2 message sent successfully');
            return response;
        } catch (error) {
            if (error.name === 'AbortError') {
                throw new Error('[Content] Timeout HTTP/2 request');
            }
            throw error;
        }
    }

    const tabInfo = await getTabInfo();

    // WindowHandleをバインドする
    const message = {
        action: 'bindWindowHandle',
        data: {
            tabId: tabInfo.tabId,
            windowId: tabInfo.windowId
        }
    };

    await sendMessage('http://127.0.0.1:5000/message', message);
    console.log(`[Content] Request bindWindowHandle windowId:${tabInfo.windowId} tabInfo:${tabInfo.tabId}`);
})();

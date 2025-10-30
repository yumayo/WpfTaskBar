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

    // HTTP/2でメッセージを送信
    async function sendMessage(url, message, timeout = 5000) {
        try {
            console.log('[Content] Sending HTTP/2 Message:', message);

            const controller = new AbortController();
            const timer = setTimeout(() => {
                controller.abort();
            }, timeout);

            const headers = {
                'Content-Type': 'application/json',
            };

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


// C#との通信用の関数
function sendMessageToHost(type, data = null) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({
            type: type,
            data: data,
            timestamp: new Date().toISOString()
        });
    }
}

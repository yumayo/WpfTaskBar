// コンテンツスクリプト - 将来的な拡張用
// 現在は特に機能なし、WebページからJavaScript API経由での通知送信機能を実装する場合に使用

// 将来的にWebページ側で以下のようなAPIを提供する場合の例:
/*
// WebページからJavaScript APIで通知を送信する例
window.wpfTaskBarNotification = function(title, message) {
    window.postMessage({
        type: 'WPF_TASKBAR_NOTIFICATION',
        data: { title, message }
    }, '*');
};
*/

// メッセージリスナー（将来的な実装用）
window.addEventListener('message', (event) => {
    // セキュリティチェック: 同一オリジンからのメッセージのみ受信
    if (event.source !== window) return;
    
    if (event.data.type && event.data.type === 'WPF_TASKBAR_NOTIFICATION') {
        // Chrome拡張のバックグラウンドスクリプトに通知データを転送
        chrome.runtime.sendMessage({
            action: 'sendWebPageNotification',
            data: event.data.data
        });
    }
});

console.log('WpfTaskBar Chrome Extension content script loaded');
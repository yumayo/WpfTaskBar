// DOM要素を取得
const connectionStatus = document.getElementById('connectionStatus');
const statusText = document.getElementById('statusText');
const sendNotificationBtn = document.getElementById('sendNotificationBtn');
const messageArea = document.getElementById('messageArea');
const currentTabInfo = document.getElementById('currentTabInfo');

// 初期化
document.addEventListener('DOMContentLoaded', () => {
    updateConnectionStatus();
    updateCurrentTabInfo();
    
    // イベントリスナーを設定
    sendNotificationBtn.addEventListener('click', sendTestNotification);
    
    // 1秒ごとに接続確認を実行
    setInterval(updateConnectionStatus, 1000);
});

// 接続状況を更新
function updateConnectionStatus() {
    chrome.runtime.sendMessage({ action: 'getConnectionStatus' }, (response) => {
        if (chrome.runtime.lastError) {
            showConnectionStatus(false, 'バックグラウンドスクリプトエラー');
            return;
        }
        
        if (response && response.connected) {
            showConnectionStatus(true, 'WpfTaskBarに接続済み');
            sendNotificationBtn.disabled = false;
        } else {
            showConnectionStatus(false, '未接続 - WpfTaskBarが起動していない可能性があります');
            sendNotificationBtn.disabled = true;
        }
    });
}

// 接続状況を表示
function showConnectionStatus(connected, message) {
    if (connected) {
        connectionStatus.className = 'status connected';
        statusText.textContent = message;
    } else {
        connectionStatus.className = 'status disconnected';
        statusText.textContent = message;
    }
}

// 現在のタブ情報を更新
function updateCurrentTabInfo() {
    chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
        if (chrome.runtime.lastError) {
            currentTabInfo.textContent = 'エラー';
            return;
        }
        
        if (tabs && tabs[0]) {
            const tab = tabs[0];
            currentTabInfo.textContent = `${tab.title} (ID: ${tab.id})`;
        } else {
            currentTabInfo.textContent = 'タブが見つかりません';
        }
    });
}

// テスト通知を送信
function sendTestNotification() {
    sendNotificationBtn.disabled = true;
    sendNotificationBtn.textContent = '送信中...';
    
    chrome.runtime.sendMessage({ action: 'sendTestNotification' }, (response) => {
        sendNotificationBtn.disabled = false;
        sendNotificationBtn.textContent = 'テスト通知を送信';
        
        if (chrome.runtime.lastError) {
            showMessage('エラー: ' + chrome.runtime.lastError.message, 'error');
            return;
        }
        
        if (response && response.success) {
            showMessage('テスト通知が正常に送信されました！', 'success');
        } else {
            showMessage('通知の送信に失敗しました', 'error');
        }
    });
}

// メッセージを表示
function showMessage(text, type) {
    const message = document.createElement('div');
    message.className = `message ${type}`;
    message.textContent = text;
    
    // 既存のメッセージをクリア
    messageArea.innerHTML = '';
    messageArea.appendChild(message);
    
    // 3秒後にメッセージを自動削除
    setTimeout(() => {
        if (message.parentNode) {
            message.parentNode.removeChild(message);
        }
    }, 3000);
}


// DOM要素を取得
const connectionStatus = document.getElementById('connectionStatus')!;
const statusText = document.getElementById('statusText')!;
const sendNotificationBtn = document.getElementById('sendNotificationBtn') as HTMLButtonElement;
const currentTabInfo = document.getElementById('currentTabInfo')!;

// 初期化
updateConnectionStatus();
updateCurrentTabInfo();

// 1秒ごとに接続確認を実行
setInterval(updateConnectionStatus, 1000);

// 接続状況を更新
function updateConnectionStatus(): void {
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
function showConnectionStatus(connected: boolean, message: string): void {
    if (connected) {
        connectionStatus.className = 'status connected';
        statusText.textContent = message;
    } else {
        connectionStatus.className = 'status disconnected';
        statusText.textContent = message;
    }
}

// 現在のタブ情報を更新
function updateCurrentTabInfo(): void {
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

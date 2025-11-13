// メイン背景スクリプト - モジュール化されたファイルをまとめる

import { WebSocketClient } from './websocket-client';
import { tabManagerRegisterCurrentTabs, tabManagerSetupTabEventListeners } from './tab-manager';
import { setupPopupMessageListener } from '../popup/popup-handler';
import { heartbeatStart, heartbeatStop } from './heartbeat';

// WebSocketクライアントのインスタンスを作成
const webSocketClient = new WebSocketClient();

// ポップアップとの通信設定
setupPopupMessageListener(() => webSocketClient.getConnectionStatus());

// タブイベントリスナー設定
tabManagerSetupTabEventListeners(webSocketClient);

// WebSocketコールバックを登録
webSocketClient.onConnected(() => {
    // ハートビートを開始
    heartbeatStart(webSocketClient);

    // 既存のタブ情報を登録
    tabManagerRegisterCurrentTabs(webSocketClient);
});

// C#バックエンドとの通信 (ChromeExtension外の通信)
webSocketClient.onMessage((message) => {
    switch (message.action) {
        case 'pong':
            console.log('pong received.');
            break;
        case 'focusTab':
            handleFocusTab(message.data);
            break;
        default:
            console.log('Unknown message action:', message.action);
            break;
    }
});

// タブフォーカス処理
function handleFocusTab(data: { tabId: number }): void {
    console.log('Focus tab request:', data);

    chrome.tabs.update(data.tabId, { active: true }, (tab) => {
        if (chrome.runtime.lastError) {
            console.error('Failed to focus tab:', chrome.runtime.lastError);
        } else {
            console.log('Successfully focused tab:', tab);
        }
    });
}

// content.jsとの通信 (ChromeExtension内の通信)
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    switch (message.action) {
        case 'getTabInfo':
            // content scriptに現在のタブ情報を返す
            if (sender.tab) {
                sendResponse({
                    tabId: sender.tab.id,
                    windowId: sender.tab.windowId,
                    url: sender.tab.url,
                    title: sender.tab.title,
                    favIconUrl: sender.tab.favIconUrl,
                    active: sender.tab.active,
                });
            } else {
                sendResponse(null);
            }
            break;
        default:
            break;
    }
    return true;
});

webSocketClient.onDisconnected(() => {
    // ハートビートを停止
    heartbeatStop();
});

// 初期化
webSocketClient.initialize();

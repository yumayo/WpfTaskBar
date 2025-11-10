// タブイベントリスナーを設定
import {webSocketRequestRegisterTab} from "./websocket-controller.js";

export function tabManagerSetupTabEventListeners(webSocketClient) {
    chrome.tabs.onCreated.addListener((tab) => {
        tabManagerRegisterCurrentTabs(webSocketClient, tab);
    });

    chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
        console.log('【OnUpdated】 Tab updated:', tab, 'changeInfo:', changeInfo);

        if (changeInfo.url || changeInfo.title || changeInfo.favIconUrl) {
            console.log('【OnUpdated】Tab property changed (url/title/favicon), registering tab:', tab);
            webSocketRequestRegisterTab(webSocketClient, tab);
        }

        // ページの読み込みが完了した時にfaviconが確定するのでタブを再登録
        if (changeInfo.status === 'complete') {
            console.log('【OnUpdated】Tab loading complete, notifying tabs change and re-registering:', tab);
            webSocketRequestRegisterTab(webSocketClient, tab);
        }
    });

    // アクティブなタブが変更された時にタブ情報を再登録し、すぐに通知
    chrome.tabs.onActivated.addListener((activeInfo) => {
        chrome.tabs.get(activeInfo.tabId, (tab) => {
            if (chrome.runtime.lastError) {
                console.error('Failed to get tab:', chrome.runtime.lastError);
                return;
            }
            webSocketRequestRegisterTab(webSocketClient, tab);
        });
    });
}

// 現在のタブ情報を登録
export function tabManagerRegisterCurrentTabs(webSocketClient) {
    chrome.tabs.query({}, (tabs) => {
        tabs.forEach(tab => {
            webSocketRequestRegisterTab(webSocketClient, tab);
        });
    });
}

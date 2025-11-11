// タブイベントリスナーを設定
import {webSocketRequestUpdateTab, webSocketRequestRemoveTab} from "./websocket-controller.js";

export function tabManagerSetupTabEventListeners(webSocketClient) {
    chrome.tabs.onCreated.addListener((tab) => {
        console.log('【OnUpdated】Tab created, registering tab:', tab);
        webSocketRequestUpdateTab(webSocketClient, tab);
    });

    chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
        console.log('【OnUpdated】Tab updated:', tab, 'changeInfo:', changeInfo);
        webSocketRequestUpdateTab(webSocketClient, tab);
    });

    // アクティブなタブが変更された時にタブ情報を再登録し、すぐに通知
    chrome.tabs.onActivated.addListener((activeInfo) => {
        chrome.tabs.get(activeInfo.tabId, (tab) => {
            if (chrome.runtime.lastError) {
                console.error('Failed to get tab:', chrome.runtime.lastError);
                return;
            }
            webSocketRequestUpdateTab(webSocketClient, tab);
        });
    });

    // タブが移動された時にタブ情報を再登録
    chrome.tabs.onMoved.addListener((tabId, moveInfo) => {
        chrome.tabs.get(tabId, (tab) => {
            if (chrome.runtime.lastError) {
                console.error('Failed to get tab:', chrome.runtime.lastError);
                return;
            }
            console.log('【OnMoved】Tab moved:', tab, 'moveInfo:', moveInfo);
            webSocketRequestUpdateTab(webSocketClient, tab);
        });
    });

    // タブが削除された時に通知
    chrome.tabs.onRemoved.addListener((tabId, removeInfo) => {
        console.log('【OnRemoved】Tab removed:', tabId, 'removeInfo:', removeInfo);
        webSocketRequestRemoveTab(webSocketClient, tabId, removeInfo.windowId);
    });
}

// 現在のタブ情報を登録
export function tabManagerRegisterCurrentTabs(webSocketClient) {
    chrome.tabs.query({}, (tabs) => {
        tabs.forEach(tab => {
            console.log('【OnUpdated】Tab created, registering tab:', tab);
            webSocketRequestUpdateTab(webSocketClient, tab);
        });
    });
}

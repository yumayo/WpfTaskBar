// WindowManager for WebView2
// ウィンドウ管理のロジックをJavaScript側に移植

class WindowManager {
    constructor() {
        this.windowHandles = [];
        this.taskBarItems = [];
    }

    // 開始
    start() {
        console.log('WindowManager.start() called');
        this.updateTaskWindows();
        console.log('WindowManager started');
    }

    // ウィンドウリストの更新ループ
    async updateTaskWindows() {
        try {
            // C#側にウィンドウハンドル一覧の取得を要求
            const windowHandles = await this.requestWindowHandles();
            this.windowHandles = windowHandles || [];

            // タスクバーウィンドウの更新
            await this.updateTaskBarWindows();

            // 100ms後に再実行
            setTimeout(() => {
                this.updateTaskWindows();
            }, 100);
        } catch (error) {
            console.error('Error in WindowManager.updateTaskWindows():', error);
            // エラーが発生した場合も継続
            setTimeout(() => {
                this.updateTaskWindows();
            }, 100);
        }
    }

    // C#側にウィンドウハンドル一覧を要求
    async requestWindowHandles() {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error('requestWindowHandles timeout')), 1000);

            // レスポンス受信用の一時的なリスナー
            const responseHandler = (event) => {
                try {
                    let data;
                    if (typeof event.data === 'string') {
                        data = JSON.parse(event.data);
                    } else {
                        data = event.data;
                    }

                    if (data && data.type === 'window_handles_response') {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);
                        resolve(data.windowHandles);
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    reject(error);
                }
            };

            // イベントリスナー追加
            window.chrome.webview.addEventListener('message', responseHandler);

            sendMessageToHost('request_window_handles');
        });
    }

    // タスクバーウィンドウの更新処理
    async updateTaskBarWindows() {
        try {
            // フォアグラウンドウィンドウの取得
            const foregroundHwnd = await this.requestForegroundWindow();

            // ウィンドウハンドルに存在しないタスクバーを削除
            this.taskBarItems = this.taskBarItems.filter(item => {
                return this.windowHandles.some(handle =>
                    handle === item.handle
                );
            });

            // 各ウィンドウハンドルを処理
            for (const windowHandle of this.windowHandles) {
                // タスクバーウィンドウかどうかの判定をC#側に要求
                let isTaskBarWindow = await this.requestIsTaskBarWindow(windowHandle);

                // 現在の仮想デスクトップにあるウィンドウのみを対象とする
                if (isTaskBarWindow) {
                    const isOnCurrentVirtualDesktop = await this.requestIsWindowOnCurrentVirtualDesktop(windowHandle);
                    if (!isOnCurrentVirtualDesktop) {
                        isTaskBarWindow = false;
                    }
                }

                // 現在のタスクバーアイテムに含まれているか
                const existingTaskBarItem = this.taskBarItems.find(item => item.handle === windowHandle);

                if (existingTaskBarItem) {
                    // 既存のアイテム
                    if (!isTaskBarWindow) {
                        // タスクバー管理外になったため削除
                        // または異なる仮想デスクトップに移動されたため削除
                        this.taskBarItems = this.taskBarItems.filter(item => item.handle !== windowHandle);
                    } else {
                        // 既存アイテムの更新（IsForegroundプロパティを更新）
                        const updatedItemOrItems = await this.createTaskBarItem(windowHandle, foregroundHwnd);
                        // createTaskBarItemがChromeタブの配列を返す場合がある
                        if (Array.isArray(updatedItemOrItems)) {
                            // 新しいタブリストに存在しないタブを削除
                            this.taskBarItems = this.taskBarItems.filter(item => {
                                // 異なるwindowHandleのアイテムは残す
                                if (item.handle !== windowHandle) {
                                    return true;
                                }
                                // 同じwindowHandleのChromeタブの場合、updatedItemOrItemsに含まれるかチェック
                                return updatedItemOrItems.some(updatedItem =>
                                    updatedItem.tabId === item.tabId &&
                                    updatedItem.windowId === item.windowId
                                );
                            });
                            // 各タブを更新または追加
                            for (let updatedItem of updatedItemOrItems) {
                                const index = this.taskBarItems.findIndex(item =>
                                    item.handle === updatedItem.handle &&
                                    item.tabId === updatedItem.tabId &&
                                    item.windowId === updatedItem.windowId
                                );
                                if (index >= 0) {
                                    this.taskBarItems[index] = updatedItem;
                                } else {
                                    this.taskBarItems.push(updatedItem);
                                }
                            }
                        } else {
                            const index = this.taskBarItems.findIndex(item => item.handle === updatedItemOrItems.handle);
                            this.taskBarItems[index] = updatedItemOrItems;
                        }
                    }
                } else {
                    // 新しいアイテム
                    if (isTaskBarWindow) {
                        const newItemOrItems = await this.createTaskBarItem(windowHandle, foregroundHwnd);
                        // createTaskBarItemがChromeタブの配列を返す場合がある
                        if (Array.isArray(newItemOrItems)) {
                            // 各タブを追加
                            for (let newItem of newItemOrItems) {
                                const index = this.taskBarItems.findIndex(item =>
                                    item.handle === newItem.handle &&
                                    item.tabId === newItem.tabId &&
                                    item.windowId === newItem.windowId
                                );
                                if (index >= 0) {
                                    this.taskBarItems[index] = newItem;
                                } else {
                                    this.taskBarItems.push(newItem);
                                }
                            }
                        } else {
                            this.taskBarItems.push(newItemOrItems);
                        }
                    }
                }
            }

            // タスクバー一覧を更新する
            updateTaskList(this.taskBarItems);

        } catch (error) {
            console.error('Error in updateTaskBarWindows:', error);
        }
    }

    // フォアグラウンドウィンドウの取得
    async requestForegroundWindow() {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error('requestForegroundWindow timeout')), 1000);

            const responseHandler = (event) => {
                try {
                    let data;
                    if (typeof event.data === 'string') {
                        data = JSON.parse(event.data);
                    } else {
                        data = event.data;
                    }

                    if (data && data.type === 'foreground_window_response') {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);
                        resolve(data.foregroundWindow);
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    reject(error);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            sendMessageToHost('request_foreground_window');
        });
    }

    // タスクバーウィンドウかどうかの判定
    async requestIsTaskBarWindow(windowHandle) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error('requestIsTaskBarWindow timeout')), 1000);

            const responseHandler = (event) => {
                try {
                    let data;
                    if (typeof event.data === 'string') {
                        data = JSON.parse(event.data);
                    } else {
                        data = event.data;
                    }

                    if (data && data.type === 'is_taskbar_window_response' && data.windowHandle === windowHandle) {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);
                        resolve(data.isTaskBarWindow);
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    reject(error);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            sendMessageToHost('request_is_taskbar_window', { windowHandle: windowHandle });
        });
    }

    // タスクバーアイテムの作成
    async createTaskBarItem(windowHandle, foregroundHwnd) {
        try {
            const windowInfo = await this.requestWindowInfo(windowHandle);

            // Chromeウィンドウで、タブ情報がある場合は各タブをタスクバーアイテムにする
            if (windowInfo?.chromeTabs?.length > 0) {
                // 各タブを個別のタスクバーアイテムとして返す
                return windowInfo.chromeTabs.map(tab => ({
                    handle: windowHandle,
                    moduleFileName: windowInfo.moduleFileName,
                    title: tab.title || 'Untitled',
                    // Chromeウィンドウがフォアグラウンドで、かつタブがアクティブな場合のみハイライト
                    isForeground: windowHandle === foregroundHwnd && tab.isActive,
                    iconData: tab.iconData || null,
                    // Chromeタブ専用のプロパティ
                    isChrome: true,
                    tabId: tab.tabId,
                    windowId: tab.windowId,
                    url: tab.url
                }));
            } else {
                // 通常のウィンドウの場合
                return {
                    handle: windowHandle,
                    moduleFileName: windowInfo?.moduleFileName || '',
                    title: windowInfo?.title || '',
                    isForeground: windowHandle === foregroundHwnd,
                    iconData: windowInfo?.iconData || null,
                    isChrome: false
                };
            }
        } catch (error) {
            console.error('Error creating TaskBarItem:', error);
            return {
                handle: windowHandle,
                moduleFileName: '',
                title: '',
                isForeground: false,
                iconData: null,
                isChrome: false
            };
        }
    }

    // ウィンドウ情報の取得
    async requestWindowInfo(windowHandle) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error('requestWindowInfo timeout')), 1000);

            const responseHandler = (event) => {
                try {
                    let data;
                    if (typeof event.data === 'string') {
                        data = JSON.parse(event.data);
                    } else {
                        data = event.data;
                    }

                    if (data && data.type === 'window_info_response' &&
                        data.windowHandle === windowHandle) {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);
                        resolve(data);
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    reject(error);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            sendMessageToHost('request_window_info', { windowHandle: windowHandle });
        });
    }

    // 仮想デスクトップ判定の要求
    async requestIsWindowOnCurrentVirtualDesktop(windowHandle) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error('requestIsWindowOnCurrentVirtualDesktop timeout')), 1000);

            const responseHandler = (event) => {
                try {
                    let data;
                    if (typeof event.data === 'string') {
                        data = JSON.parse(event.data);
                    } else {
                        data = event.data;
                    }

                    if (data && data.type === 'is_window_on_current_virtual_desktop_response' &&
                        data.windowHandle === windowHandle) {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);
                        resolve(data.isOnCurrentVirtualDesktop);
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    reject(error);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            sendMessageToHost('request_is_window_on_current_virtual_desktop', { windowHandle: windowHandle });
        });
    }
}

const windowManager = new WindowManager();
windowManager.start();

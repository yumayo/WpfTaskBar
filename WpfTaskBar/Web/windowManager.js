// WindowManager for WebView2
// ウィンドウ管理のロジックをJavaScript側に移植

class WindowManager {
    constructor() {
        this.windowHandles = [];
        this.taskBarItems = [];
        this.isRunning = false;
        this.updateInterval = null;
        this.listeners = new Map();

        // イベントリスナー
        this.windowListChangedListeners = [];
    }

    // イベントリスナーの追加
    addEventListener(eventType, listener) {
        if (eventType === 'windowListChanged') {
            this.windowListChangedListeners.push(listener);
        }
    }

    // イベントの発火
    fireWindowListChanged(eventArgs) {
        this.windowListChangedListeners.forEach(listener => {
            try {
                listener(this, eventArgs);
            } catch (error) {
                console.error('WindowListChanged listener error:', error);
            }
        });
    }

    // 開始
    start() {
        console.log('WindowManager.start() called');
        this.isRunning = true;
        this.updateTaskWindows();
        console.log('WindowManager started');
    }

    // 停止
    stop() {
        console.log('WindowManager.stop() called');
        this.isRunning = false;
        if (this.updateInterval) {
            clearTimeout(this.updateInterval);
            this.updateInterval = null;
        }
        console.log('WindowManager stopped');
    }

    // ウィンドウリストの更新ループ
    async updateTaskWindows() {
        if (!this.isRunning) {
            return;
        }

        try {
            // C#側にウィンドウハンドル一覧の取得を要求
            const windowHandles = await this.requestWindowHandles();
            this.windowHandles = windowHandles || [];

            // タスクバーウィンドウの更新
            this.updateTaskBarWindows();

            // 100ms後に再実行
            this.updateInterval = setTimeout(() => {
                this.updateTaskWindows();
            }, 100);
        } catch (error) {
            console.error('Error in WindowManager.updateTaskWindows():', error);
            // エラーが発生した場合も継続
            this.updateInterval = setTimeout(() => {
                this.updateTaskWindows();
            }, 100);
        }
    }

    // C#側にウィンドウハンドル一覧を要求
    async requestWindowHandles() {
        return new Promise((resolve, reject) => {
            // タイムアウト設定
            const timeout = setTimeout(() => {
                reject(new Error('Window handles request timeout'));
            }, 5000);

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

            // C#にリクエスト送信
            this.sendMessageToHost('request_window_handles');
        });
    }

    // タスクバーウィンドウの更新処理
    async updateTaskBarWindows() {
        try {
            const updateTaskBarItems = [];
            const addedTaskBarItems = [];
            const removedTaskBarItems = [];

            // フォアグラウンドウィンドウの取得
            const foregroundHwnd = await this.requestForegroundWindow();

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
                        removedTaskBarItems.push(existingTaskBarItem);
                    } else {
                        // 既存アイテムの更新（IsForegroundプロパティを更新）
                        const updatedItem = await this.createTaskBarItem(windowHandle, foregroundHwnd);
                        const index = this.taskBarItems.findIndex(item => item.handle === windowHandle);
                        this.taskBarItems[index] = updatedItem;
                    }
                } else {
                    // 新しいアイテム
                    if (isTaskBarWindow) {
                        const newTaskBarItem = await this.createTaskBarItem(windowHandle, foregroundHwnd);
                        this.taskBarItems.push(newTaskBarItem);
                        addedTaskBarItems.push(newTaskBarItem);
                    }
                }
            }

            // 存在しなくなったウィンドウハンドルの削除
            const removedByHandle = this.taskBarItems.filter(item =>
                !this.windowHandles.includes(item.handle)
            );

            for (const removedItem of removedByHandle) {
                this.taskBarItems = this.taskBarItems.filter(item => item.handle !== removedItem.handle);
                removedTaskBarItems.push(removedItem);
            }

            // デバッグログ出力
            // for (const taskBarItem of addedTaskBarItems) {
            //     console.log(`追加(${taskBarItem.handle.toString().padStart(10)}) ${taskBarItem.title}`);
            // }

            // for (const taskBarItem of removedTaskBarItems) {
            //     console.log(`削除(${taskBarItem.handle.toString().padStart(10)}) ${taskBarItem.title}`);
            // }

            // 全てのアイテムを更新（プロセス名などの最新情報を取得）
            for (const taskBarWindow of this.taskBarItems) {
                const updatedItem = await this.createTaskBarItem(taskBarWindow.handle, foregroundHwnd);
                // console.log(`更新アイテム: ${updatedItem.title}, iconData: ${!!updatedItem.iconData ? '有り' : '無し'}`);
                updateTaskBarItems.push(updatedItem);
            }

            // アプリケーション順序サービスによるソート
            const sortedUpdateTaskBarItems = await this.sortItemsByOrder(updateTaskBarItems);
            const sortedAddedTaskBarItems = await this.sortItemsByOrder(addedTaskBarItems);

            // イベント発火
            if (addedTaskBarItems.length > 0 || removedTaskBarItems.length > 0 || updateTaskBarItems.length > 0) {
                const eventArgs = {
                    updateTaskBarItems: sortedUpdateTaskBarItems,
                    addedTaskBarItems: sortedAddedTaskBarItems,
                    removedTaskBarItems: removedTaskBarItems
                };

                this.fireWindowListChanged(eventArgs);
            }

        } catch (error) {
            console.error('Error in updateTaskBarWindows:', error);
        }
    }

    // フォアグラウンドウィンドウの取得
    async requestForegroundWindow() {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                resolve(null); // タイムアウト時はnullを返す
            }, 1000);

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
                    resolve(null);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            this.sendMessageToHost('request_foreground_window');
        });
    }

    // タスクバーウィンドウかどうかの判定
    async requestIsTaskBarWindow(windowHandle) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                resolve(false); // タイムアウト時はfalseを返す
            }, 1000);

            const responseHandler = (event) => {
                try {
                    let data;
                    if (typeof event.data === 'string') {
                        data = JSON.parse(event.data);
                    } else {
                        data = event.data;
                    }

                    if (data && data.type === 'is_taskbar_window_response' &&
                        data.windowHandle === windowHandle) {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);
                        resolve(data.isTaskBarWindow);
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    resolve(false);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            this.sendMessageToHost('request_is_taskbar_window', { windowHandle: windowHandle });
        });
    }

    // タスクバーアイテムの作成
    async createTaskBarItem(windowHandle, foregroundHwnd) {
        try {
            // ウィンドウ情報をC#側から取得
            const windowInfo = await this.requestWindowInfo(windowHandle);
            // console.log(`createTaskBarItem: ${windowInfo?.title}, iconData length: ${windowInfo?.iconData?.length || 0}`);

            return {
                handle: windowHandle,
                moduleFileName: windowInfo?.moduleFileName || '',
                title: windowInfo?.title || '',
                isForeground: windowHandle === foregroundHwnd,
                iconData: windowInfo?.iconData || null
            };
        } catch (error) {
            console.error('Error creating TaskBarItem:', error);
            return {
                handle: windowHandle,
                moduleFileName: '',
                title: '',
                isForeground: false,
                iconData: null
            };
        }
    }

    // ウィンドウ情報の取得
    async requestWindowInfo(windowHandle) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                resolve({
                    moduleFileName: '',
                    title: '',
                    iconData: null
                });
            }, 1000);

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
                        resolve({
                            moduleFileName: data.moduleFileName,
                            title: data.title,
                            iconData: data.iconData
                        });
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    resolve({
                        moduleFileName: '',
                        title: '',
                        iconData: null
                    });
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            this.sendMessageToHost('request_window_info', { windowHandle: windowHandle });
        });
    }

    // C#との通信用メソッド
    sendMessageToHost(type, data = null) {
        if (window.chrome && window.chrome.webview) {
            const message = {
                type: type,
                data: data,
                timestamp: new Date().toISOString()
            };
            window.chrome.webview.postMessage(message);
            // console.log(`→ C#に送信: ${type}`, data);
        } else {
            console.error('❌ WebView2 通信環境が利用できません');
        }
    }

    // ウィンドウ数をプロセス別に取得
    async countBySameProcess(hwnd) {
        try {
            // 同じプロセスIDを持つタスクバーアイテムの数を返す
            const targetProcessId = await this.getProcessIdFromHandle(hwnd);
            const counts = await Promise.all(
                this.taskBarItems.map(item => this.getProcessIdFromHandle(item.handle))
            );
            return counts.filter(processId => processId === targetProcessId).length;
        } catch (error) {
            console.error('Error counting by same process:', error);
            return 0;
        }
    }

    // プロセスIDの取得
    async getProcessIdFromHandle(hwnd) {
        try {
            return await this.requestProcessId(hwnd);
        } catch (error) {
            console.error('Error getting process ID:', error);
            return hwnd; // フォールバック
        }
    }

    // アプリケーション順序の更新
    updateApplicationOrder(orderedExecutablePaths) {
        this.sendMessageToHost('update_application_order', {
            orderedExecutablePaths: orderedExecutablePaths
        });
    }

    // ウィンドウ順序の更新
    updateWindowOrder(orderedWindows) {
        this.sendMessageToHost('update_window_order', {
            orderedWindows: orderedWindows
        });
    }

    // アイテムのソート
    async sortItemsByOrder(items) {
        try {
            return await this.requestSortByOrder(items);
        } catch (error) {
            console.error('Error sorting items:', error);
            return items; // フォールバック：ソートせずにそのまま返す
        }
    }

    // 仮想デスクトップ判定の要求
    async requestIsWindowOnCurrentVirtualDesktop(windowHandle) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                resolve(true); // タイムアウト時はtrueを返す（安全側に倒す）
            }, 1000);

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
                    resolve(true);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            this.sendMessageToHost('request_is_window_on_current_virtual_desktop', { windowHandle: windowHandle });
        });
    }

    // プロセスIDの要求
    async requestProcessId(windowHandle) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                resolve(windowHandle); // タイムアウト時はハンドル値を返す
            }, 1000);

            const responseHandler = (event) => {
                try {
                    let data;
                    if (typeof event.data === 'string') {
                        data = JSON.parse(event.data);
                    } else {
                        data = event.data;
                    }

                    if (data && data.type === 'process_id_response' &&
                        data.windowHandle === windowHandle) {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);
                        resolve(data.processId);
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    resolve(windowHandle);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            this.sendMessageToHost('request_process_id', { windowHandle: windowHandle });
        });
    }

    // ソート要求
    async requestSortByOrder(items) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                resolve(items); // タイムアウト時はソートせずに返す
            }, 2000);

            const responseHandler = (event) => {
                try {
                    let data;
                    if (typeof event.data === 'string') {
                        data = JSON.parse(event.data);
                    } else {
                        data = event.data;
                    }

                    if (data && data.type === 'sort_by_order_response') {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);
                        resolve(data.sortedItems);
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    resolve(items);
                }
            };

            window.chrome.webview.addEventListener('message', responseHandler);
            this.sendMessageToHost('request_sort_by_order', { items: items });
        });
    }
}

// グローバルインスタンス
let windowManager = null;

// WindowManagerのファクトリ関数
function createWindowManager() {
    if (!windowManager) {
        windowManager = new WindowManager();
    }
    return windowManager;
}

// エクスポート（モジュール形式でない場合）
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { WindowManager, createWindowManager };
} else {
    // グローバルスコープに公開
    window.WindowManager = WindowManager;
    window.createWindowManager = createWindowManager;
}
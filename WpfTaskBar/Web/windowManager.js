// WindowManager for WebView2
// ウィンドウ管理のロジックをJavaScript側に移植

class WindowManager {
    constructor() {
        this.windowHandles = [];
        this.taskBarItems = [];
        this.isRunning = false;
        this.updateInterval = null;
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

            sendMessageToHost('request_window_handles');
        });
    }

    // タスクバーウィンドウの更新処理
    async updateTaskBarWindows() {
        try {
            const updateTaskBarItems = [];

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
                    }
                }
            }

            // 全てのアイテムを更新（プロセス名などの最新情報を取得）
            for (const taskBarWindow of this.taskBarItems) {
                const updatedItem = await this.createTaskBarItem(taskBarWindow.handle, foregroundHwnd);
                updateTaskBarItems.push(updatedItem);
            }

            // タスクバー一覧を更新する
            updateTaskList(updateTaskBarItems);

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
            sendMessageToHost('request_foreground_window');
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
            sendMessageToHost('request_is_taskbar_window', { windowHandle: windowHandle });
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
            sendMessageToHost('request_window_info', { windowHandle: windowHandle });
        });
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
            return null;
        }
    }

    // アプリケーション順序の更新
    updateApplicationOrder(orderedExecutablePaths) {
        sendMessageToHost('update_application_order', {
            orderedExecutablePaths: orderedExecutablePaths
        });
    }

    // ウィンドウ順序の更新
    updateWindowOrder(orderedWindows) {
        sendMessageToHost('update_window_order', {
            orderedWindows: orderedWindows
        });
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
            sendMessageToHost('request_is_window_on_current_virtual_desktop', { windowHandle: windowHandle });
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
            sendMessageToHost('request_process_id', { windowHandle: windowHandle });
        });
    }
}

const windowManager = new WindowManager();
windowManager.start();

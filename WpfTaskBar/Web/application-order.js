class ApplicationOrder {
    constructor() {
        this.aboveRelations = new Map(); // Map<string, Set<string>>
        this.windowOrderByApplication = new Map(); // Map<string, string[]>
        this.isInitialized = false;
    }

    async setup() {
        try {
            await this.loadRelations();
            await this.loadWindowOrder();
            this.isInitialized = true;
            console.log('ApplicationOrder initialized');
        } catch (error) {
            console.error('ApplicationOrder initialization failed:', error);
        }
    }

    /**
     * アプリケーションの順序を更新する
     * @param {string[]} orderedPaths - 順序付けされた実行可能ファイルのパス配列
     */
    updateOrderFromList(orderedPaths) {
        const pathList = orderedPaths.filter(p => p && p.trim() !== '');

        for (let i = 0; i < pathList.length; i++) {
            const currentApp = pathList[i];

            if (!this.aboveRelations.has(currentApp)) {
                this.aboveRelations.set(currentApp, new Set());
            }

            for (let j = i + 1; j < pathList.length; j++) {
                const belowApp = pathList[j];
                this.aboveRelations.get(currentApp).add(belowApp);

                if (this.aboveRelations.has(belowApp)) {
                    this.aboveRelations.get(belowApp).delete(currentApp);
                }
            }
        }

        this.saveRelations();
    }

    /**
     * ウィンドウの順序を更新する
     * @param {Array<{handle: string, moduleFileName: string}>} orderedWindows - 順序付けされたウィンドウ配列
     */
    updateWindowOrder(orderedWindows) {
        const windowsByApp = new Map();

        // アプリケーションごとにウィンドウをグループ化
        orderedWindows
            .filter(w => w.handle && w.handle.trim() !== '' && w.moduleFileName && w.moduleFileName.trim() !== '')
            .forEach(w => {
                if (!windowsByApp.has(w.moduleFileName)) {
                    windowsByApp.set(w.moduleFileName, []);
                }
                windowsByApp.get(w.moduleFileName).push(w.handle);
            });

        // ウィンドウ順序を更新
        windowsByApp.forEach((handles, moduleFileName) => {
            this.windowOrderByApplication.set(moduleFileName, handles);
        });

        this.saveWindowOrder();
    }

    /**
     * 関係に基づいてアイテムをソートする
     * @param {T[]} items - ソートするアイテム配列
     * @param {function(T): string} getExecutablePath - 実行可能ファイルパスを取得する関数
     * @param {function(T): string} getHandle - ハンドルを取得する関数（オプション）
     * @returns {T[]} ソートされたアイテム配列
     */
    sortByRelations(items, getExecutablePath, getHandle = null) {
        const itemList = [...items];
        const pathToItems = new Map();

        // パスごとにアイテムをグループ化
        itemList.forEach(item => {
            const path = getExecutablePath(item);
            if (path && path.trim() !== '') {
                if (!pathToItems.has(path)) {
                    pathToItems.set(path, []);
                }
                pathToItems.get(path).push(item);
            }
        });

        const paths = Array.from(pathToItems.keys());
        const sortedPaths = this.topologicalSort(paths);
        const result = [];

        // ソートされたパス順序に従ってアイテムを並び替え
        sortedPaths.forEach(path => {
            if (pathToItems.has(path)) {
                const itemsForPath = pathToItems.get(path);

                // 同一アプリケーション内でのウィンドウ順序を適用
                if (this.windowOrderByApplication.has(path) && getHandle) {
                    const savedOrder = this.windowOrderByApplication.get(path);
                    const orderedItems = [];
                    const remainingItems = [...itemsForPath];

                    // 保存された順序に従って並び替え
                    savedOrder.forEach(savedHandle => {
                        const matchingItem = remainingItems.find(item => getHandle(item) === savedHandle);
                        if (matchingItem) {
                            orderedItems.push(matchingItem);
                            const index = remainingItems.indexOf(matchingItem);
                            remainingItems.splice(index, 1);
                        }
                    });

                    // 保存された順序にない新しいアイテムを追加
                    orderedItems.push(...remainingItems);
                    result.push(...orderedItems);
                } else {
                    result.push(...itemsForPath);
                }
            }
        });

        return result;
    }

    /**
     * トポロジカルソート
     * @param {string[]} apps - アプリケーションパス配列
     * @returns {string[]} ソートされたアプリケーションパス配列
     */
    topologicalSort(apps) {
        const inDegree = new Map();
        const graph = new Map();

        // 初期化
        apps.forEach(app => {
            inDegree.set(app, 0);
            graph.set(app, []);
        });

        // グラフを構築
        apps.forEach(app => {
            if (this.aboveRelations.has(app)) {
                this.aboveRelations.get(app).forEach(belowApp => {
                    if (apps.includes(belowApp)) {
                        graph.get(app).push(belowApp);
                        inDegree.set(belowApp, inDegree.get(belowApp) + 1);
                    }
                });
            }
        });

        // 入次数が0のノードを見つける
        const queue = [];
        apps.forEach(app => {
            if (inDegree.get(app) === 0) {
                queue.push(app);
            }
        });

        const result = [];
        while (queue.length > 0) {
            const current = queue.shift();
            result.push(current);

            graph.get(current).forEach(neighbor => {
                inDegree.set(neighbor, inDegree.get(neighbor) - 1);
                if (inDegree.get(neighbor) === 0) {
                    queue.push(neighbor);
                }
            });
        }

        // 循環参照があった場合、残りのアプリケーションを追加
        apps.forEach(app => {
            if (!result.includes(app)) {
                result.push(app);
            }
        });

        return result;
    }

    /**
     * 関係データを保存する（C#側にイベントを送信）
     */
    async saveRelations() {
        try {
            const serializableData = {};
            this.aboveRelations.forEach((value, key) => {
                serializableData[key] = Array.from(value);
            });

            await this.writeFile('application_relations.json', JSON.stringify(serializableData));
            console.log('Application relations save requested');
        } catch (error) {
            console.error('関係データの保存に失敗しました:', error);
        }
    }

    /**
     * 関係データを読み込む（C#側から取得）
     */
    async loadRelations() {
        try {
            const dataString = await this.readFile('application_relations.json');
            if (dataString) {
                const relations = JSON.parse(dataString);
                this.aboveRelations.clear();
                Object.entries(relations).forEach(([key, values]) => {
                    this.aboveRelations.set(key, new Set(values));
                });
            }
        } catch (error) {
            console.error('関係データの読み込みに失敗しました:', error);
            this.aboveRelations = new Map();
        }
    }

    /**
     * ウィンドウ順序データを保存する（C#側にイベントを送信）
     */
    async saveWindowOrder() {
        try {
            const serializableData = {};
            this.windowOrderByApplication.forEach((value, key) => {
                serializableData[key] = value;
            });

            await this.writeFile('window_order.json', JSON.stringify(serializableData));
            console.log('Window order save requested');
        } catch (error) {
            console.error('ウィンドウ順序の保存に失敗しました:', error);
        }
    }

    /**
     * ウィンドウ順序データを読み込む（C#側から取得）
     */
    async loadWindowOrder() {
        try {
            const dataString = await this.readFile('window_order.json');
            if (dataString) {
                const windowOrder = JSON.parse(dataString);
                this.windowOrderByApplication.clear();
                Object.entries(windowOrder).forEach(([key, value]) => {
                    this.windowOrderByApplication.set(key, value);
                });
            }
        } catch (error) {
            console.error('ウィンドウ順序の読み込みに失敗しました:', error);
            this.windowOrderByApplication = new Map();
        }
    }

    /**
     * ファイルに文字列データを書き込む（汎用API）
     * @param {string} filename - ファイル名
     * @param {string} data - 書き込むデータ
     */
    async writeFile(filename, data) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                window.chrome.webview.removeEventListener('message', responseHandler);
                reject(new Error('writeFile timeout'));
            }, 1000);

            const responseHandler = (event) => {
                try {
                    let responseData;
                    if (typeof event.data === 'string') {
                        responseData = JSON.parse(event.data);
                    } else {
                        responseData = event.data;
                    }

                    if (responseData && responseData.type === 'file_write_response' && responseData.filename === filename) {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);

                        if (responseData.success) {
                            resolve();
                        } else {
                            reject(new Error(responseData.error || 'File write failed'));
                        }
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    reject(error);
                }
            };

            try {
                window.chrome.webview.addEventListener('message', responseHandler);
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'file_write_request',
                    filename: filename,
                    data: data
                }));
            } catch (error) {
                clearTimeout(timeout);
                reject(error);
            }
        });
    }

    /**
     * ファイルから文字列データを読み込む（汎用API）
     * @param {string} filename - ファイル名
     * @returns {Promise<string>} ファイル内容
     */
    async readFile(filename) {
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                window.chrome.webview.removeEventListener('message', responseHandler);
                reject(new Error('readFile timeout'));
            }, 1000);

            const responseHandler = (event) => {
                try {
                    let responseData;
                    if (typeof event.data === 'string') {
                        responseData = JSON.parse(event.data);
                    } else {
                        responseData = event.data;
                    }

                    if (responseData && responseData.type === 'file_read_response' && responseData.filename === filename) {
                        clearTimeout(timeout);
                        window.chrome.webview.removeEventListener('message', responseHandler);

                        if (responseData.success) {
                            resolve(responseData.data);
                        } else {
                            reject(new Error(responseData.error || 'File read failed'));
                        }
                    }
                } catch (error) {
                    clearTimeout(timeout);
                    window.chrome.webview.removeEventListener('message', responseHandler);
                    reject(error);
                }
            };

            try {
                window.chrome.webview.addEventListener('message', responseHandler);
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'file_read_request',
                    filename: filename
                }));
            } catch (error) {
                clearTimeout(timeout);
                reject(error);
            }
        });
    }
}

// グローバルインスタンスを作成
const applicationOrder = new ApplicationOrder();

applicationOrder.setup();

window.applicationOrder = applicationOrder;

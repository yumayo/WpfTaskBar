let tasks = [];
let draggedTask = null;
let draggedElement = null;

// タスクリストの更新
function updateTaskList(newTasks) {
    tasks = window.applicationOrder.sortByRelations(newTasks, (task) => task.moduleFileName, getTaskKey)
    updateTaskListOrder();
}

// タスクアイテムの作成
function createTaskItem(task) {
    const item = document.createElement('div');
    item.className = `task-item ${task.isForeground ? 'foreground' : ''}`;
    item.dataset.handle = task.handle;
    item.dataset.moduleFileName = task.moduleFileName;
    // Chromeタブの場合はtabIdとwindowIdも設定
    if (task.isChrome) {
        item.dataset.tabId = task.tabId;
        item.dataset.windowId = task.windowId;
    }
    item.draggable = true; // ドラッグ可能にする

    // アイコン
    const icon = document.createElement('div');
    icon.className = 'task-icon';

    if (task.iconData) {
        const img = document.createElement('img');
        img.src = task.iconData;
        img.style.width = '100%';
        img.style.height = '100%';
        icon.appendChild(img);
    } else {
        // デフォルトアイコン（プロセス名の最初の文字）
        const processName = task.moduleFileName ?
            task.moduleFileName.split('\\').pop().split('.')[0] :
            (task.title || 'Unknown').split(' ')[0];
        icon.textContent = processName.charAt(0).toUpperCase();
    }

    // Chromeタブの場合のFavicon（アイコンとタイトルの間）
    if (task.isChrome) {
        const favicon = document.createElement('img');
        if (task.faviconData) {
            favicon.src = task.faviconData;
        }
        favicon.className = 'chrome-favicon';
        item.appendChild(icon);
        item.appendChild(favicon);
    } else {
        item.appendChild(icon);
    }

    // テキスト
    const text = document.createElement('div');
    text.className = 'task-text';
    text.textContent = task.title || 'Unknown';
    text.title = task.title || 'Unknown'; // ツールチップ

    item.appendChild(text);

    // ドラッグ&ドロップイベントリスナー
    setupDragAndDrop(item, task);

    // イベントリスナー
    item.addEventListener('click', (e) => {
        // ドラッグ中のクリックは無視
        if (item.classList.contains('dragging')) {
            return;
        }

        // 全てのタスクアイテムから foreground クラスを削除
        document.querySelectorAll('.task-item').forEach(taskItem => {
            taskItem.classList.remove('foreground');
        });

        // クリックされたアイテムに foreground クラスを追加
        item.classList.add('foreground');

        // NOTE: あえて dataset.isForegroundは設定してません。なぜなら非同期でタスク一覧が更新されているため、クリックした後に一瞬戻ってしまうからです。
        //       ここでisForegroundを設定しないことで、早期リターンで描画は更新されないようになっています。

        // Chromeタブの場合は、tabIdとwindowIdを送信
        if (task.isChrome) {
            sendMessageToHost('task_click', {
                handle: task.handle,
                moduleFileName: task.moduleFileName,
                tabId: task.tabId,
                windowId: task.windowId
            });
        } else {
            sendMessageToHost('task_click', {
                handle: task.handle,
                moduleFileName: task.moduleFileName
            });
        }
    });

    // 中クリックでプロセス終了（Chromeの場合はタブを閉じる）
    item.addEventListener('mousedown', (e) => {
        if (e.button === 1) { // 中クリック
            e.preventDefault();
            if (task.isChrome) {
                sendMessageToHost('task_middle_click', {
                    handle: task.handle,
                    moduleFileName: task.moduleFileName,
                    tabId: task.tabId,
                    windowId: task.windowId,
                    isChrome: true
                });
            } else {
                sendMessageToHost('task_middle_click', {
                    handle: task.handle,
                    moduleFileName: task.moduleFileName
                });
            }
        }
    });

    return item;
}

// アプリケーショングループの開始・終了インデックスを取得
function getApplicationGroupRange(targetModuleName) {
    let startIndex = -1;
    let endIndex = -1;

    for (let i = 0; i < tasks.length; i++) {
        if (tasks[i].moduleFileName === targetModuleName) {
            if (startIndex === -1) {
                startIndex = i;
            }
            endIndex = i;
        }
    }

    return { startIndex, endIndex };
}

// アプリケーショングループ全体でのマウス位置判定
function isDropAboveApplicationGroup(targetModuleName, mouseY) {

    const { firstElement, lastElement } = getItems(targetModuleName)

    if (!firstElement || !lastElement) {
        return null;
    }

    // グループ全体の境界を計算
    const firstRect = firstElement.getBoundingClientRect();
    const lastRect = lastElement.getBoundingClientRect();

    const groupTop = firstRect.top;
    const groupBottom = lastRect.bottom;
    const groupCenter = groupTop + (groupBottom - groupTop) / 2;

    return mouseY < groupCenter;
}

// アプリケーショングループの開始・終了インデックスを取得
function getItems(targetModuleName) {
    const { startIndex, endIndex } = getApplicationGroupRange(targetModuleName)

    if (startIndex === -1 || endIndex === -1) {
        return null
    }

    // グループ内のすべての要素を取得
    const taskElements = document.querySelectorAll('.task-item');
    const firstElement = taskElements[startIndex];
    const lastElement = taskElements[endIndex];

    if (!firstElement || !lastElement) {
        return null;
    }

    return { firstElement, lastElement }
}

// ドラッグ&ドロップの設定
function setupDragAndDrop(item, task) {
    // ドラッグ開始
    item.addEventListener('dragstart', (e) => {
        onDragStart(item, task, e);
    });

    // ドラッグ終了
    item.addEventListener('dragend', (e) => {
        onDragEnd(item);
    });

    // ドラッグオーバー（他の要素の上を通過）
    item.addEventListener('dragover', (e) => {
        onDragOver(item, e);
    });

    // ドラッグエンター（要素に入る）
    item.addEventListener('dragenter', (e) => {
        onDragEnter(e);
    });

    // ドラッグリーブ（要素から出る）
    item.addEventListener('dragleave', (e) => {
        onDragLeave(item, e);
    });

    // ドロップ
    item.addEventListener('drop', (e) => {
        onDrop(item, e);
    });
}

function onDragStart(item, task, e) {
    draggedTask = task;
    draggedElement = item;
    item.classList.add('dragging');

    // ドラッグデータを設定
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', task.handle);

    // 少し遅延してスタイルを適用（ドラッグ画像に影響しないよう）
    setTimeout(() => {
        item.style.opacity = '0.5';
    }, 0);
}

function onDragEnd(item) {
    item.classList.remove('dragging');
    item.style.opacity = '';

    // 全ての要素からdrag-overクラスを除去
    document.querySelectorAll('.task-item').forEach(el => {
        el.classList.remove('drag-over-above', 'drag-over-below');
    });

    draggedTask = null;
    draggedElement = null;
}

function onDragOver(item, e) {
    if (draggedElement && draggedElement !== item) {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';

        const draggedModuleName = draggedElement.dataset.moduleFileName;
        const targetModuleName = item.dataset.moduleFileName;

        // 異なるアプリケーション
        if (draggedModuleName !== targetModuleName) {
            const mouseY = e.clientY;
            const { firstElement, lastElement } = getItems(targetModuleName)

            if (!firstElement || !lastElement) {
                return null;
            }

            const isAbove = isDropAboveApplicationGroup(targetModuleName, mouseY)
            if (isAbove !== null) {

                // 既存のクラスを削除
                firstElement.classList.remove('drag-over-above', 'drag-over-below');
                lastElement.classList.remove('drag-over-above', 'drag-over-below');

                // 適切なクラスを追加
                if (isAbove) {
                    firstElement.classList.add('drag-over-above');
                } else {
                    lastElement.classList.add('drag-over-below');
                }
            }
        } else {
            // マウスの位置から上半分か下半分かを判定
            const rect = item.getBoundingClientRect();
            const mouseY = e.clientY;
            const itemCenter = rect.top + rect.height / 2;
            const isAbove = mouseY < itemCenter;

            // 既存のクラスを削除
            item.classList.remove('drag-over-above', 'drag-over-below');

            // 適切なクラスを追加
            if (isAbove) {
                item.classList.add('drag-over-above');
            } else {
                item.classList.add('drag-over-below');
            }
        }
    }
}

function onDragEnter(e) {
    if (draggedElement) {
        e.preventDefault();
        // dragoverで位置判定を行うためここでは何もしない
    }
}

function onDragLeave(item, e) {
    // 子要素に移動した場合は除外
    if (!item.contains(e.relatedTarget)) {
        item.classList.remove('drag-over-above', 'drag-over-below');
    }
}

function onDrop(item, e) {
    e.preventDefault();
    item.classList.remove('drag-over-above', 'drag-over-below');

    if (draggedTask && draggedElement && draggedElement !== item) {

        const draggedModuleName = draggedElement.dataset.moduleFileName;
        const targetModuleName = item.dataset.moduleFileName;
        
        let differentApplication = draggedModuleName !== targetModuleName;
        
        if (draggedElement.dataset.windowId !== undefined && item.dataset.windowId !== undefined) {
            if (parseInt(draggedElement.dataset.windowId, 10) !== parseInt(item.dataset.windowId, 10)) {
                differentApplication = true;
            }
        }

        // 異なるアプリケーション
        if (differentApplication) {
            const mouseY = e.clientY;
            const { firstElement, lastElement } = getItems(targetModuleName)

            if (!firstElement || !lastElement) {
                return null;
            }

            const isAbove = isDropAboveApplicationGroup(targetModuleName, mouseY)
            if (isAbove !== null) {

                // 既存のクラスを削除
                firstElement.classList.remove('drag-over-above', 'drag-over-below');
                lastElement.classList.remove('drag-over-above', 'drag-over-below');

                // 適切なクラスを追加
                if (isAbove) {
                    firstElement.classList.add('drag-over-above');
                } else {
                    lastElement.classList.add('drag-over-below');
                }

                let dropTargetTask = isAbove ? firstElement.dataset : lastElement.dataset

                dropTargetTask = {
                    handle: parseInt(dropTargetTask.handle, 10),
                    tabId: parseInt(dropTargetTask.tabId, 10),
                    windowId: parseInt(dropTargetTask.windowId, 10),
                }

                // タスクの順序を変更
                reorderTasks(draggedTask, dropTargetTask, isAbove);
            }
        } else {
            // マウスの位置から上半分か下半分かを判定
            const rect = item.getBoundingClientRect();
            const mouseY = e.clientY;
            const itemCenter = rect.top + rect.height / 2;
            const isAbove = mouseY < itemCenter;
            
            const dropTargetTask = {
                handle: parseInt(item.dataset.handle, 10),
                tabId: parseInt(item.dataset.tabId, 10),
                windowId: parseInt(item.dataset.windowId, 10),
            }

            // タスクの順序を変更
            reorderTasks(draggedTask, dropTargetTask, isAbove);
        }
    }
}

// タスクの順序を変更（同種アプリケーションの一括移動対応）
function reorderTasks(draggedTask, targetTask, dropAbove) {
    
    // targetTaskがdatasetオブジェクトの場合とtaskオブジェクトの場合を考慮
    const targetIndex = tasks.findIndex(t => getTaskKey(t) === getTaskKey(targetTask));
    const draggedIndex = tasks.findIndex(t => getTaskKey(t) === getTaskKey(draggedTask));

    if (targetIndex === -1 || draggedIndex === -1 || draggedIndex === targetIndex) {
        return;
    }

    const targetTaskObj = tasks[targetIndex];
    const draggedTaskObj = tasks[draggedIndex];

    // 同種アプリケーション（同じmoduleFileName）のタスクを全て取得
    const draggedModuleName = draggedTaskObj.moduleFileName;
    const targetModuleName = targetTaskObj.moduleFileName;

    let differentApplication = draggedModuleName !== targetModuleName;
    if (draggedTaskObj.windowId !== targetTaskObj.windowId) {
        differentApplication = true;
    }

    // Chromeタブの場合の特別な処理
    const isBothChrome = draggedTaskObj.isChrome && targetTaskObj.isChrome;

    // 異なるアプリケーション間での移動の場合のみ一括移動を実行
    if (differentApplication) {
        reorderTasksWithSameApp(draggedTaskObj.handle, targetTaskObj.handle, dropAbove);
    } else if (isBothChrome) {
        // Chromeタブ同士の移動は個別のタブとして扱う（tabIdとwindowIdを使用）
        reorderSingleTaskByIndex(draggedIndex, targetIndex, dropAbove);
    } else {
        // 同じアプリケーション内での移動は従来通り
        reorderSingleTask(draggedTaskObj.handle, targetTaskObj.handle, dropAbove);
    }

    // UIを更新
    updateTaskListOrder();

    window.applicationOrder.updateOrderFromList(tasks.map(task => task.moduleFileName))
    
    // アプリケーションのグルーピングを行っていますが、ここはgetTaskKeyで判定しません。
    // ここでChromeのwindowIdとtabIdでソートしてしまうと、Chrome側でタブを並び替えたときに反映されません。
    window.applicationOrder.updateWindowOrder(tasks.map(task => ({ handle: task.handle, moduleFileName: task.moduleFileName })))
}

// 同種アプリケーションの一括移動
function reorderTasksWithSameApp(draggedHandle, targetHandle, dropAbove) {
    const draggedIndex = tasks.findIndex(t => t.handle === draggedHandle);
    const targetIndex = tasks.findIndex(t => t.handle === targetHandle);
    const draggedTask = tasks[draggedIndex];
    const targetTask = tasks[targetIndex];

    // 同じmoduleFileNameを持つタスクを全て取得（元の順序を保持）
    const sameAppTasks = tasks.filter(task => task.moduleFileName === draggedTask.moduleFileName);

    // 他のアプリケーションのタスクだけを残したリストを作成
    const otherTasks = tasks.filter(task => task.moduleFileName !== draggedTask.moduleFileName);

    // ターゲットタスクの新しい位置を計算
    let targetNewIndex = otherTasks.findIndex(t => t.handle === targetHandle);

    // ドロップ位置に応じて挿入位置を調整
    let insertIndex = targetNewIndex;
    if (targetNewIndex !== -1) {
        if (!dropAbove) {
            insertIndex = targetNewIndex + 1;
        }
    } else {
        // ターゲットが見つからない場合は最後に追加
        insertIndex = otherTasks.length;
    }

    // 新しいタスクリストを構築
    const newTasks = [...otherTasks];
    newTasks.splice(insertIndex, 0, ...sameAppTasks);

    // tasksを更新
    tasks = newTasks;
}

// 単一タスクの移動（インデックスベース、Chromeタブ用）
function reorderSingleTaskByIndex(draggedIndex, targetIndex, dropAbove) {
    if (draggedIndex === -1 || targetIndex === -1) {
        return;
    }

    // 配列から要素を取り出し
    const draggedTask = tasks.splice(draggedIndex, 1)[0];

    // 新しいインデックスを計算
    let newIndex = targetIndex;

    // ドラッグ元がターゲットより前にあった場合、ターゲットのインデックスが1つ減る
    if (draggedIndex < targetIndex) {
        newIndex = targetIndex - 1;
    }

    // ドロップ位置に応じてインデックスを調整
    if (dropAbove) {
        // 要素の上にドロップする場合はそのまま
        tasks.splice(newIndex, 0, draggedTask);
    } else {
        // 要素の下にドロップする場合は+1
        tasks.splice(newIndex + 1, 0, draggedTask);
    }
}

// 単一タスクの移動（同じアプリケーション内での移動用）
function reorderSingleTask(draggedHandle, targetHandle, dropAbove) {
    // この関数は既にreorderTasks内でdraggedIndexとtargetIndexが計算されているため、
    // 再度検索する必要があります（グローバル変数を使わないため）
    const draggedIndex = tasks.findIndex(t => t.handle === draggedHandle);
    const targetIndex = tasks.findIndex(t => t.handle === targetHandle);

    reorderSingleTaskByIndex(draggedIndex, targetIndex, dropAbove);
}

// タスクの一意なキーを生成
function getTaskKey(task) {
    if (task.isChrome) {
        return `${task.handle}-${task.windowId}-${task.tabId}`;
    } else {
        return `${task.handle}`;
    }
}

// タスク要素の内容を更新（変更がある場合のみ）
function updateTaskItemContent(item, task) {
    let needsUpdate = false;

    // isForegroundクラスの更新
    const hasForeground = item.classList.contains('foreground');
    if (hasForeground !== task.isForeground) {
        if (task.isForeground) {
            item.classList.add('foreground');
        } else {
            item.classList.remove('foreground');
        }
        needsUpdate = true;
    }

    // タイトルの更新
    const textElement = item.querySelector('.task-text');
    if (textElement && textElement.textContent !== task.title) {
        textElement.textContent = task.title || 'Unknown';
        textElement.title = task.title || 'Unknown';
        needsUpdate = true;
    }

    // アイコンの更新（iconDataが変わった場合）
    const iconElement = item.querySelector('.task-icon');
    if (iconElement) {
        const currentIconSrc = iconElement.querySelector('img')?.src || '';

        if (currentIconSrc !== task.iconData) {
            // アイコンを再構築
            iconElement.innerHTML = '';

            if (task.iconData) {
                const img = document.createElement('img');
                img.src = task.iconData;
                img.style.width = '100%';
                img.style.height = '100%';
                iconElement.appendChild(img);
            } else {
                const processName = task.moduleFileName ?
                    task.moduleFileName.split('\\').pop().split('.')[0] :
                    (task.title || 'Unknown').split(' ')[0];
                iconElement.textContent = processName.charAt(0).toUpperCase();
            }
            needsUpdate = true;
        }
    }

    // Favicon の更新（Chrome タブの場合）
    if (task.isChrome) {
        const faviconElement = item.querySelector('.chrome-favicon');
        if (faviconElement) {
            const currentFaviconSrc = faviconElement.src || '';
            const newFaviconSrc = task.faviconData || '';

            if (currentFaviconSrc !== newFaviconSrc) {
                faviconElement.src = newFaviconSrc;
                needsUpdate = true;
            }
        }
    }

    return needsUpdate;
}

// タスクリストの順序のみを更新（データの再取得なし）
function updateTaskListOrder() {
    const taskList = document.getElementById('taskList');

    // 既存の要素をキーでマップに保存
    const existingItems = new Map();
    Array.from(taskList.children).forEach(item => {
        const handle = item.dataset.handle;
        const windowId = item.dataset.windowId;
        const tabId = item.dataset.tabId;

        let key;
        if (windowId !== undefined && tabId !== undefined) {
            key = `${handle}-${windowId}-${tabId}`;
        } else {
            key = `${handle}`;
        }
        existingItems.set(key, item);
    });

    // 新しいタスクリストに基づいて要素を配置
    const newChildren = [];
    const usedKeys = new Set();

    tasks.forEach(task => {
        const key = getTaskKey(task);
        usedKeys.add(key);

        let item = existingItems.get(key);

        if (item) {
            // 既存の要素を再利用し、内容を更新
            updateTaskItemContent(item, task);
        } else {
            // 新しい要素を作成
            item = createTaskItem(task);
        }

        newChildren.push(item);
    });

    // 削除された要素をクリーンアップ
    existingItems.forEach((item, key) => {
        if (!usedKeys.has(key)) {
            item.remove();
        }
    });

    // 要素の順序を更新（変更がある場合のみDOM操作）
    newChildren.forEach((item, index) => {
        const currentItem = taskList.children[index];
        if (currentItem !== item) {
            if (currentItem) {
                taskList.insertBefore(item, currentItem);
            } else {
                taskList.appendChild(item);
            }
        }
    });
}

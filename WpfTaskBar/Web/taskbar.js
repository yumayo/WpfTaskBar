let tasks = [];
let draggedTask = null;
let draggedElement = null;

// タスクリストの更新
function updateTaskList(newTasks) {
    newTasks = window.applicationOrder.sortByRelations(newTasks, (task) => task.moduleFileName, (task) => task.handle)

    // 変更がない場合は処理をスキップ
    if (areTasksEqual(tasks, newTasks)) {
        return;
    }

    tasks = newTasks;

    const taskList = document.getElementById('taskList');

    // 既存のタスクアイテムをクリア
    taskList.innerHTML = '';

    // タスクアイテムを追加
    tasks.forEach(task => {
        const taskItem = createTaskItem(task);
        taskList.appendChild(taskItem);
    });
}

// タスクリストの比較関数
function areTasksEqual(oldTasks, newTasks) {
    if (oldTasks.length !== newTasks.length) {
        return false;
    }

    for (let i = 0; i < oldTasks.length; i++) {
        const oldTask = oldTasks[i];
        const newTask = newTasks[i];

        if (oldTask.handle !== newTask.handle ||
            oldTask.title !== newTask.title ||
            oldTask.moduleFileName !== newTask.moduleFileName ||
            oldTask.isForeground !== newTask.isForeground ||
            oldTask.iconData !== newTask.iconData) {
            return false;
        }
    }

    return true;
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
        img.src = `data:image/png;base64,${task.iconData}`;
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

    // テキスト
    const text = document.createElement('div');
    text.className = 'task-text';
    text.textContent = task.title || 'Unknown';
    text.title = task.title || 'Unknown'; // ツールチップ

    item.appendChild(icon);
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

    // 中クリックでプロセス終了
    item.addEventListener('mousedown', (e) => {
        if (e.button === 1) { // 中クリック
            e.preventDefault();
            sendMessageToHost('task_middle_click', {
                handle: task.handle,
                moduleFileName: task.moduleFileName
            });
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
    });

    // ドラッグ終了
    item.addEventListener('dragend', (e) => {
        item.classList.remove('dragging');
        item.style.opacity = '';

        // 全ての要素からdrag-overクラスを除去
        document.querySelectorAll('.task-item').forEach(el => {
            el.classList.remove('drag-over-above', 'drag-over-below');
        });

        draggedTask = null;
        draggedElement = null;
    });

    // ドラッグオーバー（他の要素の上を通過）
    item.addEventListener('dragover', (e) => {
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
    });

    // ドラッグエンター（要素に入る）
    item.addEventListener('dragenter', (e) => {
        if (draggedElement && draggedElement !== item) {
            e.preventDefault();
            // dragoverで位置判定を行うためここでは何もしない
        }
    });

    // ドラッグリーブ（要素から出る）
    item.addEventListener('dragleave', (e) => {
        // 子要素に移動した場合は除外
        if (!item.contains(e.relatedTarget)) {
            item.classList.remove('drag-over-above', 'drag-over-below');
        }
    });

    // ドロップ
    item.addEventListener('drop', (e) => {
        e.preventDefault();
        item.classList.remove('drag-over-above', 'drag-over-below');

        if (draggedTask && draggedElement && draggedElement !== item) {

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

                    const dropTargetHandle = isAbove ? firstElement.dataset.handle : lastElement.dataset.handle
                    const draggedHandle = draggedTask.handle;

                    // タスクの順序を変更
                    reorderTasks(draggedHandle, dropTargetHandle, isAbove);
                }
            } else {
                // マウスの位置から上半分か下半分かを判定
                const rect = item.getBoundingClientRect();
                const mouseY = e.clientY;
                const itemCenter = rect.top + rect.height / 2;
                const isAbove = mouseY < itemCenter;

                // タスクの順序を変更
                reorderTasks(draggedTask, item.dataset, isAbove);
            }
        }
    });
}

// タスクの順序を変更（同種アプリケーションの一括移動対応）
function reorderTasks(draggedTask, targetTask, dropAbove) {
    // targetTaskがdatasetオブジェクトの場合とtaskオブジェクトの場合を考慮
    const targetIndex = tasks.findIndex(t => {
        // handleが一致するかチェック
        if (t.handle !== targetTask.handle) {
            return false;
        }

        // Chromeタブの場合は、tabIdとwindowIdも一致する必要がある
        if (t.isChrome && targetTask.tabId && targetTask.windowId) {
            return t.tabId === parseInt(targetTask.tabId) && t.windowId === parseInt(targetTask.windowId);
        }

        return true;
    });

    const draggedIndex = tasks.findIndex(t => {
        if (typeof draggedTask === 'object' && draggedTask.handle) {
            // draggedTaskがtaskオブジェクトの場合
            if (t.handle !== draggedTask.handle) {
                return false;
            }

            if (t.isChrome && draggedTask.tabId && draggedTask.windowId) {
                return t.tabId === draggedTask.tabId && t.windowId === draggedTask.windowId;
            }

            return true;
        } else {
            // draggedTaskがhandleの文字列の場合
            return t.handle === draggedTask;
        }
    });

    if (targetIndex === -1 || draggedIndex === -1 || draggedIndex === targetIndex) {
        return;
    }

    const targetTaskObj = tasks[targetIndex];
    const draggedTaskObj = tasks[draggedIndex];

    // 同種アプリケーション（同じmoduleFileName）のタスクを全て取得
    const draggedModuleName = draggedTaskObj.moduleFileName;
    const targetModuleName = targetTaskObj.moduleFileName;

    // Chromeタブの場合の特別な処理
    const isBothChrome = draggedTaskObj.isChrome && targetTaskObj.isChrome;

    // 異なるアプリケーション間での移動の場合のみ一括移動を実行
    if (draggedModuleName !== targetModuleName) {
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

// タスクリストの順序のみを更新（データの再取得なし）
function updateTaskListOrder() {
    const taskList = document.getElementById('taskList');
    taskList.innerHTML = '';

    tasks.forEach(task => {
        const taskItem = createTaskItem(task);
        taskList.appendChild(taskItem);
    });
}

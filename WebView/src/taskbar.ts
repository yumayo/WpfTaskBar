import { sendMessageToHost } from './network';
import type { TaskBarItem, MessageData } from './types';

const UPDATE_INTERVAL = 100; // 更新間隔（ミリ秒）
const FOREGROUND_UPDATE_SKIP_DURATION = 1000; // クリック後のタスク更新スキップ時間（ミリ秒）

let taskBarItems: TaskBarItem[] = [];
let draggedTask: TaskBarItem | null = null;
let draggedElement: HTMLElement | null = null;
let lastClickTime = 0;

// 開始
function start(): void {
  console.log('WindowManager.start() called');
  updateTaskWindows();
  console.log('WindowManager started');
}

// ウィンドウリストの更新ループ
async function updateTaskWindows(): Promise<void> {
  try {
    // C#側にウィンドウハンドル一覧の取得を要求
    const windowHandles = await requestWindowHandles();

    // タスクバーウィンドウの更新
    await updateTaskBarWindows(windowHandles);

    // タスクリストの順序のみを更新
    updateTaskListOrder();

    // UPDATE_INTERVAL後に再実行
    setTimeout(() => {
      updateTaskWindows();
    }, UPDATE_INTERVAL);
  } catch (error) {
    console.error('Error in WindowManager.updateTaskWindows():', error);
    // エラーが発生した場合も継続
    setTimeout(() => {
      updateTaskWindows();
    }, UPDATE_INTERVAL);
  }
}

// C#側にウィンドウハンドル一覧を要求
async function requestWindowHandles(): Promise<number[]> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestWindowHandles timeout')), 1000);

    // レスポンス受信用の一時的なリスナー
    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'window_handles_response') {
          clearTimeout(timeout);
          window.chrome!.webview!.removeEventListener('message', responseHandler);
          resolve(data.windowHandles as number[]);
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    // イベントリスナー追加
    window.chrome!.webview!.addEventListener('message', responseHandler);

    sendMessageToHost('request_window_handles');
  });
}

// タスクバーウィンドウの更新処理
async function updateTaskBarWindows(windowHandles: number[]): Promise<void> {
  try {
    // フォアグラウンドウィンドウの取得
    const foregroundHwnd = await requestForegroundWindow();

    // ウィンドウハンドルに存在しないタスクバーを削除
    taskBarItems = taskBarItems.filter(item => {
      return windowHandles.some(handle => handle === item.handle);
    });

    // 各ウィンドウハンドルを処理
    for (const windowHandle of windowHandles) {
      // タスクバーウィンドウかどうかの判定をC#側に要求
      let isTaskBarWindow = await requestIsTaskBarWindow(windowHandle);

      // 現在の仮想デスクトップにあるウィンドウのみを対象とする
      if (isTaskBarWindow) {
        const isOnCurrentVirtualDesktop = await requestIsWindowOnCurrentVirtualDesktop(windowHandle);
        if (!isOnCurrentVirtualDesktop) {
          isTaskBarWindow = false;
        }
      }

      // 現在のタスクバーアイテムに含まれているか
      const existingTaskBarItem = taskBarItems.find(item => item.handle === windowHandle);

      if (existingTaskBarItem) {
        // 既存のアイテム
        if (!isTaskBarWindow) {
          // タスクバー管理外になったため削除
          // または異なる仮想デスクトップに移動されたため削除
          taskBarItems = taskBarItems.filter(item => item.handle !== windowHandle);
        } else {
          const taskBarItem = await createTaskBarItem(windowHandle, foregroundHwnd);
          const index = taskBarItems.findIndex(item => item.handle === taskBarItem.handle);
          taskBarItems[index] = taskBarItem;
        }
      } else {
        // 新しいアイテム
        if (isTaskBarWindow) {
          const taskBarItem = await createTaskBarItem(windowHandle, foregroundHwnd);
          taskBarItems.push(taskBarItem);
        }
      }
    }
  } catch (error) {
    console.error('Error in updateTaskBarWindows:', error);
  }
}

// フォアグラウンドウィンドウの取得
async function requestForegroundWindow(): Promise<number> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestForegroundWindow timeout')), 1000);

    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'foreground_window_response') {
          clearTimeout(timeout);
          window.chrome!.webview!.removeEventListener('message', responseHandler);
          resolve(data.foregroundWindow as number);
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    window.chrome!.webview!.addEventListener('message', responseHandler);
    sendMessageToHost('request_foreground_window');
  });
}

// タスクバーウィンドウかどうかの判定
async function requestIsTaskBarWindow(windowHandle: number): Promise<boolean> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestIsTaskBarWindow timeout')), 1000);

    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'is_taskbar_window_response') {
          const dataWindowHandle = parseInt(String(data.windowHandle), 10);
          if (dataWindowHandle === windowHandle) {
            clearTimeout(timeout);
            window.chrome!.webview!.removeEventListener('message', responseHandler);
            resolve(data.isTaskBarWindow as boolean);
          }
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    window.chrome!.webview!.addEventListener('message', responseHandler);
    sendMessageToHost('request_is_taskbar_window', { windowHandle: windowHandle });
  });
}

// タスクバーアイテムの作成
async function createTaskBarItem(windowHandle: number, foregroundHwnd: number): Promise<TaskBarItem> {
  try {
    const windowInfo = await requestWindowInfo(windowHandle);
    return {
      handle: windowHandle,
      moduleFileName: (windowInfo?.moduleFileName as string) || '',
      title: (windowInfo?.title as string) || '',
      isForeground: windowHandle === foregroundHwnd,
      iconData: (windowInfo?.iconData as string) || null,
      favIconData: (windowInfo?.favIconData as string) || null,
      tabId: (windowInfo?.tabId as number) || 0,
      windowId: (windowInfo?.windowId as number) || 0,
      url: (windowInfo?.url as string) || '',
    };
  } catch (error) {
    console.error('Error creating TaskBarItem:', error);
    return {
      handle: windowHandle,
      moduleFileName: '',
      title: '',
      isForeground: false,
      iconData: null,
    };
  }
}

// ウィンドウ情報の取得
async function requestWindowInfo(windowHandle: number): Promise<MessageData | null> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestWindowInfo timeout')), 1000);

    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'window_info_response') {
          if (data.windowHandle === windowHandle) {
            clearTimeout(timeout);
            window.chrome!.webview!.removeEventListener('message', responseHandler);
            resolve(data);
          }
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    window.chrome!.webview!.addEventListener('message', responseHandler);
    sendMessageToHost('request_window_info', { windowHandle: windowHandle });
  });
}

// 仮想デスクトップ判定の要求
async function requestIsWindowOnCurrentVirtualDesktop(windowHandle: number): Promise<boolean> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestIsWindowOnCurrentVirtualDesktop timeout')), 1000);

    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'is_window_on_current_virtual_desktop_response') {
          if (data.windowHandle === windowHandle) {
            clearTimeout(timeout);
            window.chrome!.webview!.removeEventListener('message', responseHandler);
            resolve(data.isOnCurrentVirtualDesktop as boolean);
          }
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    window.chrome!.webview!.addEventListener('message', responseHandler);
    sendMessageToHost('request_is_window_on_current_virtual_desktop', { windowHandle: windowHandle });
  });
}

// ウィンドウが最小化されているかを確認
async function requestIsWindowMinimized(handle: number): Promise<boolean> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestIsWindowMinimized timeout')), 1000);

    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'is_window_minimized_response') {
          if (data.handle === handle) {
            clearTimeout(timeout);
            window.chrome!.webview!.removeEventListener('message', responseHandler);
            resolve(data.isMinimized as boolean);
          }
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    window.chrome!.webview!.addEventListener('message', responseHandler);
    sendMessageToHost('request_is_window_minimized', { handle: handle });
  });
}

// 次にアクティブになるウィンドウを取得
async function requestNextWindowToActivate(handle: number): Promise<number> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestNextWindowToActivate timeout')), 1000);

    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'next_window_to_activate_response') {
          if (data.currentHandle === handle) {
            clearTimeout(timeout);
            window.chrome!.webview!.removeEventListener('message', responseHandler);
            resolve(data.nextHandle as number);
          }
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    window.chrome!.webview!.addEventListener('message', responseHandler);
    sendMessageToHost('request_next_window_to_activate', { handle: handle });
  });
}

// タスクリストの順序のみを更新
function updateTaskListOrder(): void {
  // タスクバー一覧をアプリケーション名でトポロジカルソートする
  taskBarItems = window.applicationOrder.sortByRelations(
    taskBarItems,
    (task) => task.moduleFileName,
    (task) => task.handle
  );

  const taskList = document.getElementById('taskList');
  if (!taskList) return;

  // 既存の要素をキーでマップに保存
  const existingItems = new Map<number, HTMLElement>();
  Array.from(taskList.children).forEach(item => {
    const handle = (item as HTMLElement).dataset.handle;
    if (handle) {
      existingItems.set(parseInt(handle, 10), item as HTMLElement);
    }
  });

  // 新しいタスクリストに基づいて要素を配置
  const newChildren: HTMLElement[] = [];
  const usedHandles = new Set<number>();

  taskBarItems.forEach(task => {
    usedHandles.add(task.handle);

    let item = existingItems.get(task.handle);

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
  existingItems.forEach((item, handle) => {
    if (!usedHandles.has(handle)) {
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

// タスクアイテムの作成
function createTaskItem(task: TaskBarItem): HTMLElement {
  const item = document.createElement('div');
  item.className = `task-item ${task.isForeground ? 'foreground' : ''}`;
  item.dataset.handle = String(task.handle);
  item.dataset.moduleFileName = task.moduleFileName;
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
  }

  item.appendChild(icon);

  // テキスト
  const text = document.createElement('div');
  text.className = 'task-text';
  text.textContent = task.title || 'Unknown';
  text.title = task.title || 'Unknown'; // ツールチップ

  item.appendChild(text);

  // ドラッグ&ドロップイベントリスナー
  setupDragAndDrop(item, task);

  return item;
}

// ドラッグ&ドロップの設定
function setupDragAndDrop(item: HTMLElement, task: TaskBarItem): void {
  // イベントリスナー
  item.addEventListener('click', (e) => onClick(item, task, e));

  // 中クリックでプロセス終了（Chromeの場合はタブを閉じる）
  item.addEventListener('mousedown', (e) => onMouseDown(item, task, e));

  // ドラッグ開始
  item.addEventListener('dragstart', (e) => onDragStart(item, task, e));

  // ドラッグ終了
  item.addEventListener('dragend', () => onDragEnd(item));

  // ドラッグオーバー（他の要素の上を通過）
  item.addEventListener('dragover', (e) => onDragOver(item, e));

  // ドラッグエンター（要素に入る）
  item.addEventListener('dragenter', (e) => onDragEnter(e));

  // ドラッグリーブ（要素から出る）
  item.addEventListener('dragleave', (e) => onDragLeave(item, e));

  // ドロップ
  item.addEventListener('drop', (e) => onDrop(item, e));
}

// タスクアイテムのクリック処理
async function onClick(item: HTMLElement, task: TaskBarItem, e: MouseEvent): Promise<void> {
  // ドラッグ中のクリックは無視
  if (item.classList.contains('dragging')) {
    return;
  }

  // 全てのタスクアイテムから foreground クラスを削除
  document.querySelectorAll('.task-item').forEach(taskItem => {
    taskItem.classList.remove('foreground');
  });

  // クリック時刻を記録（FOREGROUND_UPDATE_SKIP_DURATION後まで全タスクのforeground更新をスキップするため）
  lastClickTime = Date.now();

  try {
    // ウィンドウが最小化されているか確認
    const isMinimized = await requestIsWindowMinimized(task.handle);

    if (isMinimized) {
      // 最小化されている場合は復元してアクティブ化
      sendMessageToHost('restore_window', { handle: task.handle });

      // クリックされたアイテムに foreground クラスを追加
      item.classList.add('foreground');
    } else {
      // 現在のフォアグラウンドウィンドウを取得
      const foregroundWindow = await requestForegroundWindow();

      // クリックされたウィンドウが既にアクティブな場合
      if (task.handle === foregroundWindow) {
        // 次にアクティブになるウィンドウを取得
        const nextHandle = await requestNextWindowToActivate(task.handle);

        // 次にアクティブになるitemを探してforegroundクラスを付与
        if (nextHandle && nextHandle !== 0) {
          const nextItem = taskBarItems.find(t => t.handle === nextHandle);
          if (nextItem) {
            const nextElementSelector = `.task-item[data-handle="${nextItem.handle}"]`;
            const nextElement = document.querySelector(nextElementSelector);
            if (nextElement) {
              nextElement.classList.add('foreground');
            }
          }
        }

        // ウィンドウを最小化
        sendMessageToHost('minimize_window', { handle: task.handle });
      } else {
        // ウィンドウをアクティブにする
        sendMessageToHost('activate_window', { handle: task.handle });

        // クリックされたアイテムに foreground クラスを追加
        item.classList.add('foreground');
      }
    }
  } catch (error) {
    console.error('Error in onClick:', error);
  }
}

// タスクアイテムの中クリック処理
function onMouseDown(item: HTMLElement, task: TaskBarItem, e: MouseEvent): void {
  if (e.button === 1) {
    // 中クリック
    e.preventDefault();
    sendMessageToHost('task_middle_click', {
      handle: task.handle,
      moduleFileName: task.moduleFileName
    });
  }
}

function onDragStart(item: HTMLElement, task: TaskBarItem, e: DragEvent): void {
  draggedTask = task;
  draggedElement = item;
  item.classList.add('dragging');

  // ドラッグデータを設定
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', String(task.handle));
  }

  // 少し遅延してスタイルを適用（ドラッグ画像に影響しないよう）
  setTimeout(() => {
    item.style.opacity = '0.5';
  }, 0);
}

function onDragEnd(item: HTMLElement): void {
  item.classList.remove('dragging');
  item.style.opacity = '';

  // 全ての要素からdrag-overクラスを除去
  document.querySelectorAll('.task-item').forEach(el => {
    el.classList.remove('drag-over-above', 'drag-over-below');
  });

  draggedTask = null;
  draggedElement = null;
}

function onDragOver(item: HTMLElement, e: DragEvent): void {
  if (draggedElement && draggedElement !== item) {
    e.preventDefault();
    if (e.dataTransfer) {
      e.dataTransfer.dropEffect = 'move';
    }

    const draggedModuleName = draggedElement.dataset.moduleFileName!;
    const targetModuleName = item.dataset.moduleFileName!;

    // 異なるアプリケーション
    if (draggedModuleName !== targetModuleName) {
      const mouseY = e.clientY;
      const elements = getItems(targetModuleName);

      if (!elements) {
        return;
      }

      const { firstElement, lastElement } = elements;

      const isAbove = isDropAboveApplicationGroup(targetModuleName, mouseY);
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

function onDragEnter(e: DragEvent): void {
  if (draggedElement) {
    e.preventDefault();
    // dragoverで位置判定を行うためここでは何もしない
  }
}

function onDragLeave(item: HTMLElement, e: DragEvent): void {
  // 子要素に移動した場合は除外
  if (!item.contains(e.relatedTarget as Node)) {
    item.classList.remove('drag-over-above', 'drag-over-below');
  }
}

function onDrop(item: HTMLElement, e: DragEvent): void {
  e.preventDefault();
  item.classList.remove('drag-over-above', 'drag-over-below');

  if (draggedTask && draggedElement && draggedElement !== item) {
    const draggedModuleName = draggedElement.dataset.moduleFileName!;
    const targetModuleName = item.dataset.moduleFileName!;

    let differentApplication = draggedModuleName !== targetModuleName;

    if (draggedElement.dataset.windowId !== undefined && item.dataset.windowId !== undefined) {
      if (parseInt(draggedElement.dataset.windowId, 10) !== parseInt(item.dataset.windowId, 10)) {
        differentApplication = true;
      }
    }

    // 異なるアプリケーション
    if (differentApplication) {
      const mouseY = e.clientY;
      const elements = getItems(targetModuleName);

      if (!elements) {
        return;
      }

      const { firstElement, lastElement } = elements;

      const isAbove = isDropAboveApplicationGroup(targetModuleName, mouseY);
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

        const dropTargetTask = {
          handle: parseInt((isAbove ? firstElement : lastElement).dataset.handle!, 10),
          tabId: parseInt((isAbove ? firstElement : lastElement).dataset.tabId || '0', 10),
          windowId: parseInt((isAbove ? firstElement : lastElement).dataset.windowId || '0', 10),
        };

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
        handle: parseInt(item.dataset.handle!, 10),
        tabId: parseInt(item.dataset.tabId || '0', 10),
        windowId: parseInt(item.dataset.windowId || '0', 10),
      };

      // タスクの順序を変更
      reorderTasks(draggedTask, dropTargetTask, isAbove);
    }
  }
}

// タスクの順序を変更（同種アプリケーションの一括移動対応）
function reorderTasks(
  draggedTask: TaskBarItem,
  targetTask: { handle: number; tabId: number; windowId: number },
  dropAbove: boolean
): void {
  // targetTaskがdatasetオブジェクトの場合とtaskオブジェクトの場合を考慮
  const targetIndex = taskBarItems.findIndex(t => t.handle === targetTask.handle);
  const draggedIndex = taskBarItems.findIndex(t => t.handle === draggedTask.handle);

  if (targetIndex === -1 || draggedIndex === -1 || draggedIndex === targetIndex) {
    return;
  }

  const targetTaskObj = taskBarItems[targetIndex];
  const draggedTaskObj = taskBarItems[draggedIndex];

  // 同種アプリケーション（同じmoduleFileName）のタスクを全て取得
  const draggedModuleName = draggedTaskObj.moduleFileName;
  const targetModuleName = targetTaskObj.moduleFileName;

  let differentApplication = draggedModuleName !== targetModuleName;
  if (draggedTaskObj.windowId !== targetTaskObj.windowId) {
    differentApplication = true;
  }

  // 異なるアプリケーション間での移動の場合のみ一括移動を実行
  if (differentApplication) {
    reorderTasksWithSameApp(draggedTaskObj.handle, targetTaskObj.handle, dropAbove);
  } else {
    // 同じアプリケーション内での移動は従来通り
    reorderSingleTask(draggedTaskObj.handle, targetTaskObj.handle, dropAbove);
  }

  window.applicationOrder.updateOrderFromList(taskBarItems.map(task => task.moduleFileName));
  window.applicationOrder.updateWindowOrder(
    taskBarItems.map(task => ({ handle: task.handle, moduleFileName: task.moduleFileName }))
  );

  // UIを更新
  updateTaskListOrder();
}

// 同種アプリケーションの一括移動
function reorderTasksWithSameApp(draggedHandle: number, targetHandle: number, dropAbove: boolean): void {
  const draggedIndex = taskBarItems.findIndex(t => t.handle === draggedHandle);
  const draggedTask = taskBarItems[draggedIndex];

  // 同じmoduleFileNameを持つタスクを全て取得（元の順序を保持）
  const sameAppTasks = taskBarItems.filter(task => task.moduleFileName === draggedTask.moduleFileName);

  // 他のアプリケーションのタスクだけを残したリストを作成
  const otherTasks = taskBarItems.filter(task => task.moduleFileName !== draggedTask.moduleFileName);

  // ターゲットタスクの新しい位置を計算
  const targetNewIndex = otherTasks.findIndex(t => t.handle === targetHandle);

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

  // taskBarItemsを更新
  taskBarItems = newTasks;
}

// 単一タスクの移動（インデックスベース、Chromeタブ用）
function reorderSingleTaskByIndex(draggedIndex: number, targetIndex: number, dropAbove: boolean): void {
  if (draggedIndex === -1 || targetIndex === -1) {
    return;
  }

  // 配列から要素を取り出し
  const draggedTask = taskBarItems.splice(draggedIndex, 1)[0];

  // 新しいインデックスを計算
  let newIndex = targetIndex;

  // ドラッグ元がターゲットより前にあった場合、ターゲットのインデックスが1つ減る
  if (draggedIndex < targetIndex) {
    newIndex = targetIndex - 1;
  }

  // ドロップ位置に応じてインデックスを調整
  if (dropAbove) {
    // 要素の上にドロップする場合はそのまま
    taskBarItems.splice(newIndex, 0, draggedTask);
  } else {
    // 要素の下にドロップする場合は+1
    taskBarItems.splice(newIndex + 1, 0, draggedTask);
  }
}

// 単一タスクの移動（同じアプリケーション内での移動用）
function reorderSingleTask(draggedHandle: number, targetHandle: number, dropAbove: boolean): void {
  // この関数は既にreorderTasks内でdraggedIndexとtargetIndexが計算されているため、
  // 再度検索する必要があります（グローバル変数を使わないため）
  const draggedIndex = taskBarItems.findIndex(t => t.handle === draggedHandle);
  const targetIndex = taskBarItems.findIndex(t => t.handle === targetHandle);

  reorderSingleTaskByIndex(draggedIndex, targetIndex, dropAbove);
}

// タスク要素の内容を更新（変更がある場合のみ）
function updateTaskItemContent(item: HTMLElement, task: TaskBarItem): void {
  // クリック後FOREGROUND_UPDATE_SKIP_DURATION以内は全タスクのforeground更新をスキップ
  // これがないと定期的な通信によって上書きされて、アクティブなウィンドウのタスクバーがちらつきます。
  const timeSinceClick = Date.now() - lastClickTime;
  const shouldSkipForegroundUpdate = timeSinceClick < FOREGROUND_UPDATE_SKIP_DURATION;
  if (!shouldSkipForegroundUpdate) {
    const hasForeground = item.classList.contains('foreground');
    if (hasForeground !== task.isForeground) {
      if (task.isForeground) {
        item.classList.add('foreground');
      } else {
        item.classList.remove('foreground');
      }
    }
  }

  // タイトルの更新
  const textElement = item.querySelector('.task-text');
  if (textElement && textElement.textContent !== task.title) {
    textElement.textContent = task.title || 'Unknown';
    (textElement as HTMLElement).title = task.title || 'Unknown';
  }

  // アイコンの更新（iconDataが変わった場合）
  const iconElement = item.querySelector('.task-icon');
  if (iconElement) {
    // メインアイコン（iconData）の更新
    const imgElement = iconElement.querySelector('img:not(.fav-icon img)') as HTMLImageElement;
    if (imgElement) {
      const currentIconSrc = imgElement.src;
      if (currentIconSrc !== task.iconData && task.iconData) {
        imgElement.src = task.iconData;
      }
    }
  }
}

// アプリケーショングループ全体でのマウス位置判定
function isDropAboveApplicationGroup(targetModuleName: string, mouseY: number): boolean | null {
  const elements = getItems(targetModuleName);

  if (!elements) {
    return null;
  }

  const { firstElement, lastElement } = elements;

  // グループ全体の境界を計算
  const firstRect = firstElement.getBoundingClientRect();
  const lastRect = lastElement.getBoundingClientRect();

  const groupTop = firstRect.top;
  const groupBottom = lastRect.bottom;
  const groupCenter = groupTop + (groupBottom - groupTop) / 2;

  return mouseY < groupCenter;
}

// アプリケーショングループの開始・終了インデックスを取得
function getItems(targetModuleName: string): { firstElement: HTMLElement; lastElement: HTMLElement } | null {
  const range = getApplicationGroupRange(targetModuleName);

  if (range.startIndex === -1 || range.endIndex === -1) {
    return null;
  }

  const { startIndex, endIndex } = range;

  // グループ内のすべての要素を取得
  const taskElements = document.querySelectorAll('.task-item');
  const firstElement = taskElements[startIndex] as HTMLElement;
  const lastElement = taskElements[endIndex] as HTMLElement;

  if (!firstElement || !lastElement) {
    return null;
  }

  return { firstElement, lastElement };
}

// アプリケーショングループの開始・終了インデックスを取得
function getApplicationGroupRange(targetModuleName: string): { startIndex: number; endIndex: number } {
  let startIndex = -1;
  let endIndex = -1;

  for (let i = 0; i < taskBarItems.length; i++) {
    if (taskBarItems[i].moduleFileName === targetModuleName) {
      if (startIndex === -1) {
        startIndex = i;
      }
      endIndex = i;
    }
  }

  return { startIndex, endIndex };
}

export function startTaskbar(): void {
  start();
}

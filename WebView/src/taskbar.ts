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
    // C#側にWin32 API由来のウィンドウ状態一覧を要求
    const windowSnapshot = await requestWindowSnapshot();
    const taskBarWindowHandles = windowSnapshot
      .filter(item => item.isTaskBarWindow && item.isOnCurrentVirtualDesktop)
      .map(item => item.handle);
    const taskBarWindows = await requestTaskBarItems(taskBarWindowHandles);

    // タスクバーウィンドウの更新
    updateTaskBarWindows(taskBarWindows);

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

type WindowSnapshotItem = {
  handle: number;
  isTaskBarWindow: boolean;
  isOnCurrentVirtualDesktop: boolean;
};

// C#側にWin32 API由来のウィンドウ状態一覧を要求
async function requestWindowSnapshot(): Promise<WindowSnapshotItem[]> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestWindowSnapshot timeout')), 1000);

    // レスポンス受信用の一時的なリスナー
    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'window_snapshot_response') {
          clearTimeout(timeout);
          window.chrome!.webview!.removeEventListener('message', responseHandler);
          resolve(data.items as WindowSnapshotItem[]);
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    // イベントリスナー追加
    window.chrome!.webview!.addEventListener('message', responseHandler);

    sendMessageToHost('request_window_snapshot');
  });
}

// C#側に表示対象ウィンドウの詳細情報をまとめて要求
async function requestTaskBarItems(windowHandles: number[]): Promise<TaskBarItem[]> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestTaskBarItems timeout')), 1000);

    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'taskbar_items_response') {
          clearTimeout(timeout);
          window.chrome!.webview!.removeEventListener('message', responseHandler);
          resolve(data.items as TaskBarItem[]);
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    window.chrome!.webview!.addEventListener('message', responseHandler);
    sendMessageToHost('request_taskbar_items', { windowHandles });
  });
}

// タスクバーウィンドウの更新処理
function updateTaskBarWindows(nextTaskBarItems: TaskBarItem[]): void {
  try {
    // 現在表示対象ではないタスクバーを削除
    const nextHandles = new Set(nextTaskBarItems.map(item => item.handle));
    taskBarItems = taskBarItems.filter(item => {
      return nextHandles.has(item.handle);
    });

    // 各タスクバーウィンドウを処理
    for (const taskBarItem of nextTaskBarItems) {
      const index = taskBarItems.findIndex(item => item.handle === taskBarItem.handle);
      if (index >= 0) {
        taskBarItems[index] = taskBarItem;
      } else {
        // 新しいアイテム
        taskBarItems.push(taskBarItem);
      }
    }
  } catch (error) {
    console.error('Error in updateTaskBarWindows:', error);
  }
}

type TaskClickResponse = {
  handle: number;
  action: 'activate' | 'restore' | 'minimize';
  foregroundHandle: number;
};

async function requestTaskClick(handle: number): Promise<TaskClickResponse> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error('requestTaskClick timeout')), 1000);

    const responseHandler = (event: MessageEvent) => {
      try {
        let data: MessageData;
        if (typeof event.data === 'string') {
          data = JSON.parse(event.data);
        } else {
          data = event.data;
        }

        if (data && data.type === 'task_click_response' && data.handle === handle) {
          clearTimeout(timeout);
          window.chrome!.webview!.removeEventListener('message', responseHandler);
          resolve(data as unknown as TaskClickResponse);
        }
      } catch (error) {
        clearTimeout(timeout);
        window.chrome!.webview!.removeEventListener('message', responseHandler);
        reject(error);
      }
    };

    window.chrome!.webview!.addEventListener('message', responseHandler);
    sendMessageToHost('task_click', { handle });
  });
}

// タスクリストの順序のみを更新
function updateTaskListOrder(): void {
  // タスクバー一覧をアプリケーション名でトポロジカルソートする
  taskBarItems = window.applicationOrder.sortByRelations(
    taskBarItems,
    (task) => task.sortKey,
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
      item.dataset.sortKey = task.sortKey;
      item.dataset.moduleFileName = task.moduleFileName;
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
  item.dataset.sortKey = task.sortKey;
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

  // 中クリックでプロセス終了
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
async function onClick(item: HTMLElement, task: TaskBarItem, _e: MouseEvent): Promise<void> {
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
    const response = await requestTaskClick(task.handle);
    const foregroundHandle = response.foregroundHandle;
    if (foregroundHandle && foregroundHandle !== 0) {
      const foregroundElement = document.querySelector(`.task-item[data-handle="${foregroundHandle}"]`);
      foregroundElement?.classList.add('foreground');
    }
  } catch (error) {
    console.error('Error in onClick:', error);
  }
}

// タスクアイテムの中クリック処理
function onMouseDown(_item: HTMLElement, task: TaskBarItem, e: MouseEvent): void {
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

    const draggedSortKey = draggedElement.dataset.sortKey!;
    const targetSortKey = item.dataset.sortKey!;

    // 異なるアプリケーション
    if (draggedSortKey !== targetSortKey) {
      const mouseY = e.clientY;
      const elements = getItems(targetSortKey);

      if (!elements) {
        return;
      }

      const { firstElement, lastElement } = elements;

      const isAbove = isDropAboveApplicationGroup(targetSortKey, mouseY);
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
    const draggedSortKey = draggedElement.dataset.sortKey!;
    const targetSortKey = item.dataset.sortKey!;

    let differentApplication = draggedSortKey !== targetSortKey;

    if (draggedElement.dataset.windowId !== undefined && item.dataset.windowId !== undefined) {
      if (parseInt(draggedElement.dataset.windowId, 10) !== parseInt(item.dataset.windowId, 10)) {
        differentApplication = true;
      }
    }

    // 異なるアプリケーション
    if (differentApplication) {
      const mouseY = e.clientY;
      const elements = getItems(targetSortKey);

      if (!elements) {
        return;
      }

      const { firstElement, lastElement } = elements;

      const isAbove = isDropAboveApplicationGroup(targetSortKey, mouseY);
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
  targetTask: { handle: number; windowId: number },
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
  const draggedSortKey = draggedTaskObj.sortKey;
  const targetSortKey = targetTaskObj.sortKey;

  let differentApplication = draggedSortKey !== targetSortKey;
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

  window.applicationOrder.updateOrderFromList(taskBarItems.map(task => task.sortKey));
  window.applicationOrder.updateWindowOrder(
    taskBarItems.map(task => ({ handle: task.handle, applicationKey: task.sortKey }))
  );

  // UIを更新
  updateTaskListOrder();
}

// 同種アプリケーションの一括移動
function reorderTasksWithSameApp(draggedHandle: number, targetHandle: number, dropAbove: boolean): void {
  const draggedIndex = taskBarItems.findIndex(t => t.handle === draggedHandle);
  const draggedTask = taskBarItems[draggedIndex];

  // 同じmoduleFileNameを持つタスクを全て取得（元の順序を保持）
  const sameAppTasks = taskBarItems.filter(task => task.sortKey === draggedTask.sortKey);

  // 他のアプリケーションのタスクだけを残したリストを作成
  const otherTasks = taskBarItems.filter(task => task.sortKey !== draggedTask.sortKey);

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

// 単一タスクの移動（インデックスベース）
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
    const imgElement = iconElement.querySelector('img') as HTMLImageElement;
    if (imgElement) {
      const currentIconSrc = imgElement.src;
      if (currentIconSrc !== task.iconData && task.iconData) {
        imgElement.src = task.iconData;
      }
    }
  }
}

// アプリケーショングループ全体でのマウス位置判定
function isDropAboveApplicationGroup(targetSortKey: string, mouseY: number): boolean | null {
  const elements = getItems(targetSortKey);

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
function getItems(targetSortKey: string): { firstElement: HTMLElement; lastElement: HTMLElement } | null {
  const range = getApplicationGroupRange(targetSortKey);

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
function getApplicationGroupRange(targetSortKey: string): { startIndex: number; endIndex: number } {
  let startIndex = -1;
  let endIndex = -1;

  for (let i = 0; i < taskBarItems.length; i++) {
    if (taskBarItems[i].sortKey === targetSortKey) {
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

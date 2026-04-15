import { sendMessageToHost } from './network';

// Exit機能
function exitApplication(): void {
  sendMessageToHost('exit_application');
}

// タスクマネージャーを開く機能
function openTaskManager(): void {
  sendMessageToHost('open_task_manager');
}

// 保存データフォルダを開く機能
function openAppDataFolder(): void {
  sendMessageToHost('open_app_data_folder');
}

// 開発者ツールを開く機能
function openDevTools(): void {
  sendMessageToHost('open_dev_tools');
}

function isTaskListArea(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && target.closest('#taskList') !== null;
}

// コンテキストメニューの制御
export function setupContextMenu(): void {
  const contextMenu = document.getElementById('contextMenu');
  if (!contextMenu) {
    return;
  }
  const viewportPadding = 8;

  contextMenu.addEventListener('click', (e) => {
    e.stopPropagation();

    const item = (e.target as HTMLElement | null)?.closest('.context-menu-item') as HTMLElement | null;
    if (!item) {
      return;
    }

    const action = item.dataset.action;
    switch (action) {
      case 'openTaskManager':
        openTaskManager();
        break;
      case 'openAppDataFolder':
        openAppDataFolder();
        break;
      case 'openDevTools':
        openDevTools();
        break;
      case 'exitApplication':
        exitApplication();
        break;
      default:
        return;
    }

    contextMenu.style.display = 'none';
  });

  document.addEventListener('contextmenu', (e) => {
    if (!isTaskListArea(e.target)) {
      return;
    }

    e.preventDefault();
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;

    // コンテキストメニューを一時的に表示して実サイズを取得
    contextMenu.style.left = '0px';
    contextMenu.style.top = '0px';
    contextMenu.style.width = 'max-content';
    contextMenu.style.maxWidth = Math.max(120, viewportWidth - viewportPadding * 2) + 'px';
    contextMenu.style.visibility = 'hidden';
    contextMenu.style.display = 'block';
    const menuRect = contextMenu.getBoundingClientRect();

    const maxLeft = Math.max(viewportPadding, viewportWidth - menuRect.width - viewportPadding);
    const maxTop = Math.max(viewportPadding, viewportHeight - menuRect.height - viewportPadding);

    let left = e.clientX;
    if (left + menuRect.width > viewportWidth - viewportPadding) {
      left = viewportWidth - menuRect.width - viewportPadding;
    }

    let top = e.clientY;
    if (top + menuRect.height > viewportHeight - viewportPadding) {
      top = viewportHeight - menuRect.height - viewportPadding;
    }

    left = Math.max(viewportPadding, Math.min(left, maxLeft));
    top = Math.max(viewportPadding, Math.min(top, maxTop));

    contextMenu.style.left = left + 'px';
    contextMenu.style.top = top + 'px';
    contextMenu.style.visibility = 'visible';

    // 実際の描画結果を見て、はみ出した分を再補正する
    const renderedRect = contextMenu.getBoundingClientRect();
    let correctedLeft = left;
    let correctedTop = top;

    if (renderedRect.left < viewportPadding) {
      correctedLeft += viewportPadding - renderedRect.left;
    } else if (renderedRect.right > viewportWidth - viewportPadding) {
      correctedLeft -= renderedRect.right - (viewportWidth - viewportPadding);
    }

    if (renderedRect.top < viewportPadding) {
      correctedTop += viewportPadding - renderedRect.top;
    } else if (renderedRect.bottom > viewportHeight - viewportPadding) {
      correctedTop -= renderedRect.bottom - (viewportHeight - viewportPadding);
    }

    const finalRect = contextMenu.getBoundingClientRect();
    const finalMaxLeft = Math.max(viewportPadding, viewportWidth - finalRect.width - viewportPadding);
    const finalMaxTop = Math.max(viewportPadding, viewportHeight - finalRect.height - viewportPadding);
    correctedLeft = Math.max(viewportPadding, Math.min(correctedLeft, finalMaxLeft));
    correctedTop = Math.max(viewportPadding, Math.min(correctedTop, finalMaxTop));

    if (correctedLeft !== left || correctedTop !== top) {
      contextMenu.style.left = correctedLeft + 'px';
      contextMenu.style.top = correctedTop + 'px';
    }
  });

  document.addEventListener('click', () => {
    const contextMenu = document.getElementById('contextMenu');
    if (contextMenu) {
      contextMenu.style.display = 'none';
    }
  });
}

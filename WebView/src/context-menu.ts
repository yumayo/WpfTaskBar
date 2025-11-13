import { sendMessageToHost } from './network';

// Exit機能
function exitApplication(): void {
  sendMessageToHost('exit_application');
}

// 開発者ツールを開く機能
function openDevTools(): void {
  sendMessageToHost('open_dev_tools');
}

// コンテキストメニューの制御
export function setupContextMenu(): void {
  document.addEventListener('contextmenu', (e) => {
    if ((e.target as HTMLElement).closest('.task-item')) {
      return;
    }

    e.preventDefault();
    const contextMenu = document.getElementById('contextMenu');

    if (!contextMenu) return;

    // コンテキストメニューを一時的に表示してサイズを取得
    contextMenu.style.visibility = 'hidden';
    contextMenu.style.display = 'block';
    const menuRect = contextMenu.getBoundingClientRect();

    // ウィンドウの幅と高さを取得
    const windowWidth = window.innerWidth;
    const windowHeight = window.innerHeight;

    // 右側にはみ出す場合は左側に表示
    let left = e.clientX;
    if (left + menuRect.width > windowWidth) {
      left = e.clientX - menuRect.width;
    }

    // 下側にはみ出す場合は上側に表示
    let top = e.clientY;
    if (top + menuRect.height > windowHeight) {
      top = e.clientY - menuRect.height;
    }

    contextMenu.style.left = left + 'px';
    contextMenu.style.top = top + 'px';
    contextMenu.style.visibility = 'visible';
  });

  document.addEventListener('click', () => {
    const contextMenu = document.getElementById('contextMenu');
    if (contextMenu) {
      contextMenu.style.display = 'none';
    }
  });

  // グローバル関数として公開（HTMLから呼ばれるため）
  (window as unknown as { exitApplication: () => void; openDevTools: () => void }).exitApplication = exitApplication;
  (window as unknown as { exitApplication: () => void; openDevTools: () => void }).openDevTools = openDevTools;
}

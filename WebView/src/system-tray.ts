import type { PinnedTab, MessageData } from './types';

let pinnedTabs: PinnedTab[] = [];

// ピン留めされたタブを更新
function updatePinnedTabs(tabsData: PinnedTab[]): void {
  pinnedTabs = tabsData || [];
  renderSystemTray();
}

// システムトレイをレンダリング
function renderSystemTray(): void {
  const systemTray = document.getElementById('systemTray');
  if (!systemTray) return;

  systemTray.classList.add('visible');

  // ピン留めされたタブのアイコンを表示
  if (pinnedTabs.length > 0) {
    let pinnedTabsContainer = systemTray.querySelector('.pinned-tabs-container') as HTMLElement;

    if (!pinnedTabsContainer) {
      pinnedTabsContainer = document.createElement('div');
      pinnedTabsContainer.className = 'pinned-tabs-container';
      systemTray.appendChild(pinnedTabsContainer);
    }

    // 既存のタブIDを取得
    const existingTabIds = new Set(
      Array.from(pinnedTabsContainer.children).map(child => (child as HTMLElement).dataset.tabId)
    );

    // 新しいタブIDのセット
    const newTabIds = new Set(pinnedTabs.map(tab => String(tab.tabId)));

    // 不要なタブアイコンを削除
    Array.from(pinnedTabsContainer.children).forEach(child => {
      if (!newTabIds.has((child as HTMLElement).dataset.tabId)) {
        pinnedTabsContainer.removeChild(child);
      }
    });

    // tab.indexでソートしてからタブアイコンを更新または追加
    const sortedTabs = [...pinnedTabs].sort((a, b) => (a.index || 0) - (b.index || 0));
    sortedTabs.forEach((tab, index) => {
      const tabId = String(tab.tabId);
      let tabIcon = pinnedTabsContainer.querySelector(`[data-tab-id="${tabId}"]`) as HTMLElement;

      if (tabIcon) {
        // 既存のアイコンを更新
        updatePinnedTabIcon(tabIcon, tab);
      } else {
        // 新しいアイコンを作成
        tabIcon = createPinnedTabIcon(tab);
        pinnedTabsContainer.appendChild(tabIcon);
      }

      // 順序を調整
      const currentIndex = Array.from(pinnedTabsContainer.children).indexOf(tabIcon);
      if (currentIndex !== index) {
        if (index < pinnedTabsContainer.children.length) {
          pinnedTabsContainer.insertBefore(tabIcon, pinnedTabsContainer.children[index]);
        } else {
          pinnedTabsContainer.appendChild(tabIcon);
        }
      }
    });
  } else {
    // ピン留めされたタブがない場合はコンテナを削除
    const pinnedTabsContainer = systemTray.querySelector('.pinned-tabs-container');
    if (pinnedTabsContainer) {
      systemTray.removeChild(pinnedTabsContainer);
    }
  }
}

// ピン留めされたタブのアイコンを作成
function createPinnedTabIcon(tab: PinnedTab): HTMLElement {
  const icon = document.createElement('div');
  icon.className = 'pinned-tab-icon';
  icon.style.userSelect = 'none';
  icon.dataset.tabId = String(tab.tabId);
  icon.title = tab.title || 'Pinned Tab';

  const img = document.createElement('img');
  if (tab.favIconData) {
    img.src = tab.favIconData;
  } else if (tab.favIconUrl) {
    img.src = tab.favIconUrl;
  } else {
    img.src = 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect fill="%23999" width="16" height="16"/></svg>';
  }
  img.style.width = '24px';
  img.style.height = '24px';
  img.style.borderRadius = '4px';

  icon.appendChild(img);

  // 通知バッジを追加
  if (tab.hasNotification) {
    const badge = document.createElement('div');
    badge.className = 'notification-badge';
    icon.appendChild(badge);
  }

  // クリックイベントを追加
  icon.addEventListener('click', () => {
    activateTab(tab.tabId);
    clearNotification(tab.tabId);
  });

  return icon;
}

// タブをアクティブ化
function activateTab(tabId: number): void {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({
      type: 'activate_tab',
      tabId: tabId
    });
  }
}

// 通知をクリア
function clearNotification(tabId: number): void {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({
      type: 'clear_notification',
      tabId: tabId
    });
  }
}

// ピン留めされたタブのアイコンを更新
function updatePinnedTabIcon(icon: HTMLElement, tab: PinnedTab): void {
  icon.title = tab.title || 'Pinned Tab';

  const img = icon.querySelector('img');
  if (img) {
    let newSrc: string;
    if (tab.favIconData) {
      newSrc = tab.favIconData;
    } else if (tab.favIconUrl) {
      newSrc = tab.favIconUrl;
    } else {
      newSrc = 'data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect fill="%23999" width="16" height="16"/></svg>';
    }

    // srcが変更されている場合のみ更新（不要な再読み込みを避ける）
    if (img.src !== newSrc) {
      img.src = newSrc;
    }
  }

  // 通知バッジを更新
  const existingBadge = icon.querySelector('.notification-badge');
  if (tab.hasNotification && !existingBadge) {
    const badge = document.createElement('div');
    badge.className = 'notification-badge';
    icon.appendChild(badge);
  } else if (!tab.hasNotification && existingBadge) {
    icon.removeChild(existingBadge);
  }
}

// メッセージリスナーとタイマーの設定
export function setupSystemTray(): void {
  window.chrome?.webview?.addEventListener('message', function(event) {
    let data: MessageData;

    if (typeof event.data === 'string') {
      data = JSON.parse(event.data);
    } else {
      data = event.data;
    }

    if (!data) {
      return;
    }

    switch (data.type) {
      case 'pinned_tabs_response':
        updatePinnedTabs(data.tabs as PinnedTab[]);
        break;
      default:
        break;
    }
  });

  // ピン留めされたタブを定期的に更新（500ms間隔）
  setInterval(() => {
    window.chrome?.webview?.postMessage({
      type: 'request_pinned_tabs'
    });
  }, 500);
}

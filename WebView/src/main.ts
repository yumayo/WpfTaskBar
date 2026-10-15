import './style.css';
import { ApplicationOrder } from './application-order';
import { setupClockListeners } from './clock';
import { setupContextMenu } from './context-menu';
import { sendMessageToHost } from './network';
import { startTaskbar } from './taskbar';
import { setupTaskStatusListeners } from './task-status';

// WebView2 内のクリックは WPF に伝わらないため、余白も含めてホストへ通知する。
document.addEventListener('mousedown', () => {
  sendMessageToHost('ensure_taskbar_topmost');
}, true);

// 初期化処理を非同期関数でラップ
(async () => {
  // グローバルなApplicationOrderインスタンスを作成して公開
  const applicationOrder = new ApplicationOrder();
  await applicationOrder.setup();
  window.applicationOrder = applicationOrder;

  // 各モジュールの初期化
  setupTaskStatusListeners();
  setupClockListeners();
  setupContextMenu();
  startTaskbar();

  console.log('WebView initialized');
})();

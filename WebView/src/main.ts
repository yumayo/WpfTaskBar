import './style.css';
import { ApplicationOrder } from './application-order';
import { setupClockListeners } from './clock';
import { setupContextMenu } from './context-menu';
import { startTaskbar } from './taskbar';
import { setupTaskStatusListeners } from './task-status';

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

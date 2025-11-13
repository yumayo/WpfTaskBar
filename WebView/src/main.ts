import './style.css';
import { ApplicationOrder } from './application-order';
import { setupClockListeners } from './clock';
import { setupContextMenu } from './context-menu';
import { setupSystemTray } from './system-tray';
import { startTaskbar } from './taskbar';

// 初期化処理を非同期関数でラップ
(async () => {
  // グローバルなApplicationOrderインスタンスを作成して公開
  const applicationOrder = new ApplicationOrder();
  await applicationOrder.setup();
  window.applicationOrder = applicationOrder;

  // 各モジュールの初期化
  setupClockListeners();
  setupContextMenu();
  setupSystemTray();
  startTaskbar();

  console.log('WebView initialized');
})();

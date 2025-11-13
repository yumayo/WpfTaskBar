import type { MessageData } from './types';

// C#との通信用の関数
export function sendMessageToHost(type: string, data: unknown = null): void {
  if (window.chrome?.webview) {
    const message: MessageData = {
      type,
      data,
      timestamp: new Date().toISOString()
    };
    window.chrome.webview.postMessage(message);
  }
}

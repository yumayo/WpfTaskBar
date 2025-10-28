// WebSocket接続とメッセージング機能を処理するクラス

import { sanitizeMessage, validateMessageSize } from './sanitizer.js';

export class WebSocketClient {
    constructor(url = 'ws://127.0.0.1:5000/ws') {
        this.url = url;
        this.ws = null;
        this.isConnected = false;
        this.reconnectTimer = null;

        // コールバック関数を格納
        this.onConnectedCallback = null;
        this.onMessageCallback = null;
        this.onDisconnectedCallback = null;
        this.onErrorCallback = null;
    }

    // WebSocket接続を初期化
    initialize() {
        // 既存のWebSocketがあり、接続中または接続中の場合は何もしない
        if (this.ws && (this.ws.readyState === WebSocket.CONNECTING || this.ws.readyState === WebSocket.OPEN)) {
            return;
        }

        try {
            this.ws = new WebSocket(this.url);

            this.ws.onopen = () => {
                console.log('WebSocket connected to WpfTaskBar');
                this.isConnected = true;

                // 接続成功時に再接続タイマーをクリア
                if (this.reconnectTimer) {
                    clearTimeout(this.reconnectTimer);
                    this.reconnectTimer = null;
                }

                // 接続コールバックを実行
                if (this.onConnectedCallback) {
                    this.onConnectedCallback();
                }
            };

            this.ws.onmessage = (event) => {
                try {
                    const message = JSON.parse(event.data);
                    // メッセージコールバックを実行
                    if (this.onMessageCallback) {
                        this.onMessageCallback(message);
                    }
                } catch (error) {
                    console.error('Failed to parse WebSocket message:', error);
                }
            };

            this.ws.onclose = () => {
                console.log('WebSocket connection closed');
                this.isConnected = false;

                // 切断コールバックを実行
                if (this.onDisconnectedCallback) {
                    this.onDisconnectedCallback();
                }

                // 5秒後に再接続を試行
                this.reconnectTimer = setTimeout(() => {
                    console.log('Attempting to reconnect...');
                    this.initialize();
                }, 5000);
            };

            this.ws.onerror = (error) => {
                console.error('WebSocket error:', error);
                this.isConnected = false;

                // エラーコールバックを実行
                if (this.onErrorCallback) {
                    this.onErrorCallback(error);
                }
            };

        } catch (error) {
            console.error('Failed to initialize WebSocket:', error);
            // 5秒後に再試行
            this.reconnectTimer = setTimeout(() => this.initialize(), 5000);
        }
    }

    // WebSocketメッセージを送信
    sendMessage(message) {
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            try {
                // メッセージをサニタイズ
                const sanitized = sanitizeMessage(message);

                // メッセージサイズを検証
                if (!validateMessageSize(sanitized)) {
                    console.error('Message size exceeds limit, message not sent');
                    return;
                }

                const jsonMessage = JSON.stringify(sanitized);
                this.ws.send(jsonMessage);
            } catch (error) {
                console.error('Failed to sanitize or send message:', error, message);
            }
        } else {
            console.warn('WebSocket is not connected, message not sent:', message);
        }
    }

    // 接続状態を取得
    getConnectionStatus() {
        return this.isConnected;
    }

    // コールバック登録関数
    onConnected(callback) {
        this.onConnectedCallback = callback;
    }

    onMessage(callback) {
        this.onMessageCallback = callback;
    }

    onDisconnected(callback) {
        this.onDisconnectedCallback = callback;
    }

    onError(callback) {
        this.onErrorCallback = callback;
    }

    // WebSocket接続をクローズ
    close() {
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;
        }

        if (this.ws) {
            this.ws.close();
            this.ws = null;
        }

        this.isConnected = false;
    }
}



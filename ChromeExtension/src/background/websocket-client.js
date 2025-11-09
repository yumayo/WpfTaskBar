// WebSocketを使用した通信クライアント

export class WebSocketClient {
    constructor(baseUrl = 'ws://127.0.0.1:5000') {
        this.baseUrl = baseUrl;
        this.isConnected = false;
        this.reconnectTimer = null;
        this.webSocket = null;

        // コールバック関数を格納
        this.onConnectedCallback = null;
        this.onMessageCallback = null;
        this.onDisconnectedCallback = null;
        this.onErrorCallback = null;
    }

    // WebSocket接続を初期化
    async initialize() {

        if (this.webSocket) {
            this.close();
        }

        try {
            console.log('Connecting to WebSocket at /ws...');

            this.webSocket = new WebSocket(`${this.baseUrl}/ws`);

            // 接続成功イベント
            this.webSocket.onopen = () => {
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

            // メッセージ受信イベント
            this.webSocket.onmessage = (event) => {
                try {
                    const message = JSON.parse(event.data);

                    // メッセージコールバックを実行
                    if (this.onMessageCallback) {
                        this.onMessageCallback(message);
                    }
                } catch (error) {
                    console.error('Failed to parse message:', error, event.data);
                }
            };

            // 切断イベント
            this.webSocket.onclose = (event) => {
                console.log('WebSocket connection closed', event.code, event.reason);
                this.isConnected = false;
                this.webSocket = null;

                // 切断コールバックを実行
                if (this.onDisconnectedCallback) {
                    this.onDisconnectedCallback();
                }

                // 1秒後に再接続を試行
                this.reconnectTimer = setTimeout(() => {
                    console.log('Attempting to reconnect...');
                    this.initialize();
                }, 1000);
            };

            // エラーイベント
            this.webSocket.onerror = (error) => {
                console.error('WebSocket error:', error);

                // エラーコールバックを実行
                if (this.onErrorCallback) {
                    this.onErrorCallback(error);
                }
            };

        } catch (error) {
            console.error('Failed to initialize WebSocket:', error);
            this.isConnected = false;

            // エラーコールバックを実行
            if (this.onErrorCallback) {
                this.onErrorCallback(error);
            }

            // 1秒後に再試行
            this.reconnectTimer = setTimeout(() => this.initialize(), 1000);
        }
    }

    // サーバーにメッセージを送信
    async sendMessage(message) {
        if (!this.isConnected || !this.webSocket || this.webSocket.readyState !== WebSocket.OPEN) {
            console.warn('WebSocket is not connected, message not sent:', message);
            return;
        }

        try {
            const jsonMessage = JSON.stringify(message);

            console.log('Sending WebSocket Message:', message);

            this.webSocket.send(jsonMessage);

            console.log('Message sent successfully:', message);
        } catch (error) {
            console.error('Failed to send message:', error);
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

    // 接続をクローズ
    close() {
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;
        }

        if (this.webSocket) {
            this.webSocket.close();
            this.webSocket = null;
        }

        this.isConnected = false;
    }
}

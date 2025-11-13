// WebSocketを使用した通信クライアント

export class WebSocketClient {
    private baseUrl: string;
    private isConnected: boolean;
    private reconnectTimer: number | null;
    private webSocket: WebSocket | null;

    // コールバック関数を格納
    private onConnectedCallback: (() => void) | null;
    private onMessageCallback: ((message: any) => void) | null;
    private onDisconnectedCallback: (() => void) | null;
    private onErrorCallback: ((error: any) => void) | null;

    constructor(baseUrl: string = 'ws://127.0.0.1:5000') {
        this.baseUrl = baseUrl;
        this.isConnected = false;
        this.reconnectTimer = null;
        this.webSocket = null;

        this.onConnectedCallback = null;
        this.onMessageCallback = null;
        this.onDisconnectedCallback = null;
        this.onErrorCallback = null;
    }

    // WebSocket接続を初期化
    async initialize(): Promise<void> {
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
            this.webSocket.onmessage = (event: MessageEvent) => {
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
            this.webSocket.onclose = (event: CloseEvent) => {
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
                }, 1000) as unknown as number;
            };

            // エラーイベント
            this.webSocket.onerror = (error: Event) => {
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
            this.reconnectTimer = setTimeout(() => this.initialize(), 1000) as unknown as number;
        }
    }

    // サーバーにメッセージを送信
    async sendMessage(message: any, enableLog: boolean = true): Promise<void> {
        if (!this.isConnected || !this.webSocket || this.webSocket.readyState !== WebSocket.OPEN) {
            console.warn('WebSocket is not connected, message not sent:', message);
            return;
        }

        try {
            const jsonMessage = JSON.stringify(message);

            if (enableLog) {
                console.log('[WebSocketClient] Start sendMessage:', message);
            }

            this.webSocket.send(jsonMessage);

            if (enableLog) {
                console.log('[WebSocketClient] Finish sendMessage:', message);
            }
        } catch (error) {
            console.error('[WebSocketClient] Failed sendMessage:', error);
        }
    }

    // 接続状態を取得
    getConnectionStatus(): boolean {
        return this.isConnected;
    }

    // コールバック登録関数
    onConnected(callback: () => void): void {
        this.onConnectedCallback = callback;
    }

    onMessage(callback: (message: any) => void): void {
        this.onMessageCallback = callback;
    }

    onDisconnected(callback: () => void): void {
        this.onDisconnectedCallback = callback;
    }

    onError(callback: (error: any) => void): void {
        this.onErrorCallback = callback;
    }

    // 接続をクローズ
    close(): void {
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

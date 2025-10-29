// HTTP/2 Streamingを使用した通信クライアント

export class HttpStreamClient {
    constructor(baseUrl = 'http://127.0.0.1:5000') {
        this.baseUrl = baseUrl;
        this.connectionId = null;
        this.isConnected = false;
        this.reconnectTimer = null;
        this.abortController = null;
        this.reader = null;

        // コールバック関数を格納
        this.onConnectedCallback = null;
        this.onMessageCallback = null;
        this.onDisconnectedCallback = null;
        this.onErrorCallback = null;
    }

    // ストリーム接続を初期化
    async initialize() {
        // 既存の接続があれば閉じる
        if (this.abortController) {
            return;
        }

        try {
            this.abortController = new AbortController();

            console.log('Connecting to HTTP/2 stream...');

            const response = await fetch(`${this.baseUrl}/stream`, {
                method: 'GET',
                headers: {
                    'Accept': 'text/event-stream',
                },
                signal: this.abortController.signal
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            console.log('HTTP/2 stream connected to WpfTaskBar');
            this.isConnected = true;

            // 接続成功時に再接続タイマーをクリア
            if (this.reconnectTimer) {
                clearTimeout(this.reconnectTimer);
                this.reconnectTimer = null;
            }

            // ストリームを読み取る
            this.reader = response.body.getReader();
            const decoder = new TextDecoder();

            // 接続コールバックを実行
            if (this.onConnectedCallback) {
                this.onConnectedCallback();
            }

            // ストリームからデータを読み取る
            let buffer = '';

            try {
                while (true) {
                    const { done, value } = await this.reader.read();

                    if (done) {
                        console.log('Stream closed by server');
                        break;
                    }

                    // チャンクをデコード
                    buffer += decoder.decode(value, { stream: true });

                    // Server-Sent Eventsの形式でパース
                    const lines = buffer.split('\n');
                    buffer = lines.pop() || ''; // 最後の不完全な行を保持

                    for (const line of lines) {
                        if (line.startsWith('data: ')) {
                            const data = line.substring(6);
                            try {
                                const message = JSON.parse(data);

                                // connectionIdを保存
                                if (message.action === 'connected' && message.data?.connectionId) {
                                    this.connectionId = message.data.connectionId;
                                    console.log('Connection ID received:', this.connectionId);
                                }

                                // メッセージコールバックを実行
                                if (this.onMessageCallback) {
                                    this.onMessageCallback(message);
                                }
                            } catch (error) {
                                console.error('Failed to parse message:', error, data);
                            }
                        }
                    }
                }
            } catch (error) {
                if (error.name !== 'AbortError') {
                    console.error('Stream reading error:', error);
                    if (this.onErrorCallback) {
                        this.onErrorCallback(error);
                    }
                }
            }

            // ストリームが閉じられた
            this.isConnected = false;
            console.log('HTTP/2 stream connection closed');

            // 切断コールバックを実行
            if (this.onDisconnectedCallback) {
                this.onDisconnectedCallback();
            }

            // 5秒後に再接続を試行
            this.reconnectTimer = setTimeout(() => {
                console.log('Attempting to reconnect...');
                this.initialize();
            }, 5000);

        } catch (error) {
            console.error('Failed to initialize HTTP/2 stream:', error);
            this.isConnected = false;

            // エラーコールバックを実行
            if (this.onErrorCallback) {
                this.onErrorCallback(error);
            }

            // 5秒後に再試行
            this.reconnectTimer = setTimeout(() => this.initialize(), 5000);
        }
    }

    // サーバーにメッセージを送信
    async sendMessage(message) {
        if (!this.isConnected) {
            console.warn('HTTP/2 stream is not connected, message not sent:', message);
            return;
        }

        try {
            const jsonMessage = JSON.stringify(message);

            const headers = {
                'Content-Type': 'application/json',
            };

            // connectionIdがあればヘッダーに追加
            if (this.connectionId) {
                headers['X-Connection-Id'] = this.connectionId;
            }

            const response = await fetch(`${this.baseUrl}/message`, {
                method: 'POST',
                headers: headers,
                body: jsonMessage
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

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

        if (this.abortController) {
            this.abortController.abort();
            this.abortController = null;
        }

        if (this.reader) {
            this.reader.cancel();
            this.reader = null;
        }

        this.isConnected = false;
        this.connectionId = null;
    }
}

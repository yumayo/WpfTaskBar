// WebSocketメッセージのサニタイゼーションユーティリティ

// 文字列の最大長制限
const MAX_STRING_LENGTH = 10000;
const MAX_TITLE_LENGTH = 500;
const MAX_URL_LENGTH = 2048;
const MAX_MESSAGE_LENGTH = 1000;

/**
 * 制御文字を除去します (タブ、改行、キャリッジリターン以外)
 * @param {string} input - サニタイズする文字列
 * @returns {string} サニタイズされた文字列
 */
function removeControlCharacters(input) {
    if (!input || typeof input !== 'string') {
        return '';
    }

    // 制御文字 (0x00-0x1F, 0x7F) を除去、ただしタブ (0x09)、改行 (0x0A)、キャリッジリターン (0x0D) は保持
    return input.replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g, '');
}

/**
 * 文字列をサニタイズし、長さを制限します
 * @param {string} input - サニタイズする文字列
 * @param {number} maxLength - 最大長 (デフォルト: MAX_STRING_LENGTH)
 * @returns {string} サニタイズされた文字列
 */
export function sanitizeString(input, maxLength = MAX_STRING_LENGTH) {
    if (!input || typeof input !== 'string') {
        return '';
    }

    // 制御文字を除去
    let sanitized = removeControlCharacters(input);

    // 長さ制限
    if (sanitized.length > maxLength) {
        sanitized = sanitized.substring(0, maxLength);
    }

    return sanitized;
}

/**
 * TabInfo オブジェクトをサニタイズします
 * @param {Object} tabInfo - タブ情報オブジェクト
 * @returns {Object} サニタイズされたタブ情報
 */
export function sanitizeTabInfo(tabInfo) {
    if (!tabInfo || typeof tabInfo !== 'object') {
        throw new Error('Invalid tabInfo object');
    }

    return {
        tabId: parseInt(tabInfo.tabId) || 0,
        windowId: parseInt(tabInfo.windowId) || 0,
        url: sanitizeString(tabInfo.url || '', MAX_URL_LENGTH),
        title: sanitizeString(tabInfo.title || '', MAX_TITLE_LENGTH),
        faviconUrl: sanitizeString(tabInfo.faviconUrl || '', MAX_URL_LENGTH),
        isActive: Boolean(tabInfo.isActive),
        index: parseInt(tabInfo.index) || 0,
        lastActivity: tabInfo.lastActivity || new Date().toISOString()
    };
}

/**
 * NotificationData オブジェクトをサニタイズします
 * @param {Object} notification - 通知データオブジェクト
 * @returns {Object} サニタイズされた通知データ
 */
export function sanitizeNotification(notification) {
    if (!notification || typeof notification !== 'object') {
        throw new Error('Invalid notification object');
    }

    return {
        title: sanitizeString(notification.title || '', MAX_TITLE_LENGTH),
        message: sanitizeString(notification.message || '', MAX_MESSAGE_LENGTH),
        tabId: parseInt(notification.tabId) || 0,
        windowId: parseInt(notification.windowId) || 0,
        url: sanitizeString(notification.url || '', MAX_URL_LENGTH),
        tabTitle: sanitizeString(notification.tabTitle || '', MAX_TITLE_LENGTH),
        timestamp: notification.timestamp || new Date().toISOString()
    };
}

/**
 * WebSocketメッセージ全体をサニタイズします
 * @param {Object} message - WebSocketメッセージオブジェクト
 * @returns {Object} サニタイズされたメッセージ
 */
export function sanitizeMessage(message) {
    if (!message || typeof message !== 'object') {
        throw new Error('Invalid message object');
    }

    const sanitized = {
        action: sanitizeString(message.action || '', 50)
    };

    // actionに応じてdataをサニタイズ
    if (message.data) {
        switch (message.action) {
            case 'registerTab':
            case 'unregisterTab':
            case 'bindWindowHandle':
                sanitized.data = sanitizeTabInfo(message.data);
                break;

            case 'sendNotification':
                sanitized.data = sanitizeNotification(message.data);
                break;

            case 'updateTabs':
                if (Array.isArray(message.data.tabs)) {
                    sanitized.data = {
                        tabs: message.data.tabs.map(tab => sanitizeTabInfo(tab))
                    };
                } else {
                    sanitized.data = { tabs: [] };
                }
                break;

            case 'focusTab':
            case 'closeTab':
                sanitized.data = {
                    tabId: parseInt(message.data.tabId) || 0,
                    windowId: parseInt(message.data.windowId) || 0
                };
                break;

            default:
                // その他のactionではdataをそのまま保持
                sanitized.data = message.data;
                break;
        }
    } else {
        sanitized.data = {};
    }

    return sanitized;
}

/**
 * JSONメッセージのサイズを検証します
 * @param {Object} message - 検証するメッセージオブジェクト
 * @returns {boolean} サイズが許容範囲内の場合はtrue
 */
export function validateMessageSize(message) {
    // メッセージサイズの上限を設定 (100KB)
    const MAX_MESSAGE_SIZE = 100 * 1024;

    try {
        const jsonString = JSON.stringify(message);
        const byteSize = new TextEncoder().encode(jsonString).length;
        return byteSize <= MAX_MESSAGE_SIZE;
    } catch (error) {
        console.error('Failed to validate message size:', error);
        return false;
    }
}

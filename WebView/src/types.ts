// WebView2 API types
export interface WebView2API {
  postMessage(message: string | object): void;
  addEventListener(event: 'message', handler: (event: MessageEvent) => void): void;
  removeEventListener(event: 'message', handler: (event: MessageEvent) => void): void;
}

declare global {
  interface Window {
    chrome?: {
      webview?: WebView2API;
    };
    applicationOrder: ApplicationOrder;
  }
}

// Message types
export interface MessageData {
  type: string;
  data?: unknown;
  timestamp?: string;
  [key: string]: unknown;
}

// Task bar item
export interface TaskBarItem {
  handle: number;
  moduleFileName: string;
  title: string;
  isForeground: boolean;
  iconData: string | null;
  favIconData?: string | null;
  tabId?: number;
  windowId?: number;
  url?: string;
}

// Pinned tab
export interface PinnedTab {
  tabId: number;
  title: string;
  favIconUrl?: string;
  favIconData?: string;
  hasNotification: boolean;
  index?: number;
}

// Time record
export interface TimeRecord {
  clockInDate: Date | null;
  clockOutDate: Date | null;
}

// Application Order class interface
export interface ApplicationOrder {
  setup(): Promise<void>;
  updateOrderFromList(orderedPaths: string[]): void;
  updateWindowOrder(orderedWindows: Array<{ handle: number; moduleFileName: string }>): void;
  sortByRelations<T>(
    items: T[],
    getExecutablePath: (item: T) => string,
    getHandle?: ((item: T) => number) | null
  ): T[];
  topologicalSort(apps: string[]): string[];
  saveRelations(): Promise<void>;
  loadRelations(): Promise<void>;
  saveWindowOrder(): Promise<void>;
  loadWindowOrder(): Promise<void>;
  writeFile(filename: string, data: string): Promise<void>;
  readFile(filename: string): Promise<string>;
}

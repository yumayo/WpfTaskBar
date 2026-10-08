import * as vscode from 'vscode';
import { randomBytes } from 'node:crypto';
import { ApiError, TaskbarClient, isConnectionError, normalizeUrl, terminalEnvironment, type TaskWindow } from './client';

interface Binding {
  client: TaskbarClient;
  id: string;
  window: TaskWindow;
  title?: string;
  updatingTitle?: boolean;
  offline?: boolean;
}

let session: Binding | undefined;
let target: { apiUrl: string; window: TaskWindow; id: string } | undefined;
let notificationId: string;
let environment: vscode.GlobalEnvironmentVariableCollection;
let heartbeat: NodeJS.Timeout | undefined;
let titlePolling: NodeJS.Timeout | undefined;
let operation: Promise<void> = Promise.resolve();
let output: vscode.OutputChannel;
let stopping = false;
let waitingForConnection = false;
const connectionCheckInterval = 1000;

function describe(error: unknown): string { return error instanceof Error ? error.message : String(error); }

function reportError(error: unknown, notify = false): void {
  if (stopping) return;
  if (isConnectionError(error)) {
    if (!waitingForConnection) output.appendLine('WpfTaskBar の起動・接続を待っています。1秒間隔で再試行します。');
    waitingForConnection = true;
    return;
  }
  output.appendLine(describe(error));
  if (notify) void vscode.window.showErrorMessage(describe(error));
}

function updateEnvironment(containerUrl: string, id: string): void {
  const env = terminalEnvironment(containerUrl, id, process.env.WSLENV);
  for (const [name, value] of Object.entries(env)) {
    if (environment.get(name)?.value !== value) environment.replace(name, value);
  }
}

async function selectWindow(client: TaskbarClient, force: boolean): Promise<TaskWindow | undefined> {
  const windows = await client.windows();
  if (!force && target?.apiUrl === client.url) {
    const previous = target.window;
    const current = windows.find(window => window.handle === previous.handle && window.processId === previous.processId);
    if (!current) throw new Error('通知先の VSCode が見つかりません。「この VSCode の通知先を選択」で選び直してください。');
    return current;
  }
  if (windows.length === 0) throw new Error('WpfTaskBar に VSCode のタスクがありません。Windows 側で WpfTaskBar を起動してください。');
  // 起動時は先頭候補を使い、明示的に選び直すときだけ一覧を開く。
  return !force ? windows[0] : (await vscode.window.showQuickPick(
    windows.map(window => ({
      label: window.title,
      description: `HWND ${window.handle} / PID ${window.processId}`,
      window,
    })),
    { title: 'この VSCode ウィンドウのタスクを選択', placeHolder: 'タイトルが同じ場合は VSCode のウィンドウタイトルを区別してから選択してください。', ignoreFocusOut: true },
  ))?.window;
}

function configuration() {
  if (process.platform !== 'win32') throw new Error('この拡張は Windows 側の VSCode にインストールしてください（WSL 側ではありません）。');
  const config = vscode.workspace.getConfiguration('wpftaskbar');
  const apiUrl = normalizeUrl(config.get('apiUrl', 'http://127.0.0.1:5000'));
  return {
    client: new TaskbarClient(apiUrl),
    containerUrl: normalizeUrl(config.get('containerApiUrl', '') || apiUrl),
  };
}

async function removeBinding(binding: Binding): Promise<void> {
  await binding.client.remove(binding.id).catch(error => reportError(error));
}

function updateTerminalContext(): void {
  void vscode.commands.executeCommand('setContext', 'wpftaskbar.sessionActive',
    !!session && !!vscode.window.activeTerminal);
}

function expire(binding: Binding): void {
  if (session !== binding || stopping) return;
  session = undefined;
  updateTerminalContext();
  output.appendLine('WpfTaskBar の通知先を同じ ID で再登録します。');
}

async function connect(selected?: { apiUrl: string; window: TaskWindow }): Promise<void> {
  const { client, containerUrl } = configuration();
  if (selected && selected.apiUrl !== client.url) throw new Error('選択中に API の接続先が変更されました。通知先を選び直してください。');
  const window = selected?.window ?? (session?.client.url === client.url ? session.window : await selectWindow(client, false));
  if (!window || stopping) return;
  const previous = session;
  if (!session || session.client.url !== client.url || session.window.handle !== window.handle || session.window.processId !== window.processId) {
    const id = !target ? notificationId
      : target.apiUrl === client.url && target.window.handle === window.handle && target.window.processId === window.processId
        ? target.id : randomBytes(16).toString('hex');
    const created = await client.createSession(window, id);
    const binding = { client, id: created.sessionId, window };
    if (stopping) { await removeBinding(binding); return; }
    session = binding;
    target = { apiUrl: client.url, window, id };
    output.appendLine(`ウィンドウの通知先を登録: ${window.title} (HWND ${window.handle})`);
  }
  waitingForConnection = false;
  updateEnvironment(containerUrl, session.id);
  updateTerminalContext();
  void syncTitle();
  if (previous && previous !== session) await removeBinding(previous);
}

async function renewSession(): Promise<void> {
  const binding = session;
  if (binding) {
    try {
      await binding.client.renew(binding.id);
      binding.offline = false;
      waitingForConnection = false;
      void syncTitle();
    }
    catch (error) {
      if (isConnectionError(error)) binding.offline = true;
      if (!(error instanceof ApiError) || error.statusCode !== 404) throw error;
      expire(binding);
    }
  }
  if (!session && !stopping) await connect();
}

async function interruptTerminal(): Promise<void> {
  const binding = vscode.window.activeTerminal ? session : undefined;
  // AIへ先にEscを渡し、通知先が停止していても中断操作を遅らせない。
  await vscode.commands.executeCommand('workbench.action.terminal.sendSequence', { text: '\u001b' });
  if (binding) {
    try { await binding.client.interrupt(binding.id); }
    catch (error) {
      if (error instanceof ApiError && error.statusCode === 404) expire(binding);
      else {
        if (isConnectionError(error)) binding.offline = true;
        throw error;
      }
    }
  }
}

async function syncTitle(): Promise<void> {
  const binding = session;
  if (!binding || binding.offline || binding.updatingTitle || stopping) return;
  binding.updatingTitle = true;
  try {
    // アクティブなターミナルのタイトルをウィンドウ共通のセッションへ送る。
    while (!stopping && session === binding) {
      const title = (vscode.window.activeTerminal?.name || '').slice(0, 4096);
      if (title === binding.title) break;
      await binding.client.setTitle(binding.id, title);
      binding.title = title;
    }
  } catch (error) {
    if (session !== binding || stopping) return;
    if (error instanceof ApiError && error.statusCode === 404) expire(binding);
    else {
      if (isConnectionError(error)) binding.offline = true;
      reportError(error);
    }
  } finally { binding.updatingTitle = false; }
}

function enqueue(action: () => Promise<void>, notify = false): Promise<void> {
  // 起動・設定変更・生存通知が重なっても、ウィンドウの登録を重複させない。
  operation = operation.then(async () => {
    if (stopping) return;
    try { await action(); }
    catch (error) {
      reportError(error, notify);
    }
  });
  return operation;
}

function scheduleHeartbeat(): void {
  if (stopping) return;
  // 応答待ちや他の登録処理が長引いても、定期確認をキューへ積み上げない。
  heartbeat = setTimeout(() => {
    heartbeat = undefined;
    void enqueue(renewSession).finally(scheduleHeartbeat);
  }, connectionCheckInterval);
}

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  output = vscode.window.createOutputChannel('WpfTaskBar');
  environment = context.environmentVariableCollection;
  // 再読み込み後に失効したIDを新しいシェルへ注入しない。
  environment.persistent = false;
  environment.clear();
  environment.description = 'この VSCode ウィンドウの AI 通知先';
  // 接続前に ID を渡し、後から本体が起動しても既存のコンテナから通知できるようにする。
  notificationId = randomBytes(16).toString('hex');
  try { updateEnvironment(configuration().containerUrl, notificationId); }
  catch (error) { reportError(error); }
  const command = (name: string, action: () => unknown) => context.subscriptions.push(vscode.commands.registerCommand(name, async () => {
    try { await action(); }
    catch (error) {
      reportError(error, true);
    }
  }));
  command('wpftaskbar.selectWindow', async () => {
    // 選択画面を開いている間も生存通知を続ける。
    const { client } = configuration();
    const window = await selectWindow(client, true);
    if (window) await enqueue(() => connect({ apiUrl: client.url, window }), true);
  });
  command('wpftaskbar.showLog', () => output.show());
  command('wpftaskbar.interruptTerminal', interruptTerminal);
  context.subscriptions.push(output,
    vscode.window.onDidChangeActiveTerminal(() => { updateTerminalContext(); void syncTitle(); }),
    vscode.workspace.onDidChangeConfiguration(event => {
      if (event.affectsConfiguration('wpftaskbar')) void enqueue(() => connect(), true);
    }),
  );
  updateTerminalContext();
  // 安定版 VSCode API にタイトル変更専用イベントがないため、差分を1秒ごとに確認する。
  titlePolling = setInterval(() => { void syncTitle(); }, 1000);
  await enqueue(() => connect());
  scheduleHeartbeat();
}

export async function deactivate(): Promise<void> {
  stopping = true;
  clearTimeout(heartbeat);
  clearInterval(titlePolling);
  environment.clear();
  const binding = session;
  session = undefined;
  updateTerminalContext();
  await Promise.all([operation, binding ? removeBinding(binding) : Promise.resolve()]);
}

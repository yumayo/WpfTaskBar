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
let target: { apiUrl: string; window: TaskWindow } | undefined;
let deferredSelectionUrl: string | undefined;
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
  if (!force && deferredSelectionUrl === client.url) return undefined;
  const windows = await client.windows();
  if (!force && target?.apiUrl === client.url) {
    const previous = target.window;
    const current = windows.find(window => window.handle === previous.handle && window.processId === previous.processId);
    if (!current) throw new Error('通知先の VSCode が見つかりません。「この VSCode の通知先を選択」で選び直してください。');
    return current;
  }
  if (windows.length === 0) throw new Error('WpfTaskBar に VSCode のタスクがありません。Windows 側で WpfTaskBar を起動してください。');
  // 候補が1つでも、利用者が確認するまで通知先を登録しない。
  const selected = (await vscode.window.showQuickPick(
    windows.map(window => ({
      label: window.title,
      description: `HWND ${window.handle} / PID ${window.processId}`,
      window,
    })),
    { title: 'この VSCode ウィンドウのタスクを選択', placeHolder: 'タイトルが同じ場合は VSCode のウィンドウタイトルを区別してから選択してください。', ignoreFocusOut: true },
  ))?.window;
  deferredSelectionUrl = selected ? undefined : client.url;
  if (!selected) output.appendLine('通知先は未変更です。「この VSCode の通知先を選択」から選択できます。');
  return selected;
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
  // 本体側の紐付けを優先する。応答喪失やタスクバー側での変更後も古い HWND で上書きしない。
  const registered = await client.getSession(notificationId);
  const window = selected?.window ?? (registered
    ? (await client.windows()).find(item => item.handle === registered.handle && item.processId === registered.processId)
    : await selectWindow(client, false));
  if (!window || stopping) return;
  const previous = session;
  // 初回登録が通信に失敗しても、確認済みの選択と同じ ID で再試行する。
  if (!session) target = { apiUrl: client.url, window };
  const created = registered
    ? (selected && (registered.handle !== window.handle || registered.processId !== window.processId)
      ? await client.changeTarget(notificationId, window) : registered)
    : await client.createSession(window, notificationId);
  if (!registered || !session || session.client.url !== client.url || session.window.handle !== window.handle || session.window.processId !== window.processId) {
    const binding = { client, id: created.sessionId, window };
    if (stopping) { await removeBinding(binding); return; }
    session = binding;
    output.appendLine(`ウィンドウの通知先を登録: ${window.title} (HWND ${window.handle})`);
  }
  target = { apiUrl: client.url, window };
  waitingForConnection = false;
  updateEnvironment(containerUrl, notificationId);
  updateTerminalContext();
  void syncTitle();
  // 同じ API の紐付け変更では ID を削除しない。
  if (previous && previous.client.url !== client.url) await removeBinding(previous);
}

async function renewSession(): Promise<void> {
  const binding = session;
  if (binding) {
    try {
      const current = await binding.client.renew(binding.id);
      if (session !== binding || stopping) return;
      if (binding.window.handle !== current.handle || binding.window.processId !== current.processId) {
        binding.window = { ...binding.window, handle: current.handle, processId: current.processId };
        target = { apiUrl: binding.client.url, window: binding.window };
        output.appendLine(`タスクバー側で通知先を変更: HWND ${current.handle} / PID ${current.processId}（通知 ID は維持）`);
      }
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

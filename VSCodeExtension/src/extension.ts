import * as vscode from 'vscode';
import { ApiError, TaskbarClient, normalizeUrl, terminalEnvironment, type TaskWindow } from './client';

interface Binding {
  client: TaskbarClient;
  id: string;
}

interface PendingTerminal extends Binding {
  window: TaskWindow;
  createdAt: number;
  cancellation?: vscode.Disposable;
}

interface PreparedTerminal {
  id: string;
  options: vscode.TerminalOptions;
}

const terminals = new Map<vscode.Terminal, Binding>();
const pendingTerminals = new Map<string, PendingTerminal>();
let target: { apiUrl: string; window: TaskWindow } | undefined;
let heartbeat: NodeJS.Timeout | undefined;
let renewal: Promise<void> | undefined;
let output: vscode.OutputChannel;
let opening = false;
let stopping = false;
let preparing = 0;
let preparation: Promise<unknown> = Promise.resolve();

function describe(error: unknown): string { return error instanceof Error ? error.message : String(error); }

async function selectWindow(client: TaskbarClient, force = false, token?: vscode.CancellationToken): Promise<TaskWindow | undefined> {
  const windows = await client.windows();
  if (!force && target?.apiUrl === client.url) {
    const previous = target.window;
    const current = windows.find(window => window.handle === previous.handle && window.processId === previous.processId);
    if (current) return current;
  }
  if (windows.length === 0) throw new Error('WpfTaskBar に VSCode のタスクがありません。Windows 側で WpfTaskBar を起動してください。');
  const selected = windows.length === 1 && !force ? windows[0] : (await vscode.window.showQuickPick(
    windows.map(window => ({
      label: window.title,
      description: `HWND ${window.handle} / PID ${window.processId}`,
      window,
    })),
    { title: 'この VSCode ウィンドウのタスクを選択', placeHolder: 'タイトルが同じ場合は VSCode のウィンドウタイトルを区別してから選択してください。', ignoreFocusOut: true },
    token,
  ))?.window;
  if (selected) target = { apiUrl: client.url, window: selected };
  return selected;
}

function configuration() {
  if (process.platform !== 'win32') throw new Error('この拡張は Windows 側の VSCode にインストールしてください（WSL 側ではありません）。');
  const config = vscode.workspace.getConfiguration('wpftaskbar');
  const apiUrl = normalizeUrl(config.get('apiUrl', 'http://127.0.0.1:5000'));
  return {
    client: new TaskbarClient(apiUrl),
    containerUrl: normalizeUrl(config.get('containerApiUrl', '') || apiUrl),
    shellPath: config.get<string>('shellPath', '') || undefined,
    shellArgs: config.get<string[]>('shellArgs', []),
  };
}

async function removeBinding(binding: Binding): Promise<void> {
  await binding.client.remove(binding.id).catch(error => output.appendLine(`通知先の削除に失敗: ${describe(error)}`));
}

async function removePending(id: string): Promise<void> {
  const binding = pendingTerminals.get(id);
  if (!binding) return;
  pendingTerminals.delete(id);
  binding.cancellation?.dispose();
  await removeBinding(binding);
}

async function prepareTerminalOptions(token?: vscode.CancellationToken): Promise<PreparedTerminal | undefined> {
  if (stopping || token?.isCancellationRequested) return;
  const { client, containerUrl, shellPath, shellArgs } = configuration();
  const window = await selectWindow(client, false, token);
  if (!window || stopping || token?.isCancellationRequested) return;
  const session = await client.createSession(window);
  const binding: PendingTerminal = { client, id: session.sessionId, window, createdAt: Date.now() };
  if (stopping || token?.isCancellationRequested) {
    await removeBinding(binding);
    return;
  }
  pendingTerminals.set(binding.id, binding);
  binding.cancellation = token?.onCancellationRequested(() => { void removePending(binding.id); });
  return {
    id: binding.id,
    options: {
      name: 'AI (WpfTaskBar)',
      env: terminalEnvironment(containerUrl, binding.id, process.env.WSLENV),
      shellPath,
      shellArgs: shellPath || shellArgs.length > 0 ? shellArgs : undefined,
    },
  };
}

function prepareTerminal(token?: vscode.CancellationToken): Promise<PreparedTerminal | undefined> {
  // 同時起動でも通知先の選択を重ねず、要求ごとに独立したセッションを作る。
  preparing++;
  const result = preparation.then(() => prepareTerminalOptions(token));
  preparation = result.catch(() => {});
  return result.finally(() => { preparing--; });
}

function bindTerminal(terminal: vscode.Terminal): void {
  if (terminals.has(terminal)) return;
  const options = terminal.creationOptions;
  const id = 'env' in options ? options.env?.WPF_TASKBAR_SESSION_ID : undefined;
  const binding = id ? pendingTerminals.get(id) : undefined;
  if (!binding) return;
  pendingTerminals.delete(binding.id);
  binding.cancellation?.dispose();
  terminals.set(terminal, binding);
  output.appendLine(`通知付きターミナルを作成: ${binding.window.title} (HWND ${binding.window.handle})`);
}

async function openTerminal(): Promise<void> {
  if (opening) return;
  opening = true;
  try {
    const prepared = await prepareTerminal();
    if (!prepared || stopping) return;
    let terminal: vscode.Terminal;
    try {
      terminal = vscode.window.createTerminal(prepared.options);
    } catch (error) {
      await removePending(prepared.id);
      throw error;
    }
    bindTerminal(terminal);
    terminal.show();
  } finally { opening = false; }
}

async function renewSessions(): Promise<void> {
  // プロファイルを返した後のシェル起動失敗は API から通知されない。
  // 未接続の登録は延命せず、サーバーの有効期限 (2 分) に合わせて破棄する。
  await Promise.all([...pendingTerminals.values()]
    .filter(binding => Date.now() - binding.createdAt >= 120000)
    .map(binding => removePending(binding.id)));
  await Promise.all([...terminals].map(async ([terminal, binding]) => {
    try { await binding.client.renew(binding.id); }
    catch (error) {
      if (terminals.get(terminal) !== binding) return;
      output.appendLine(`通知先の更新に失敗: ${describe(error)}`);
      if (error instanceof ApiError && error.statusCode === 404) {
        terminals.delete(terminal);
        void vscode.window.showWarningMessage('WpfTaskBar の通知先が失効しました。AI 通知付きターミナルを開き直してください。');
      }
    }
  }));
}

export function activate(context: vscode.ExtensionContext): void {
  output = vscode.window.createOutputChannel('WpfTaskBar');
  context.subscriptions.push(output);
  const command = (name: string, action: () => unknown) => context.subscriptions.push(vscode.commands.registerCommand(name, async () => {
    try { await action(); }
    catch (error) {
      output.appendLine(describe(error));
      void vscode.window.showErrorMessage(describe(error));
    }
  }));
  context.subscriptions.push(vscode.window.registerTerminalProfileProvider('wpftaskbar.aiTerminal', {
    async provideTerminalProfile(token) {
      try {
        const prepared = await prepareTerminal(token);
        if (!prepared || stopping || token.isCancellationRequested) return;
        return new vscode.TerminalProfile(prepared.options);
      } catch (error) {
        output.appendLine(describe(error));
        void vscode.window.showErrorMessage(describe(error));
        return undefined;
      }
    },
  }));
  context.subscriptions.push(vscode.window.onDidOpenTerminal(bindTerminal));
  command('wpftaskbar.openTerminal', openTerminal);
  command('wpftaskbar.selectWindow', async () => {
    // 開いているターミナルの通知先を黙って変更しない。
    if (terminals.size > 0 || pendingTerminals.size > 0 || preparing > 0) {
      throw new Error('通知付きターミナルの作成中・使用中は通知先を変更できません。作成完了後にすべて閉じてください。');
    }
    await selectWindow(configuration().client, true);
  });
  command('wpftaskbar.showLog', () => output.show());
  context.subscriptions.push(vscode.window.onDidCloseTerminal(terminal => {
    const binding = terminals.get(terminal);
    if (!binding) return;
    terminals.delete(terminal);
    void removeBinding(binding);
  }));
  heartbeat = setInterval(() => {
    if (!renewal) renewal = renewSessions().finally(() => { renewal = undefined; });
  }, 30000);
}

export async function deactivate(): Promise<void> {
  stopping = true;
  clearInterval(heartbeat);
  const bindings = [...terminals.values(), ...pendingTerminals.values()];
  for (const binding of pendingTerminals.values()) binding.cancellation?.dispose();
  terminals.clear();
  pendingTerminals.clear();
  await Promise.allSettled(bindings.map(binding => binding.client.remove(binding.id)));
}

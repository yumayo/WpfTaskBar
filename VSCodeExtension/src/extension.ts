import * as vscode from 'vscode';
import { ApiError, TaskbarClient, normalizeUrl, terminalEnvironment, type TaskWindow } from './client';

interface Binding {
  client: TaskbarClient;
  id: string;
}

const terminals = new Map<vscode.Terminal, Binding>();
let target: { apiUrl: string; window: TaskWindow } | undefined;
let heartbeat: NodeJS.Timeout | undefined;
let renewal: Promise<void> | undefined;
let output: vscode.OutputChannel;
let opening = false;

function describe(error: unknown): string { return error instanceof Error ? error.message : String(error); }

async function selectWindow(client: TaskbarClient, force = false): Promise<TaskWindow | undefined> {
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
  };
}

async function openTerminal(): Promise<void> {
  if (opening) return;
  opening = true;
  try {
    const { client, containerUrl } = configuration();
    const window = await selectWindow(client);
    if (!window) return;
    const session = await client.createSession(window);
    try {
      const terminal = vscode.window.createTerminal({
        name: 'AI (WpfTaskBar)',
        env: terminalEnvironment(containerUrl, session.sessionId, process.env.WSLENV),
      });
      terminals.set(terminal, { client, id: session.sessionId });
      terminal.show();
      output.appendLine(`通知付きターミナルを作成: ${window.title} (HWND ${window.handle})`);
    } catch (error) {
      await client.remove(session.sessionId).catch(() => {});
      throw error;
    }
  } finally { opening = false; }
}

async function renewSessions(): Promise<void> {
  await Promise.all([...terminals].map(async ([terminal, binding]) => {
    try { await binding.client.renew(binding.id); }
    catch (error) {
      if (terminals.get(terminal) !== binding) return;
      output.appendLine(`通知先の更新に失敗: ${describe(error)}`);
      if (error instanceof ApiError && error.statusCode === 404) {
        terminals.delete(terminal);
        void vscode.window.showWarningMessage('WpfTaskBar の通知先が失効しました。「AI 通知付きターミナルを開く」から開き直してください。');
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
  command('wpftaskbar.openTerminal', openTerminal);
  command('wpftaskbar.selectWindow', async () => {
    // 開いているターミナルの通知先を黙って変更しない。
    if (terminals.size > 0) throw new Error('通知先を変更するには、AI 通知付きターミナルを先に閉じてください。');
    await selectWindow(configuration().client, true);
  });
  command('wpftaskbar.showLog', () => output.show());
  context.subscriptions.push(vscode.window.onDidCloseTerminal(terminal => {
    const binding = terminals.get(terminal);
    if (!binding) return;
    terminals.delete(terminal);
    void binding.client.remove(binding.id).catch(error => output.appendLine(`通知先の削除に失敗: ${describe(error)}`));
  }));
  heartbeat = setInterval(() => {
    if (!renewal) renewal = renewSessions().finally(() => { renewal = undefined; });
  }, 30000);
}

export async function deactivate(): Promise<void> {
  clearInterval(heartbeat);
  const bindings = [...terminals.values()];
  terminals.clear();
  await Promise.allSettled(bindings.map(binding => binding.client.remove(binding.id)));
}

import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/task-status.ts', import.meta.url), 'utf8');
const { outputText } = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2020 },
});
const { updateTaskStatus } = await import(`data:text/javascript;base64,${Buffer.from(outputText).toString('base64')}`);

// 状態更新に必要な DOM 操作だけを記録する。描画自体は WebView2 で確認する。
function createElement() {
  const attributes = new Map();
  let writes = 0;
  return {
    dataset: new Proxy({}, { set(target, key, value) { writes++; target[key] = value; return true; } }),
    get writes() { return writes; },
    get title() { return attributes.get('title'); },
    set title(value) { this.setAttribute('title', value); },
    setAttribute(name, value) { writes++; attributes.set(name, value); },
    removeAttribute(name) { writes++; attributes.delete(name); },
    getAttribute(name) { return attributes.get(name) ?? null; },
  };
}

test('開始、完了、解除、再実行で表示とアクセシビリティ情報を切り替える', () => {
  const element = createElement();
  updateTaskStatus(element);
  assert.equal(element.dataset.status, 'none');
  assert.equal(element.getAttribute('aria-hidden'), 'true');

  for (const [status, label] of [['running', 'AI 実行中'], ['completed', 'AI 実行済み']]) {
    updateTaskStatus(element, status);
    assert.equal(element.dataset.status, status);
    assert.equal(element.title, label);
    assert.equal(element.getAttribute('aria-label'), label);
    assert.equal(element.getAttribute('role'), 'img');
    assert.equal(element.getAttribute('aria-hidden'), null);
  }

  updateTaskStatus(element, 'none');
  assert.equal(element.dataset.status, 'none');
  assert.equal(element.title, undefined);
  assert.equal(element.getAttribute('aria-label'), null);
  assert.equal(element.getAttribute('role'), null);
  assert.equal(element.getAttribute('aria-hidden'), 'true');

  updateTaskStatus(element, 'running');
  assert.equal(element.dataset.status, 'running');
  assert.equal(element.getAttribute('aria-hidden'), null);
});

test('定期更新で同じ状態を受けても DOM を変更しない', () => {
  const element = createElement();
  updateTaskStatus(element, 'running');
  const writes = element.writes;
  for (let i = 0; i < 10; i++) updateTaskStatus(element, 'running');
  assert.equal(element.writes, writes);
});

test('一つのタスクを更新しても他のタスクの状態は変わらない', () => {
  const first = createElement();
  const second = createElement();
  updateTaskStatus(first, 'running');
  updateTaskStatus(second, 'running');
  updateTaskStatus(first, 'completed');
  assert.equal(second.dataset.status, 'running');
  assert.equal(second.title, 'AI 実行中');
});

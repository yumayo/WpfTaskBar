import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

const source = await readFile(new URL('../src/system-monitor.ts', import.meta.url), 'utf8');
const { outputText } = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2020 },
});
const { SystemMetricsHistory, buildGraphPaths, setupSystemMonitor } = await import(
  `data:text/javascript;base64,${Buffer.from(outputText).toString('base64')}`
);

const values = { cpu: 25, memory: 50, disk: 75, swap: 10 };

test('4項目を同じ時刻に記録し、計測前の履歴は0%にしない', () => {
  const history = new SystemMetricsHistory();
  assert.equal(history.add(100_000, values), true);
  for (const [metric, value] of Object.entries(values)) {
    assert.equal(history.values[metric].length, 61);
    assert.deepEqual(history.values[metric].slice(0, -1), Array(60).fill(null));
    assert.equal(history.values[metric].at(-1), value);
  }
});

test('欠測秒を空白にし、長時間のスリープ後も60秒分を超えて保持しない', () => {
  const history = new SystemMetricsHistory();
  history.add(100_000, values);
  history.add(103_000, { ...values, cpu: 90, swap: null });
  assert.deepEqual(history.values.cpu.slice(-4), [25, null, null, 90]);
  assert.deepEqual(history.values.swap.slice(-4), [10, null, null, null]);
  history.add(3_700_000, values);
  assert.equal(history.values.cpu.length, 61);
  assert.deepEqual(history.values.cpu.slice(0, -1), Array(60).fill(null));
});

test('同じ秒は最新値に置換し、遅れて届いた古いサンプルや不正な時刻は無視する', () => {
  const history = new SystemMetricsHistory();
  history.add(100_000, values);
  history.add(100_900, { ...values, cpu: 99 });
  assert.equal(history.values.cpu.at(-1), 99);
  assert.equal(history.values.cpu.at(-2), null);
  assert.equal(history.add(99_999, values), false);
  assert.equal(history.add(NaN, values), false);
  assert.equal(history.add(Infinity, values), false);
  assert.equal(history.values.cpu.at(-1), 99);
});

test('一部の取得失敗は他の項目に影響せず、使用率は0〜100%に収める', () => {
  const history = new SystemMetricsHistory();
  history.add(100_000, { cpu: -2, memory: 105, disk: NaN, swap: '20' });
  assert.deepEqual(Object.values(history.values).map(series => series.at(-1)), [0, 100, null, null]);
  history.add(101_000, { ...values, swap: Infinity });
  assert.equal(history.values.cpu.at(-1), 25);
  assert.equal(history.values.swap.at(-1), null);
});

test('欠測を横断する線や塗りつぶしを描かず、0%と100%を同じ縦軸で表示する', () => {
  assert.deepEqual(buildGraphPaths([null, null]), { line: '', area: '' });
  const paths = buildGraphPaths([0, 100, null, 50, 25]);
  assert.equal(paths.line, 'M0 100.00 L1 0.00 M3 50.00 L4 75.00');
  assert.equal(paths.area, 'M0 100 L0 100.00 L1 0.00 L1 100 Z M3 100 L3 50.00 L4 75.00 L4 100 Z');
  assert.equal(buildGraphPaths([null, 50]).line, 'M1 50.00 L1 50.00');
});

function createElement(textContent = '') {
  const attributes = new Map();
  return {
    textContent,
    title: '',
    setAttribute(name, value) { attributes.set(name, value); },
    getAttribute(name) { return attributes.get(name); },
  };
}

function setupHost(t) {
  const originalWindow = globalThis.window;
  const originalDocument = globalThis.document;
  const listeners = new Set();
  const pageEvents = new Map();
  const cards = new Map();
  for (const [metric, label] of [['cpu', 'CPU'], ['memory', 'メモリ'], ['disk', 'ディスク'], ['swap', 'スワップ']]) {
    const line = createElement();
    const area = createElement();
    const value = createElement('--');
    const graph = Object.assign(createElement(), {
      querySelector(selector) { return selector.endsWith('line') ? line : area; },
    });
    const card = Object.assign(createElement(), {
      querySelector(selector) {
        return selector === 'svg' ? graph : selector.endsWith('value') ? value : createElement(label);
      },
      graph, line, area, value,
    });
    cards.set(metric, card);
  }
  const container = {
    querySelector(selector) { return cards.get(selector.match(/"(\w+)"/)[1]); },
  };
  globalThis.document = {
    getElementById(id) { return id === 'systemMonitor' ? container : null; },
  };
  globalThis.window = {
    chrome: { webview: {
      addEventListener(_event, handler) { listeners.add(handler); },
      removeEventListener(_event, handler) { listeners.delete(handler); },
    } },
    addEventListener(event, handler) { pageEvents.set(event, handler); },
  };
  t.mock.timers.enable({ apis: ['setInterval', 'Date'], now: Date.parse('2026-10-29T12:00:00Z') });
  t.after(() => {
    pageEvents.get('pagehide')?.();
    globalThis.window = originalWindow;
    globalThis.document = originalDocument;
  });
  setupSystemMonitor();
  return {
    cards, listeners, pageEvents,
    receive(data) { for (const listener of listeners) listener({ data }); },
    sample(overrides = {}) {
      return {
        type: 'system_metrics_update', timestamp: new Date().toISOString(),
        cpuPercent: 25, memoryPercent: 50, diskPercent: 75, swapPercent: 10,
        memoryUsedBytes: 8 * 1024 ** 3, memoryTotalBytes: 16 * 1024 ** 3,
        ...overrides,
      };
    },
  };
}

test('WebView2の文字列・オブジェクト形式から4グラフ、現在値、メモリ容量を更新する', t => {
  const host = setupHost(t);
  host.receive(JSON.stringify(host.sample()));
  assert.deepEqual([...host.cards.values()].map(card => card.value.textContent), ['25%', '50%', '75%', '10%']);
  assert.equal(host.cards.get('memory').title, '物理メモリ：8.0 / 16.0 GiB 使用中');
  assert.match(host.cards.get('swap').graph.getAttribute('aria-label'), /スワップ：10%/);
  assert.match(host.cards.get('swap').line.getAttribute('d'), /60 90.00/);

  t.mock.timers.tick(1000);
  host.receive(host.sample({ swapPercent: null }));
  assert.equal(host.cards.get('swap').value.textContent, '--');
  assert.equal(host.cards.get('cpu').value.textContent, '25%');
  assert.match(host.cards.get('swap').graph.getAttribute('aria-label'), /取得できません/);
});

test('無関係なメッセージや壊れたJSONは無視し、次の正常サンプルを受信できる', t => {
  const host = setupHost(t);
  for (const data of [null, '{broken', [], { type: 'taskbar_items_response' }, host.sample({ timestamp: 'invalid' })]) {
    assert.doesNotThrow(() => host.receive(data));
  }
  assert.ok([...host.cards.values()].every(card => card.value.textContent === '--'));
  host.receive(host.sample());
  assert.equal(host.cards.get('swap').value.textContent, '10%');
});

test('更新停止後は現在値を残さず、復帰時は欠測を補間せずに再開し、終了時に購読解除する', t => {
  const host = setupHost(t);
  host.receive(host.sample());
  t.mock.timers.tick(3000);
  assert.ok([...host.cards.values()].every(card => card.value.textContent === '--'));
  assert.equal(host.cards.get('cpu').line.getAttribute('d'), 'M57 75.00 L57 75.00');

  t.mock.timers.tick(1000);
  host.receive(host.sample({ cpuPercent: 60 }));
  assert.equal(host.cards.get('cpu').value.textContent, '60%');
  assert.equal(host.cards.get('cpu').line.getAttribute('d'), 'M56 75.00 L56 75.00 M60 40.00 L60 40.00');
  host.pageEvents.get('pagehide')();
  assert.equal(host.listeners.size, 0);
  t.mock.timers.tick(10_000);
  assert.equal(host.cards.get('cpu').value.textContent, '60%');
});

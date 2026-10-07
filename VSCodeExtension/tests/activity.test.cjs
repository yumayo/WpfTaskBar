'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const test = require('node:test');

const run = promisify(execFile);
const scripts = path.join(__dirname, '../scripts');
const codexMessage = (role, text, phase = 'commentary') => ({
  type: 'response_item', payload: { type: 'message', role, phase, content: [{ type: role === 'user' ? 'input_text' : 'output_text', text }] },
});

async function fixture(t) {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'wpftaskbar-activity-'));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  const capture = path.join(dir, 'curl-args');
  const transcript = path.join(dir, 'transcript.jsonl');
  await fs.writeFile(path.join(dir, 'curl'), '#!/bin/sh\nprintf "%s\\n" "$@" > "$TASKBAR_TEST_CAPTURE"\nexit "${TASKBAR_TEST_EXIT:-0}"\n', { mode: 0o755 });
  const env = { ...process.env, PATH: `${dir}:${process.env.PATH}`, TASKBAR_TEST_CAPTURE: capture,
    WPF_TASKBAR_URL: 'http://windows-host:5000', WPF_TASKBAR_SESSION_ID: '0123456789abcdef0123456789abcdef' };
  return {
    dir, transcript, env,
    async write(records, suffix = '') {
      await fs.writeFile(transcript, records.map(value => JSON.stringify(value)).join('\n') + '\n' + suffix);
    },
    async invoke(action, input = {}, options = {}) {
      await fs.rm(capture, { force: true });
      const execution = run('sh', [path.join(scripts, 'taskbar-status.sh'), action], { env, timeout: 3000, ...options });
      execution.child.stdin.end(JSON.stringify({ transcript_path: transcript, ...input }));
      const result = await execution;
      assert.equal(result.stdout, '');
      let args;
      try { args = (await fs.readFile(capture, 'utf8')).trim().split('\n'); }
      catch (error) { if (error.code !== 'ENOENT') throw error; }
      return { ...result, args, payload: args && JSON.parse(args[args.indexOf('--data') + 1]) };
    },
  };
}

test('Codexの最新進捗をツール前後に送り、依頼・思考・ツール結果は表示しない', { skip: process.platform === 'win32' }, async t => {
  const f = await fixture(t);
  await f.write([
    codexMessage('assistant', '以前の応答'), codexMessage('user', '送信しない依頼'),
    codexMessage('assistant', '調査を開始します。'),
    { type: 'response_item', payload: { type: 'reasoning', summary: [{ text: '送信しない思考' }] } },
    codexMessage('assistant', '**設定ファイル**を確認しています。\n次に [テスト](https://example.com) を実行します。'),
    codexMessage('assistant', '表示対象外の内部メッセージ', 'analysis'),
    { type: 'response_item', payload: { type: 'function_call_output', output: '送信しないツール結果' } },
  ], '{"type":"response_item",');
  const text = '設定ファイルを確認しています。 次に テスト を実行します。';
  const activity = await f.invoke('activity');
  assert.deepEqual(activity.payload, { activityText: text });
  assert.equal(activity.args[activity.args.indexOf('--request') + 1], 'PUT');
  assert.match(activity.args.at(-1), /\/activity$/);
  assert.deepEqual((await f.invoke('resume')).payload, { status: 'running', onlyIfActive: true, activityText: text });
  assert.deepEqual((await f.invoke('waiting')).payload, { status: 'waiting', activityText: text });
  assert.deepEqual((await f.invoke('completed', { last_assistant_message: '修正してテストが通りました。' })).payload,
    { status: 'completed', activityText: '修正してテストが通りました。' });
  // 開始・終了フックは前の応答を再送しない。表示の解除はサーバーが行う。
  assert.deepEqual((await f.invoke('running')).payload, { status: 'running' });
  assert.deepEqual((await f.invoke('none')).payload, { status: 'none' });
});

test('新しい依頼の後に応答がなければ以前の文章を送らず、ログがなくても状態を通知する', { skip: process.platform === 'win32' }, async t => {
  const f = await fixture(t);
  await f.write([codexMessage('assistant', '古い文章'), codexMessage('user', '新しい依頼')]);
  assert.equal((await f.invoke('activity')).args, undefined);
  assert.deepEqual((await f.invoke('resume')).payload, { status: 'running', onlyIfActive: true });
  await fs.unlink(f.transcript);
  assert.deepEqual((await f.invoke('completed')).payload, { status: 'completed' });
  assert.equal((await f.invoke('activity')).args, undefined);
  assert.equal((await f.invoke('activity', {}, { env: { ...f.env, WPF_TASKBAR_SESSION_ID: '' } })).args, undefined);
});

test('Claudeの本文だけを抜粋し、ツール結果・サブエージェント・思考を混ぜない', { skip: process.platform === 'win32' }, async t => {
  const f = await fixture(t);
  await f.write([
    { type: 'user', message: { role: 'user', content: '送信しない依頼' } },
    { type: 'assistant', message: { role: 'assistant', content: [
      { type: 'thinking', thinking: '送信しない思考' },
      { type: 'text', text: '不具合の原因を調査しています。' },
      { type: 'tool_use', name: 'Bash', input: { command: '送信しないコマンド' } },
    ] } },
    { type: 'assistant', isSidechain: true, message: { role: 'assistant', content: [{ type: 'text', text: '別エージェント' }] } },
    { type: 'user', message: { role: 'user', content: [{ type: 'tool_result', content: '送信しない結果' }] } },
  ]);
  assert.deepEqual((await f.invoke('tool-failed', { is_interrupt: true })).payload,
    { status: 'interrupted', onlyIfActive: true, activityText: '不具合の原因を調査しています。' });
});

test('長いログの末尾を読み、日本語や絵文字を壊さず120文字に収める', { skip: process.platform === 'win32' }, async t => {
  const f = await fixture(t);
  await f.write([
    { type: 'response_item', payload: { type: 'function_call_output', output: 'x'.repeat(600000) } },
    codexMessage('assistant', '## **進捗**\n```sh\n送信しないコード\n```\n' + '修正中😀'.repeat(100)),
  ]);
  const { activityText } = (await f.invoke('activity')).payload;
  assert.equal([...activityText].length, 120);
  assert.ok(activityText.startsWith('進捗 修正中😀'));
  assert.ok(activityText.endsWith('…'));
  assert.equal(activityText.includes('コード'), false);
  assert.equal(activityText.includes('\ufffd'), false);
});

for (const ai of ['codex', 'claude']) {
  test(`${ai}の導入済みフックが配置先のヘルパーを使って進捗を送れる`, { skip: process.platform === 'win32' }, async t => {
    const f = await fixture(t);
    const target = path.join(f.dir, "config 'quoted'");
    await run('python3', [path.join(scripts, `install-${ai}-hooks.py`), `--${ai}-dir`, target]);
    const config = JSON.parse(await fs.readFile(path.join(target, ai === 'codex' ? 'hooks.json' : 'settings.json'), 'utf8'));
    const handler = config.hooks.PreToolUse.flatMap(group => group.hooks).find(hook => hook.command.endsWith(' activity'));
    assert.ok(handler);
    assert.equal(await fs.readFile(path.join(target, 'hooks/wpftaskbar/taskbar-message.py'), 'utf8'),
      await fs.readFile(path.join(scripts, 'taskbar-message.py'), 'utf8'));
    await f.write([codexMessage('assistant', 'テストを実行しています。')]);
    const execution = run('sh', ['-c', handler.command], { env: f.env, cwd: f.dir });
    execution.child.stdin.end(JSON.stringify({ transcript_path: f.transcript }));
    assert.equal((await execution).stdout, '');
    const args = (await fs.readFile(path.join(f.dir, 'curl-args'), 'utf8')).trim().split('\n');
    assert.deepEqual(JSON.parse(args[args.indexOf('--data') + 1]), { activityText: 'テストを実行しています。' });
  });
}

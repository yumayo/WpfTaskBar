import type { MessageData } from './types';

const HISTORY_SECONDS = 60;
const METRICS = ['cpu', 'memory', 'disk', 'swap'] as const;
type Metric = typeof METRICS[number];

function percent(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value)
    ? Math.max(0, Math.min(100, value))
    : null;
}

/** 時刻で履歴を揃え、欠測を 0% や補間値で埋めない。最大 60 秒分だけ保持する。 */
export class SystemMetricsHistory {
  private second: number | null = null;
  readonly values: Record<Metric, Array<number | null>> = {
    cpu: Array(HISTORY_SECONDS + 1).fill(null),
    memory: Array(HISTORY_SECONDS + 1).fill(null),
    disk: Array(HISTORY_SECONDS + 1).fill(null),
    swap: Array(HISTORY_SECONDS + 1).fill(null),
  };

  advanceTo(timestamp: number): boolean {
    if (!Number.isFinite(timestamp)) return false;
    const second = Math.floor(timestamp / 1000);
    if (this.second !== null && second < this.second) return false;

    const gap = this.second === null ? 0 : Math.min(second - this.second, HISTORY_SECONDS + 1);
    if (gap > 0) {
      for (const metric of METRICS) {
        this.values[metric].splice(0, gap);
        this.values[metric].push(...Array(gap).fill(null));
      }
    }
    this.second = second;
    return true;
  }

  add(timestamp: number, values: Record<Metric, unknown>): boolean {
    if (!this.advanceTo(timestamp)) return false;
    for (const metric of METRICS) {
      this.values[metric][HISTORY_SECONDS] = percent(values[metric]);
    }
    return true;
  }
}

/** 欠測の前後でパスを分ける。縦軸は全グラフ共通で 0〜100%。 */
export function buildGraphPaths(values: ReadonlyArray<number | null>): { line: string; area: string } {
  const lines: string[] = [];
  const areas: string[] = [];
  let segment: string[] = [];
  let start = 0;
  let end = 0;

  function flush(): void {
    if (segment.length === 0) return;
    // 単独サンプルも丸い点として描画する。
    lines.push(`M${segment.join(' L')}${segment.length === 1 ? ` L${segment[0]}` : ''}`);
    areas.push(`M${start} 100 L${segment.join(' L')} L${end} 100 Z`);
    segment = [];
  }

  values.forEach((value, index) => {
    const normalized = percent(value);
    if (normalized === null) {
      flush();
      return;
    }
    if (segment.length === 0) start = index;
    end = index;
    segment.push(`${index} ${(100 - normalized).toFixed(2)}`);
  });
  flush();
  return { line: lines.join(' '), area: areas.join(' ') };
}

export function setupSystemMonitor(): void {
  const webview = window.chrome?.webview;
  const container = document.getElementById('systemMonitor');
  if (!webview || !container) return;

  const history = new SystemMetricsHistory();
  const cards = METRICS.map(metric => {
    const card = container.querySelector<HTMLElement>(`[data-metric="${metric}"]`)!;
    const graph = card.querySelector<SVGElement>('svg')!;
    return {
      metric, card, graph,
      label: card.querySelector('.system-metric-heading > span')!.textContent,
      value: card.querySelector('.system-metric-value')!,
      line: graph.querySelector('.system-metric-line')!,
      area: graph.querySelector('.system-metric-area')!,
    };
  });
  let lastReceivedAt: number | null = null;

  function render(stale = false): void {
    for (const { metric, graph, value, line, area, label } of cards) {
      const values = history.values[metric];
      const current = stale ? null : values[HISTORY_SECONDS];
      value.textContent = current === null ? '--' : `${current.toFixed(0)}%`;
      graph.setAttribute('aria-label', `${label}：${current === null ? '取得できません' : `${current.toFixed(0)}%`}、直近60秒`);
      const paths = buildGraphPaths(values);
      line.setAttribute('d', paths.line);
      area.setAttribute('d', paths.area);
    }
  }

  function receive(event: MessageEvent): void {
    let data: MessageData;
    try {
      data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
    } catch {
      return;
    }
    if (!data || data.type !== 'system_metrics_update' || typeof data.timestamp !== 'string') return;

    if (!history.add(Date.parse(data.timestamp), {
      cpu: data.cpuPercent, memory: data.memoryPercent,
      disk: data.diskPercent, swap: data.swapPercent,
    })) return;

    lastReceivedAt = Date.now();
    const memoryCard = cards.find(card => card.metric === 'memory')!.card;
    const used = data.memoryUsedBytes;
    const total = data.memoryTotalBytes;
    memoryCard.title = typeof used === 'number' && Number.isFinite(used) && used >= 0 &&
      typeof total === 'number' && Number.isFinite(total) && total > 0 && used <= total
      ? `物理メモリ：${(used / 1024 ** 3).toFixed(1)} / ${(total / 1024 ** 3).toFixed(1)} GiB 使用中`
      : '物理メモリの使用率（現在取得できません）';
    render();
  }

  webview.addEventListener('message', receive);
  const timer = setInterval(() => {
    if (lastReceivedAt !== null && Date.now() - lastReceivedAt >= 3000) {
      history.advanceTo(Date.now());
      render(true);
      cards.find(card => card.metric === 'memory')!.card.title = '物理メモリの使用率（更新待ち）';
    }
  }, 1000);

  window.addEventListener('pagehide', () => {
    clearInterval(timer);
    webview.removeEventListener('message', receive);
  }, { once: true });
}

import type { TaskStatus } from './types';

export function updateTaskStatus(element: HTMLElement, status: TaskStatus = 'none'): void {
  if (element.dataset.status === status) return;

  element.dataset.status = status;
  if (status === 'running' || status === 'completed') {
    const label = status === 'running' ? 'AI 実行中' : 'AI 実行済み';
    element.title = label;
    element.setAttribute('role', 'img');
    element.setAttribute('aria-label', label);
    element.removeAttribute('aria-hidden');
  } else {
    element.removeAttribute('title');
    element.removeAttribute('role');
    element.removeAttribute('aria-label');
    element.setAttribute('aria-hidden', 'true');
  }
}

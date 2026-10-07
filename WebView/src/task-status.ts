import type { TaskStatus, TaskBarItem } from './types';

const statusLabels: Record<TaskStatus, string> = {
  none: '', running: 'AI 実行中', waiting: 'AI 質問・承認待ち',
  interrupted: 'AI 中断', completed: 'AI 実行済み',
};

export function updateTaskStatus(element: HTMLElement, status: TaskStatus = 'none'): void {
  if (element.dataset.status === status) return;

  element.dataset.status = status;
  const label = statusLabels[status];
  if (label) {
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

export function updateAiTask(item: HTMLElement, titleElement: HTMLElement, task: TaskBarItem): void {
  const hasAiTask = task.hasAiTask ?? (!!task.status && task.status !== 'none');
  item.classList.toggle('has-ai-task', hasAiTask);
  const title = hasAiTask ? task.terminalTitle || '' : '';
  if (titleElement.textContent !== title) titleElement.textContent = title;
  if (titleElement.title !== title) titleElement.title = title;
  if (titleElement.hidden !== !hasAiTask) titleElement.hidden = !hasAiTask;
}

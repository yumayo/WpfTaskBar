using System.Windows;
using System.Windows.Controls;

namespace WpfTaskBar;

public partial class TaskSessionWindow : Window
{
	private readonly TaskStatusStore _statuses;
	private readonly ITaskWindowProvider _windows;

	public TaskSessionWindow(TaskStatusStore statuses, ITaskWindowProvider windows)
	{
		InitializeComponent();
		_statuses = statuses;
		_windows = windows;
		RefreshChoices();
	}

	private void RefreshChoices()
	{
		var windows = _windows.GetWindows().Where(window => window.IsVsCode()).ToArray();
		TargetComboBox.ItemsSource = windows.Select(window => new WindowChoice(window,
			$"{window.Title} (HWND {window.Handle} / PID {window.ProcessId})")).ToArray();
		SessionComboBox.ItemsSource = _statuses.GetSessions().Select(session =>
		{
			var window = windows.FirstOrDefault(item => item.Handle == session.Handle && item.ProcessId == session.ProcessId);
			return new SessionChoice(session.Id,
				$"{window?.Title ?? "接続先なし"} / {session.TerminalTitle} / ID {session.Id[..8]}");
		}).ToArray();
		StatusText.Text = SessionComboBox.Items.Count == 0
			? "登録済みの通知がありません。VSCodeで「この VSCode の通知先を選択」を実行してください。"
			: "変更する通知と、変更後の表示先を選択してください。";
		UpdateApplyButton();
	}

	private void Session_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		SessionIdTextBox.Text = (SessionComboBox.SelectedItem as SessionChoice)?.Id ?? "";
		TargetComboBox.SelectedIndex = -1;
		UpdateApplyButton();
	}

	private void Target_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateApplyButton();

	private void UpdateApplyButton() => ApplyButton.IsEnabled =
		SessionComboBox.SelectedItem is SessionChoice && TargetComboBox.SelectedItem is WindowChoice;

	private void Apply_OnClick(object sender, RoutedEventArgs e)
	{
		if (SessionComboBox.SelectedItem is not SessionChoice session ||
			TargetComboBox.SelectedItem is not WindowChoice selected) return;
		var target = selected.Window;
		if (!_windows.GetWindows().Any(window => window.Handle == target.Handle &&
			window.ProcessId == target.ProcessId && window.IsVsCode()))
		{
			StatusText.Text = "選択したウィンドウが見つかりません。一覧を更新して選び直してください。";
			return;
		}
		if (_statuses.ReassignSession(session.Id, target.Handle, target.ProcessId) == null)
		{
			StatusText.Text = "通知の登録が失効しました。一覧を更新して選び直してください。";
			return;
		}
		Logger.Info($"AI通知先を変更: HWND {target.Handle} / PID {target.ProcessId}");
		RefreshChoices();
		StatusText.Text = $"「{target.Title}」に変更しました。通知IDは変わりません。";
	}

	private void Refresh_OnClick(object sender, RoutedEventArgs e) => RefreshChoices();
	private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

	private sealed record SessionChoice(string Id, string Label);
	private sealed record WindowChoice(TaskWindow Window, string Label);
}

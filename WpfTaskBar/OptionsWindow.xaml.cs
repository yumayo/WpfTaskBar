using System.Windows;

namespace WpfTaskBar;

public partial class OptionsWindow : Window
{
	public OptionsWindow()
	{
		InitializeComponent();
		AttendanceEnabledCheckBox.IsChecked = AppSettingsModel.IsAttendanceEnabled;
		AiTaskDetailsVisibleCheckBox.IsChecked = AppSettingsModel.IsAiTaskDetailsVisible;
	}

	private void SaveButton_OnClick(object sender, RoutedEventArgs e)
	{
		AppSettingsModel.SetAttendanceEnabled(AttendanceEnabledCheckBox.IsChecked == true);
		AppSettingsModel.SetAiTaskDetailsVisible(AiTaskDetailsVisibleCheckBox.IsChecked == true);
		Close();
	}

	private void CancelButton_OnClick(object sender, RoutedEventArgs e)
	{
		Close();
	}
}

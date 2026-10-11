using System.Windows;

namespace KeepingYouAwake.App;
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Startup.IsChecked = App.CurrentApp.Engine.Status.Preferences.StartWithWindows;
        Startup.IsEnabled = !App.CurrentApp.Simulation;
    }
    private void StartupClick(object sender, RoutedEventArgs e)
    {
        try { App.CurrentApp.SetStartup(Startup.IsChecked == true); }
        catch (Exception ex)
        {
            Startup.IsChecked = App.CurrentApp.Engine.Status.Preferences.StartWithWindows;
            MessageBox.Show(this, ex.Message, "设置未能保存", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText("kya shutdown --json"); }
        catch (System.Runtime.InteropServices.COMException) { MessageBox.Show(this, "剪贴板正被其他程序使用，请稍后重试。", "复制命令"); }
    }
}

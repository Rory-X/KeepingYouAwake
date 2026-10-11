using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeepingYouAwake.Core;

namespace KeepingYouAwake.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShutdownDate.SelectedDate = DateTime.Today;
        AwakeDuration.SelectionChanged += (_, _) => CustomAwakeRow.Visibility = ((ComboBoxItem)AwakeDuration.SelectedItem).Tag?.ToString() == "custom" ? Visibility.Visible : Visibility.Collapsed;
        DelayMode.Checked += (_, _) => SetScheduleMode(false);
        AtMode.Checked += (_, _) => SetScheduleMode(true);
        Closing += OnClosing;
    }
    private void SetScheduleMode(bool at)
    { DelayRow.Visibility = at ? Visibility.Collapsed : Visibility.Visible; AtRow.Visibility = at ? Visibility.Visible : Visibility.Collapsed; }
    public void Refresh(AppStatus state)
    {
        AwakeTitle.Text = state.Awake ? "防休眠已开启" : "防休眠已关闭";
        AwakeDetail.Text = state.ManualAwake ? state.AwakeUntilUtc is { } until ? "剩余 " + Remaining(until - DateTimeOffset.UtcNow) : "持续保持运行，直到你关闭"
            : state.ScheduleAwake ? "关机计划正在保持电脑运行" : "电脑遵循 Windows 电源设置";
        AwakeToggle.Content = state.ManualAwake ? "关闭防休眠" : "开启防休眠";
        KeepScreen.IsChecked = state.Preferences.KeepScreenOn;
        CoffeeCup.Fill = state.Awake ? (Brush)FindResource("Accent") : Brushes.Transparent;
        CancelPlan.IsEnabled = state.Shutdown is not null;
        if (state.Shutdown is { } plan)
        {
            PlanTitle.Text = plan.Phase == PlanPhase.Countdown ? $"{state.CountdownSeconds} 秒后关机" : plan.DueUtc.ToLocalTime().ToString("MM-dd HH:mm:ss") + " 开始倒计时";
            PlanDetail.Text = (plan.Source == "cli" ? "由 CLI / AI 创建" : "由你创建") + " · 等待期间保持运行";
        }
        else { PlanTitle.Text = "暂无关机计划"; PlanDetail.Text = "等待期间自动防休眠，屏幕可熄灭"; }
        Message.Text = (state.Simulation ? "[模拟模式] " : "") + state.LastEvent.Message;
    }
    private static string Remaining(TimeSpan time)
    {
        if (time.TotalHours >= 1) return $"{(int)time.TotalHours} 小时 {time.Minutes} 分钟";
        return time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes} 分钟 {time.Seconds} 秒" : $"{Math.Max(0, (int)time.TotalSeconds)} 秒";
    }
    private void AwakeClick(object sender, RoutedEventArgs e)
    {
        var app = App.CurrentApp;
        if (app.Engine.Status.ManualAwake) { app.Run(new("awake-off")); return; }
        try
        {
            var tag = ((ComboBoxItem)AwakeDuration.SelectedItem).Tag!.ToString();
            var seconds = tag == "0" ? (double?)null : CommandLine.DurationSeconds((tag == "custom" ? CustomAwakeMinutes.Text : tag) + "m", false);
            app.Run(new("awake-on", seconds));
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "时长无效", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ScreenClick(object sender, RoutedEventArgs e) => App.CurrentApp.Run(new("screen", Enabled: KeepScreen.IsChecked == true));
    private void SettingsClick(object sender, RoutedEventArgs e) => App.CurrentApp.ShowSettings();
    private void CancelClick(object sender, RoutedEventArgs e) => App.CurrentApp.Run(new("cancel"));
    private void ScheduleClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CommandRequest request;
            if (AtMode.IsChecked == true)
            {
                if (ShutdownDate.SelectedDate is not { } date) throw new ArgumentException("请选择日期。");
                if (!TimeSpan.TryParseExact(ShutdownTime.Text.Trim(), [@"hh\:mm", @"h\:mm", @"hh\:mm\:ss"], CultureInfo.InvariantCulture, out var time) || time.TotalHours >= 24)
                    throw new ArgumentException("请输入有效时间，例如 23:00。");
                var at = new DateTimeOffset(DateTime.SpecifyKind(date.Date.Add(time), DateTimeKind.Local));
                request = new("shutdown", AtUtc: at.ToUniversalTime());
            }
            else request = new("shutdown", CommandLine.DurationSeconds(DelayMinutes.Text + "m", true));
            if (App.CurrentApp.Engine.Status.Shutdown is not null)
            {
                if (MessageBox.Show(this, "已有一个关机计划，是否替换为新计划？", "替换关机计划", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                request = request with { Replace = true };
            }
            App.CurrentApp.Run(request);
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "关机计划无效", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    { if (!App.CurrentApp.Quitting) { e.Cancel = true; Hide(); } }
}

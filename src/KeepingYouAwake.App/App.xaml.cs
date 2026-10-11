using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeepingYouAwake.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace KeepingYouAwake.App;

public partial class App : Application
{
    private Mutex? mutex;
    private PipeServer? server;
    private WindowsPower? power;
    private EventLog? log;
    private Forms.NotifyIcon? tray;
    private Icon? solidIcon, outlineIcon;
    private DispatcherTimer? timer;
    private MainWindow? panel;
    private CountdownWindow? countdown;
    private bool quitting, simulation, headless, faulted;
    private bool? lastIconAwake;
    private string? capturePath;
    private StateEvent? runtimeError;
    public PowerEngine Engine { get; private set; } = null!;
    public bool Simulation => simulation;
    private AppStatus Status => runtimeError is null ? Engine.Status : Engine.Status with { LastEvent = runtimeError };
    public static App CurrentApp => (App)Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var options = e.Args.ToList();
            simulation = options.Contains("--simulate") || options.Contains("--capture-preview");
            headless = options.Contains("--headless");
            if (headless && !simulation) throw new ArgumentException("--headless 只能用于模拟测试。");
            capturePath = Value(options, "--capture-preview");
            var instance = Value(options, "--instance") ?? (capturePath is null ? "default" : "preview");
            if (simulation && instance == "default") instance = "simulation";
            var key = Wire.InstanceKey(WindowsIdentity.GetCurrent().User!.Value, instance);
            mutex = new Mutex(true, @"Local\" + key, out var first);
            if (!first)
            {
                if (options.Contains("--show")) await ShowExisting(key);
                Shutdown(); return;
            }
            var directory = Value(options, "--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeepingYouAwake", simulation ? "simulation-" + instance : "data");
            log = new EventLog(directory);
            var repository = new JsonRepository(directory);
            power = new WindowsPower(simulation);
            Engine = new PowerEngine(repository, power);
            Engine.Notice += OnNotice;
            if (!simulation && Engine.Status.Preferences.StartWithWindows != StartupRegistration.Enabled())
                Engine.SetStartupPreference(StartupRegistration.Enabled());
            solidIcon = LoadIcon("coffee-solid.ico");
            outlineIcon = LoadIcon("coffee-outline.ico");
            tray = new Forms.NotifyIcon { Icon = outlineIcon, Text = "KeepingYouAwake", Visible = true };
            tray.MouseClick += (_, mouse) => { if (mouse.Button == Forms.MouseButtons.Left) ShowPanel(); };
            var menu = new Forms.ContextMenuStrip();
            menu.Opening += (_, _) => PopulateMenu(menu);
            tray.ContextMenuStrip = menu;
            server = new PipeServer(key, Dispatcher, Handle);
            timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher);
            SystemEvents.PowerModeChanged += PowerModeChanged;
            SessionEnding += (_, _) => { quitting = true; };
            Refresh();
            log.Write(Engine.Status.LastEvent);
            if (repository.RecoveryMessage is { } warning) OnNotice(new(DateTimeOffset.UtcNow, "error", warning));
            if (Engine.Status.LastEvent.Kind == "missed") OnNotice(Engine.Status.LastEvent);
            if (options.Contains("--show") || capturePath is not null) ShowPanel();
            if (capturePath is not null)
            {
                Window preview = panel!;
                switch (Value(options, "--capture-window"))
                {
                    case "settings": preview = new SettingsWindow { Owner = panel }; preview.Show(); break;
                    case "countdown": preview = new CountdownWindow { AllowClose = true }; preview.Show(); break;
                    case "awake": Engine.Execute(new("awake-on", Seconds: 7200)); Refresh(); break;
                    case "scheduled": Engine.Execute(new("shutdown", Seconds: 1800)); Refresh(); break;
                }
                await Task.Delay(600);
                Capture(preview, capturePath);
                quitting = true; Shutdown();
            }
        }
        catch (Exception ex)
        {
            log?.Write(new(DateTimeOffset.UtcNow, "startup-failed", ex.ToString()));
            MessageBox.Show("软件未能启动：" + ex.Message, "KeepingYouAwake", MessageBoxButton.OK, MessageBoxImage.Error);
            quitting = true; Shutdown(1);
        }
    }

    private static string? Value(List<string> args, string name)
    {
        var index = args.IndexOf(name);
        if (index < 0) return null;
        if (index + 1 >= args.Count) throw new ArgumentException(name + " 缺少参数。");
        return args[index + 1];
    }
    private static Icon LoadIcon(string name)
    {
        using var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/" + name))!.Stream;
        using var icon = new Icon(resource, 32, 32);
        return (Icon)icon.Clone();
    }
    private CommandResponse Handle(CommandRequest request)
    {
        if (request.ProtocolVersion != 1) return new(false, "protocol", "CLI 与后台版本不一致。", Status);
        if (request.Command == "show") { ShowPanel(); return new(true, "shown", "控制面板已打开。", Status); }
        if (faulted && request.Command is not ("status" or "cancel")) return new(false, "operation-failed", "电源管理已暂停，请重启软件。", Status);
        var result = Engine.Execute(request);
        // Immediate shutdown enters the cancellable countdown before replying to the CLI.
        if (result.Success) Tick();
        else Refresh();
        return result with { State = Status };
    }
    public CommandResponse Run(CommandRequest request)
    {
        var response = Handle(request with { Source = "ui" });
        if (!response.Success) MessageBox.Show(panel, response.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        return response;
    }
    private void Tick()
    {
        if (faulted) return;
        try { Engine.Advance(); Refresh(); }
        catch (Exception ex)
        {
            // Stop ticking instead of repeatedly attempting a potentially dangerous transition.
            timer?.Stop();
            faulted = true;
            runtimeError = new(DateTimeOffset.UtcNow, "error", "电源管理已暂停，请重启软件：" + ex.Message);
            OnNotice(runtimeError);
            Refresh();
        }
    }
    private void Refresh()
    {
        var state = Status;
        if (tray is not null)
        {
            if (lastIconAwake != state.Awake) { tray.Icon = state.Awake ? solidIcon : outlineIcon; lastIconAwake = state.Awake; }
            var text = state.Shutdown?.Phase == PlanPhase.Countdown ? $"咖啡 · {state.CountdownSeconds} 秒后关机"
                : state.ScheduleAwake ? $"咖啡 · 保持运行至 {state.Shutdown!.DueUtc.ToLocalTime():HH:mm} 关机倒计时"
                : state.ManualAwake ? state.AwakeUntilUtc is { } until ? $"咖啡 · 防休眠至 {until.ToLocalTime():HH:mm}" : "咖啡 · 持续防休眠"
                : "咖啡 · 防休眠已关闭";
            tray.Text = simulation ? "[模拟] " + text : text;
        }
        panel?.Refresh(state);
        if (state.Shutdown?.Phase == PlanPhase.Countdown && !headless)
        {
            if (countdown is null)
            {
                countdown = new CountdownWindow();
                countdown.Closed += (_, _) => countdown = null;
                countdown.Show();
            }
            countdown.Update(state.CountdownSeconds);
        }
        else if (countdown is not null) { countdown.AllowClose = true; countdown.Close(); countdown = null; }
    }
    private void OnNotice(StateEvent entry)
    {
        log?.Write(entry);
        panel?.Refresh(Status);
        if (entry.Kind is "countdown" or "missed" or "shutdown-failed" or "error" or "awake-expired")
        {
            tray?.ShowBalloonTip(6000, "KeepingYouAwake", entry.Message,
                entry.Kind is "error" or "shutdown-failed" ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        }
    }
    private void PopulateMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        var state = Engine.Status;
        menu.Items.Add(state.Awake ? "● 防休眠生效中" : "○ 防休眠已关闭").Enabled = false;
        menu.Items.Add("打开控制面板", null, (_, _) => ShowPanel());
        menu.Items.Add(state.ManualAwake ? "关闭手动防休眠" : "持续开启防休眠", null, (_, _) => Run(new(state.ManualAwake ? "awake-off" : "awake-on")));
        var timed = new Forms.ToolStripMenuItem("保持清醒…");
        foreach (var minutes in new[] { 30, 60, 120 })
            timed.DropDownItems.Add(minutes + " 分钟", null, (_, _) => Run(new("awake-on", minutes * 60)));
        menu.Items.Add(timed);
        var screen = new Forms.ToolStripMenuItem("同时保持屏幕亮起") { Checked = state.Preferences.KeepScreenOn };
        screen.Click += (_, _) => Run(new("screen", Enabled: !state.Preferences.KeepScreenOn));
        menu.Items.Add(screen);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("安排关机…", null, (_, _) => ShowPanel());
        var cancel = menu.Items.Add("取消关机计划", null, (_, _) => Run(new("cancel"))); cancel.Enabled = state.Shutdown is not null;
        menu.Items.Add("设置与 CLI", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出软件", null, (_, _) => ExitByUser());
    }
    public void ShowPanel()
    {
        panel ??= new MainWindow();
        panel.Refresh(Status);
        panel.Show();
        if (panel.WindowState == WindowState.Minimized) panel.WindowState = WindowState.Normal;
        panel.Activate();
    }
    public void ShowSettings()
    {
        ShowPanel();
        var settings = new SettingsWindow { Owner = panel };
        settings.ShowDialog();
    }
    public void SetStartup(bool enabled)
    {
        if (simulation) throw new InvalidOperationException("模拟模式不会修改登录启动设置。");
        var previous = StartupRegistration.Enabled();
        StartupRegistration.Set(enabled);
        try { Engine.SetStartupPreference(enabled); }
        catch { StartupRegistration.Set(previous); throw; }
        Refresh();
    }
    public void ExitByUser()
    {
        if (Engine.Status.Shutdown is not null)
        {
            if (MessageBox.Show("退出后将取消当前关机计划。确定退出吗？", "退出 KeepingYouAwake", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (!Run(new("cancel")).Success) return;
        }
        quitting = true; Shutdown();
    }
    public bool Quitting => quitting;
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() =>
        { try { if (!faulted) Engine.Resumed(); Refresh(); } catch (Exception ex) { timer?.Stop(); faulted = true; runtimeError = new(DateTimeOffset.UtcNow, "error", ex.Message); OnNotice(runtimeError); Refresh(); } });
    }
    protected override void OnExit(ExitEventArgs e)
    {
        quitting = true;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        timer?.Stop(); server?.Dispose();
        tray?.Dispose(); solidIcon?.Dispose(); outlineIcon?.Dispose();
        try { power?.Dispose(); } catch (Win32Exception) { }
        mutex?.Dispose();
        base.OnExit(e);
    }
    private static async Task ShowExisting(string key)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", key, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new CommandRequest("show"), Wire.Json));
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { }
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path); encoder.Save(file);
    }
}

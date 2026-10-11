using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using KeepingYouAwake.Core;

Console.OutputEncoding = Encoding.UTF8;
var wantsJson = args.Contains("--json", StringComparer.Ordinal);
try
{
    var parsed = CommandLine.Parse(args);
    if (parsed.Help)
    {
        Console.WriteLine("""
            KeepingYouAwake CLI 0.1.0

            kya status [--json]                         查询后台状态
            kya awake on [--for 2h] [--screen-on]         开启防休眠；默认持续开启
            kya awake off                               关闭手动防休眠
            kya shutdown [--after 30m | --at "23:00"]    安排关机；默认立即开始 60 秒倒计时
            kya shutdown --at "2026-10-12 23:00" --replace
            kya cancel                                  取消计划和正在进行的倒计时
            kya show                                    打开控制面板

            时长支持 s / m / h / d，无单位按分钟。--at 不带时区时使用电脑本地时间。
            计划到期后还有 60 秒可取消；不强制关闭有未保存内容的程序。
            所有控制命令支持 --json；已存在计划需显式 --replace。
            后台未运行时自动启动。仅当前用户可连接后台。
            退出码：0 成功，2 参数错误，4 后台不可用，5 请求被拒绝，6 操作失败。
            """);
        return 0;
    }
    if (parsed.Version) { Console.WriteLine(parsed.Json ? "{\"version\":\"0.1.0\"}" : "KeepingYouAwake 0.1.0"); return 0; }
    var key = Wire.InstanceKey(WindowsIdentity.GetCurrent().User!.Value, parsed.Instance);
    NamedPipeClientStream? pipe = await Connect(key, 600);
    if (pipe is null && parsed.Instance == "default")
    {
        var appPath = Path.Combine(AppContext.BaseDirectory, "KeepingYouAwake.exe");
        if (!File.Exists(appPath)) throw new IOException("未找到托盘程序。请将 CLI 与 KeepingYouAwake.exe 放在同一目录。");
        // ShellExecute launches the GUI without inheriting the CLI's pipe handles.
        // CreateProcess with redirected streams still inherits unrelated inheritable
        // handles on Windows, keeping PowerShell command substitution open indefinitely.
        var start = new ProcessStartInfo(appPath)
        {
            UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add("--background");
        Process.Start(start)?.Dispose();
        for (var attempt = 0; attempt < 15 && pipe is null; attempt++) pipe = await Connect(key, 1000);
    }
    if (pipe is null) throw new IOException("无法连接后台。请确认 CLI 与托盘程序运行于同一用户及权限级别。");
    using (pipe)
    using (var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true))
    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(parsed.Request, Wire.Json).AsMemory(), timeout.Token);
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("后台未返回结果。");
        var response = JsonSerializer.Deserialize<CommandResponse>(line, Wire.Json) ?? throw new IOException("后台返回了无效结果。");
        if (parsed.Json) Console.WriteLine(JsonSerializer.Serialize(response, Wire.Json));
        else
        {
            Console.WriteLine(response.Message);
            if (response.State is { } state)
            {
                if (state.Simulation) Console.WriteLine("[模拟模式：不会操作真实电源]");
                Console.WriteLine($"防休眠：{(state.Awake ? "开启" : "关闭")} / 屏幕：{(state.Preferences.KeepScreenOn ? "保持亮起" : "允许熄灭")}");
                if (state.Shutdown is { } shutdown)
                    Console.WriteLine(shutdown.Phase == PlanPhase.Countdown ? $"关机倒计时：{state.CountdownSeconds} 秒" : $"计划：{shutdown.DueUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} 开始关机倒计时 ({shutdown.Source})");
                if (parsed.Request?.Command == "status") Console.WriteLine("最近事件：" + state.LastEvent.Message);
            }
        }
        return response.Success ? 0 : response.Code == "operation-failed" ? 6 : 5;
    }
}
catch (ArgumentException ex) { PrintError("invalid", ex.Message); return 2; }
catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
{ PrintError("unavailable", ex.Message); return 4; }

void PrintError(string code, string message)
{
    if (wantsJson) Console.WriteLine(JsonSerializer.Serialize(new CommandResponse(false, code, message), Wire.Json));
    else Console.Error.WriteLine(message);
}
static async Task<NamedPipeClientStream?> Connect(string name, int milliseconds)
{
    var stream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    try
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        await stream.ConnectAsync(timeout.Token);
        return stream;
    }
    catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
    { stream.Dispose(); return null; }
}

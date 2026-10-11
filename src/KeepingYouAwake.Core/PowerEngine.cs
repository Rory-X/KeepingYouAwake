namespace KeepingYouAwake.Core;

// Owned by one dispatcher thread, including calls to SetThreadExecutionState.
public sealed class PowerEngine
{
    public const int GraceSeconds = 60;
    private readonly IStateRepository repository;
    private readonly IPowerController power;
    private readonly TimeProvider clock;
    private Preferences preferences;
    private ShutdownPlan? plan;
    private bool manualAwake;
    private DateTimeOffset? awakeUntil;
    private StateEvent lastEvent;
    public event Action<StateEvent>? Notice;

    public PowerEngine(IStateRepository repository, IPowerController power, TimeProvider? clock = null)
    {
        this.repository = repository;
        this.power = power;
        this.clock = clock ?? TimeProvider.System;
        var saved = repository.Load();
        if (saved.SchemaVersion != 1) throw new InvalidDataException("不支持的数据版本。");
        preferences = saved.Preferences;
        plan = saved.Plan;
        lastEvent = new(Now, "ready", "已就绪，电脑遵循 Windows 电源设置。");
        if (plan is not null)
        {
            if (plan.Phase != PlanPhase.Pending || plan.DueUtc <= Now)
            {
                Commit(null);
                lastEvent = new(Now, "missed", "已取消过期或中断的关机计划。");
            }
            else lastEvent = new(Now, "restored", "已恢复尚未到期的关机计划。");
        }
        ApplyPower(manualAwake, plan, preferences);
    }

    private DateTimeOffset Now => clock.GetUtcNow();
    public AppStatus Status => new(manualAwake || plan is not null, manualAwake, plan is not null,
        awakeUntil, preferences, plan, lastEvent, power.Simulation,
        plan?.CountdownEndsUtc is { } end ? Math.Max(0, (int)Math.Ceiling((end - Now).TotalSeconds)) : 0);

    public CommandResponse Execute(CommandRequest request)
    {
        if (request.ProtocolVersion != 1) return Fail("protocol", "CLI 与后台版本不一致，请重新安装。");
        try
        {
            switch (request.Command)
            {
                case "status": return Ok("status", "状态已读取。");
                case "awake-on":
                    if (request.Seconds is { } seconds && (!double.IsFinite(seconds) || seconds <= 0 || seconds > 31536000))
                        return Fail("invalid", "防休眠时长必须大于零，且不超过 365 天。");
                    var nextPreferences = request.KeepScreenOn is { } screen ? preferences with { KeepScreenOn = screen } : preferences;
                    var end = request.Seconds is { } duration ? Now.AddSeconds(duration) : (DateTimeOffset?)null;
                    ApplyPower(true, plan, nextPreferences);
                    try { if (nextPreferences != preferences) Commit(plan, nextPreferences); }
                    catch { ApplyPower(manualAwake, plan, preferences); throw; }
                    manualAwake = true;
                    awakeUntil = end;
                    SetEvent("awake-on", end is null ? "已持续开启防休眠。" : $"防休眠将持续到 {end.Value.ToLocalTime():MM-dd HH:mm:ss}。");
                    return Ok("awake-on", lastEvent.Message);
                case "awake-off":
                    ApplyPower(false, plan, preferences);
                    manualAwake = false;
                    awakeUntil = null;
                    SetEvent("awake-off", plan is null ? "已关闭防休眠，恢复 Windows 电源策略。" : "已关闭手动防休眠；关机计划仍保持电脑运行。");
                    return Ok("awake-off", lastEvent.Message);
                case "screen":
                    if (request.Enabled is null) return Fail("invalid", "缺少屏幕设置。");
                    var screenPreferences = preferences with { KeepScreenOn = request.Enabled.Value };
                    ApplyPower(manualAwake, plan, screenPreferences);
                    try { Commit(plan, screenPreferences); }
                    catch { ApplyPower(manualAwake, plan, preferences); throw; }
                    SetEvent("settings", request.Enabled.Value ? "防休眠时同时保持屏幕亮起。" : "防休眠时允许屏幕熄灭。");
                    return Ok("settings", lastEvent.Message);
                case "shutdown":
                    if (plan is not null && !request.Replace) return Fail("conflict", "已有一个关机计划，请先取消，或显式替换。");
                    if (request.AtUtc is not null && request.Seconds is not null) return Fail("invalid", "关机时间与倒计时不能同时指定。");
                    if (request.Seconds is { } delay && (!double.IsFinite(delay) || delay < 0 || delay > 31536000))
                        return Fail("invalid", "关机延时必须为 0 到 365 天。");
                    var due = request.AtUtc ?? Now.AddSeconds(request.Seconds ?? 0);
                    if (request.AtUtc is not null && due <= Now) return Fail("invalid", "请选择未来的关机时间。");
                    if (due > Now.AddDays(365)) return Fail("invalid", "关机时间不能超过一年。");
                    var candidate = new ShutdownPlan(Guid.NewGuid(), due.ToUniversalTime(), request.Source == "ui" ? "ui" : "cli");
                    ApplyPower(manualAwake, candidate, preferences);
                    try { Commit(candidate); }
                    catch { ApplyPower(manualAwake, plan, preferences); throw; }
                    SetEvent("scheduled", $"将在 {due.ToLocalTime():MM-dd HH:mm:ss} 开始 60 秒关机倒计时。");
                    return Ok("scheduled", lastEvent.Message);
                case "cancel":
                    if (plan is null) return Ok("no-plan", "当前没有关机计划。");
                    Commit(null);
                    ApplyPower(manualAwake, null, preferences);
                    SetEvent("cancelled", "关机计划已取消。");
                    return Ok("cancelled", lastEvent.Message);
                default: return Fail("invalid", "不支持的命令。");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetEvent("error", ex.Message);
            return Fail("operation-failed", ex.Message);
        }
    }

    public void SetStartupPreference(bool enabled)
    {
        Commit(plan, preferences with { StartWithWindows = enabled });
        SetEvent("settings", enabled ? "已开启登录后启动。" : "已关闭登录后启动。");
    }

    public void Advance()
    {
        if (manualAwake && awakeUntil is { } end && Now >= end)
        {
            ApplyPower(false, plan, preferences);
            manualAwake = false;
            awakeUntil = null;
            SetEvent("awake-expired", "防休眠时长已结束，已恢复电源策略。");
        }
        if (plan is not { } current) return;
        if (current.Phase == PlanPhase.Pending && Now >= current.DueUtc)
        {
            if ((Now - current.DueUtc).TotalSeconds > 15) { CancelMissed(); return; }
            Commit(current with { Phase = PlanPhase.Countdown, CountdownEndsUtc = Now.AddSeconds(GraceSeconds) });
            SetEvent("countdown", "电脑将在 60 秒后关机。你可以取消；未保存内容仍可阻止关机。");
        }
        else if (current.Phase == PlanPhase.Countdown && current.CountdownEndsUtc is { } finish && Now >= finish)
        {
            if ((Now - finish).TotalSeconds > 15) { CancelMissed(); return; }
            // Persist removal before handing off, so a crash never repeats a shutdown request.
            Commit(null);
            try
            {
                power.RequestShutdown();
                SetEvent("shutdown-submitted", power.Simulation ? "模拟关机请求已完成，未操作真实电源。" : "已向 Windows 提交关机请求；应用可能因未保存内容阻止关机。");
            }
            catch (Exception ex)
            {
                SetEvent("shutdown-failed", "关机未能执行：" + ex.Message);
            }
            finally { ApplyPower(manualAwake, null, preferences); }
        }
    }

    public void Resumed()
    {
        if (plan is { } current && (current.Phase == PlanPhase.Countdown || current.DueUtc <= Now)) CancelMissed();
        Advance();
        ApplyPower(manualAwake, plan, preferences);
    }

    private void CancelMissed()
    {
        Commit(null);
        ApplyPower(manualAwake, null, preferences);
        SetEvent("missed", "已取消错过或中断的关机计划，电脑不会补执行关机。");
    }
    private void Commit(ShutdownPlan? next, Preferences? nextPreferences = null)
    {
        repository.Save(new(1, nextPreferences ?? preferences, next));
        plan = next;
        preferences = nextPreferences ?? preferences;
    }
    private void ApplyPower(bool manual, ShutdownPlan? scheduled, Preferences settings)
        => power.SetAwake(manual || scheduled is not null, (manual || scheduled is not null) && settings.KeepScreenOn);
    private void SetEvent(string kind, string message)
    {
        lastEvent = new(Now, kind, message);
        Notice?.Invoke(lastEvent);
    }
    private CommandResponse Ok(string code, string message) => new(true, code, message, Status);
    private CommandResponse Fail(string code, string message) => new(false, code, message, Status);
}

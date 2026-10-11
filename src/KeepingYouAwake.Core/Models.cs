using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeepingYouAwake.Core;

public enum PlanPhase { Pending, Countdown }
public sealed record Preferences(bool KeepScreenOn = false, bool StartWithWindows = false);
public sealed record ShutdownPlan(Guid Id, DateTimeOffset DueUtc, string Source,
    PlanPhase Phase = PlanPhase.Pending, DateTimeOffset? CountdownEndsUtc = null);
public sealed record SavedState(int SchemaVersion, Preferences Preferences, ShutdownPlan? Plan);
public sealed record StateEvent(DateTimeOffset TimeUtc, string Kind, string Message);
public sealed record AppStatus(bool Awake, bool ManualAwake, bool ScheduleAwake,
    DateTimeOffset? AwakeUntilUtc, Preferences Preferences, ShutdownPlan? Shutdown,
    StateEvent LastEvent, bool Simulation, int CountdownSeconds);
public sealed record CommandRequest(string Command, double? Seconds = null,
    DateTimeOffset? AtUtc = null, bool Replace = false, bool? Enabled = null,
    bool? KeepScreenOn = null, string Source = "cli", int ProtocolVersion = 1);
public sealed record CommandResponse(bool Success, string Code, string Message, AppStatus? State = null);

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string InstanceKey(string userId, string instance = "default")
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + instance)))[..24];
        return "KeepingYouAwake-" + hash;
    }
}

public interface IStateRepository
{
    SavedState Load();
    void Save(SavedState state);
}

public interface IPowerController : IDisposable
{
    bool Simulation { get; }
    void SetAwake(bool awake, bool keepScreenOn);
    void RequestShutdown();
}

using System.Text.Json;
using KeepingYouAwake.Core;

var tests = new (string Name, Action Test)[]
{
    ("manual wake allows screen off and expires", () =>
    {
        var f = new Fixture();
        Require(f.Engine.Execute(new("awake-on", 120)).Success);
        Require(f.Power.Awake && !f.Power.Screen);
        f.Clock.Add(120); f.Engine.Advance();
        Require(!f.Power.Awake && !f.Engine.Status.ManualAwake);
    }),
    ("manual state never survives restart", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("awake-on", KeepScreenOn: true));
        var next = f.Restart();
        Require(!next.Status.ManualAwake && !next.Status.Awake);
        Require(next.Status.Preferences.KeepScreenOn);
    }),
    ("scheduled and manual wake are independent leases", () =>
    {
        var f = new Fixture();
        f.Engine.Execute(new("awake-on")); f.Engine.Execute(new("shutdown", 600));
        f.Engine.Execute(new("awake-off"));
        Require(f.Engine.Status.Awake && f.Engine.Status.ScheduleAwake && !f.Engine.Status.ManualAwake);
        f.Engine.Execute(new("cancel")); Require(!f.Power.Awake);
    }),
    ("cancelling schedule preserves manual wake", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("awake-on")); f.Engine.Execute(new("shutdown", 100)); f.Engine.Execute(new("cancel"));
        Require(f.Power.Awake && f.Engine.Status.ManualAwake);
    }),
    ("conflicting request cannot replace without explicit permission", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown", 100)); var id = f.Engine.Status.Shutdown!.Id;
        var response = f.Engine.Execute(new("shutdown", 200)); Require(!response.Success && response.Code == "conflict");
        Require(f.Engine.Status.Shutdown.Id == id);
        Require(f.Engine.Execute(new("shutdown", 200, Replace: true)).Success); Require(f.Engine.Status.Shutdown.Id != id);
    }),
    ("shutdown requires full 60-second grace and submits once", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown", 30)); f.Clock.Add(30); f.Engine.Advance();
        Require(f.Engine.Status.CountdownSeconds == 60 && f.Power.Shutdowns == 0);
        f.Clock.Add(59); f.Engine.Advance(); Require(f.Power.Shutdowns == 0);
        f.Clock.Add(1); f.Engine.Advance(); Require(f.Power.Shutdowns == 1 && f.Engine.Status.Shutdown is null);
        f.Engine.Advance(); Require(f.Power.Shutdowns == 1 && !f.Power.Awake);
    }),
    ("cancel during countdown prevents native call", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown")); f.Engine.Advance();
        f.Clock.Add(59); f.Engine.Execute(new("cancel")); f.Clock.Add(5); f.Engine.Advance();
        Require(f.Power.Shutdowns == 0 && f.Engine.Status.Shutdown is null);
    }),
    ("manual wake does not block explicit shutdown", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("awake-on")); f.Engine.Execute(new("shutdown")); f.Engine.Advance();
        f.Clock.Add(60); f.Engine.Advance(); Require(f.Power.Shutdowns == 1);
    }),
    ("future saved schedule restores with wake lease", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown", 300)); var id = f.Engine.Status.Shutdown!.Id;
        var next = f.Restart(); Require(next.Status.Shutdown?.Id == id && next.Status.Awake);
    }),
    ("expired saved schedule is cancelled on restart", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown", 300)); f.Clock.Add(400); var next = f.Restart();
        Require(next.Status.Shutdown is null && next.Status.LastEvent.Kind == "missed" && f.Power.Shutdowns == 0);
    }),
    ("interrupted countdown never resumes on restart", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown")); f.Engine.Advance(); var next = f.Restart();
        Require(next.Status.Shutdown is null && f.Power.Shutdowns == 0);
    }),
    ("resume after deadline cancels without catch-up shutdown", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown", 100)); f.Clock.Add(101); f.Engine.Resumed();
        Require(f.Engine.Status.Shutdown is null && f.Power.Shutdowns == 0);
    }),
    ("resume interrupts in-progress countdown", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown")); f.Engine.Advance(); f.Clock.Add(2); f.Engine.Resumed();
        Require(f.Engine.Status.Shutdown is null && f.Power.Shutdowns == 0);
    }),
    ("long tick pause is treated as missed plan", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown", 100)); f.Clock.Add(120); f.Engine.Advance();
        Require(f.Engine.Status.Shutdown is null && f.Power.Shutdowns == 0);
    }),
    ("long pause at end of grace cannot cause surprise shutdown", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("shutdown")); f.Engine.Advance(); f.Clock.Add(100); f.Engine.Advance();
        Require(f.Engine.Status.Shutdown is null && f.Power.Shutdowns == 0);
    }),
    ("native failure is reported and not retried", () =>
    {
        var f = new Fixture(); f.Power.FailShutdown = true; f.Engine.Execute(new("shutdown")); f.Engine.Advance();
        f.Clock.Add(60); f.Engine.Advance(); f.Engine.Advance();
        Require(f.Engine.Status.LastEvent.Kind == "shutdown-failed" && f.Power.Shutdowns == 1 && f.Engine.Status.Shutdown is null);
    }),
    ("failed persistence does not accept a schedule or keep awake", () =>
    {
        var f = new Fixture(); f.Repository.FailSave = true;
        var response = f.Engine.Execute(new("shutdown", 60));
        Require(!response.Success && f.Engine.Status.Shutdown is null && !f.Power.Awake);
    }),
    ("failed native wake does not commit a schedule", () =>
    {
        var f = new Fixture(); f.Power.FailWake = true;
        Require(!f.Engine.Execute(new("shutdown", 60)).Success && f.Repository.State.Plan is null);
    }),
    ("screen preference applies immediately and clears when idle", () =>
    {
        var f = new Fixture(); f.Engine.Execute(new("awake-on")); f.Engine.Execute(new("screen", Enabled: true));
        Require(f.Power.Screen); f.Engine.Execute(new("awake-off")); Require(!f.Power.Screen && !f.Power.Awake);
    }),
    ("nonfinite durations, past deadlines and unknown protocols rejected", () =>
    {
        var f = new Fixture();
        Require(!f.Engine.Execute(new("shutdown", double.NaN)).Success);
        Require(!f.Engine.Execute(new("awake-on", -1)).Success);
        Require(!f.Engine.Execute(new("shutdown", AtUtc: f.Clock.GetUtcNow().AddSeconds(-1))).Success);
        Require(!f.Engine.Execute(new("shutdown", ProtocolVersion: 2)).Success);
        Require(f.Power.Shutdowns == 0);
    }),
    ("CLI parses global JSON, duration and replacement", () =>
    {
        var parsed = CommandLine.Parse(["--json", "shutdown", "--after", "2h", "--replace"]);
        Require(parsed.Json && parsed.Request!.Seconds == 7200 && parsed.Request.Replace);
        Require(CommandLine.Parse(["awake", "on", "--for", "1.5h", "--screen-off"]).Request?.KeepScreenOn == false);
    }),
    ("CLI refuses typos, conflicting flags and force shutdown", () =>
    {
        foreach (var arguments in new[] { new[] { "shutdown", "--force" }, ["shutdown", "--after", "1h", "--at", "23:00"],
            ["awake", "on", "--screen-on", "--screen-off"], ["awake", "off", "--for", "1h"], ["shutdown", "--after", "-3m"], ["status", "--json", "--json"] })
            Throws<ArgumentException>(() => CommandLine.Parse(arguments));
    }),
    ("wire protocol roundtrip keeps identifiers and countdown phase", () =>
    {
        var plan = new ShutdownPlan(Guid.NewGuid(), DateTimeOffset.UtcNow, "cli", PlanPhase.Countdown, DateTimeOffset.UtcNow.AddSeconds(60));
        var roundtrip = JsonSerializer.Deserialize<ShutdownPlan>(JsonSerializer.Serialize(plan, Wire.Json), Wire.Json);
        Require(roundtrip == plan);
        Require(Wire.InstanceKey("user", "default") != Wire.InstanceKey("user", "test"));
    })
};
var failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed. No real power operations performed.");
return failures == 0 ? 0 : 1;
static void Require(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

sealed class Fixture
{
    public ManualClock Clock { get; } = new();
    public MemoryRepository Repository { get; } = new();
    public FakePower Power { get; } = new();
    public PowerEngine Engine { get; }
    public Fixture() => Engine = new(Repository, Power, Clock);
    public PowerEngine Restart() => new(Repository, Power, Clock);
}
sealed class ManualClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-10-11T10:00:00Z");
    public override DateTimeOffset GetUtcNow() => now;
    public void Add(double seconds) => now = now.AddSeconds(seconds);
}
sealed class MemoryRepository : IStateRepository
{
    public SavedState State { get; private set; } = new(1, new(), null);
    public bool FailSave { get; set; }
    public SavedState Load() => State;
    public void Save(SavedState state) { if (FailSave) throw new IOException("disk failure"); State = state; }
}
sealed class FakePower : IPowerController
{
    public bool Simulation => true;
    public bool Awake { get; private set; }
    public bool Screen { get; private set; }
    public int Shutdowns { get; private set; }
    public bool FailShutdown { get; set; }
    public bool FailWake { get; set; }
    public void SetAwake(bool awake, bool keepScreenOn)
    { if (FailWake) throw new InvalidOperationException("wake failure"); Awake = awake; Screen = keepScreenOn; }
    public void RequestShutdown() { Shutdowns++; if (FailShutdown) throw new InvalidOperationException("denied"); }
    public void Dispose() { }
}

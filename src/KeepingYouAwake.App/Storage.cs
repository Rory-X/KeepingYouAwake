using System.Text;
using System.Text.Json;
using KeepingYouAwake.Core;
using Microsoft.Win32;

namespace KeepingYouAwake.App;

internal sealed class JsonRepository : IStateRepository
{
    private readonly string path;
    public string? RecoveryMessage { get; private set; }
    public JsonRepository(string directory) { Directory.CreateDirectory(directory); path = Path.Combine(directory, "state.json"); }
    public SavedState Load()
    {
        if (!File.Exists(path)) return new(1, new(), null);
        try
        {
            var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path), Wire.Json) ?? throw new InvalidDataException();
            if (state.SchemaVersion != 1 || state.Preferences is null) throw new InvalidDataException();
            if (state.Plan is { } plan && (plan.Id == Guid.Empty || !Enum.IsDefined(plan.Phase) || (plan.Phase == PlanPhase.Countdown && plan.CountdownEndsUtc is null))) throw new InvalidDataException();
            return state;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            File.Move(path, path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            RecoveryMessage = "设置文件损坏，已保留备份并恢复默认；未执行关机。";
            return new(1, new(), null);
        }
    }
    public void Save(SavedState state)
    {
        var temporary = path + ".tmp";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, Wire.Json));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
}

internal sealed class EventLog
{
    private readonly string path;
    public EventLog(string directory) { Directory.CreateDirectory(directory); path = Path.Combine(directory, "events.jsonl"); }
    public void Write(StateEvent entry)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
            { File.Move(path, path + ".previous", true); }
            File.AppendAllText(path, JsonSerializer.Serialize(entry, Wire.Json) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) key.SetValue("KeepingYouAwake", "\"" + Environment.ProcessPath + "\" --background", RegistryValueKind.String);
        else key.DeleteValue("KeepingYouAwake", false);
    }
    public static bool Enabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue("KeepingYouAwake") is string;
    }
}

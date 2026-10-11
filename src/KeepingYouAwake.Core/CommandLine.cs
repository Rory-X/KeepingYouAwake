using System.Globalization;

namespace KeepingYouAwake.Core;

public sealed record ParsedCommand(CommandRequest? Request, bool Json, string Instance, bool Help = false, bool Version = false);

public static class CommandLine
{
    public static ParsedCommand Parse(string[] args, DateTimeOffset? now = null)
    {
        var tokens = args.ToList();
        var json = ExtractSwitch(tokens, "--json");
        var instance = ExtractValue(tokens, "--instance") ?? "default";
        if (instance.Length is < 1 or > 64 || instance.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("实例名只能包含字母、数字、短横线或下划线。");
        if (tokens.Count == 0 || tokens[0] is "help" or "--help" or "-h") return new(null, json, instance, Help: true);
        if (tokens[0] is "version" or "--version") return new(null, json, instance, Version: true);
        var command = tokens[0];
        tokens.RemoveAt(0);
        CommandRequest request;
        switch (command)
        {
            case "status": case "cancel": case "show":
                request = new(command);
                break;
            case "awake":
                if (tokens.Count == 0 || tokens[0] is not ("on" or "off")) throw new ArgumentException("用法：kya awake on|off [--for 2h] [--screen-on|--screen-off]");
                var on = tokens[0] == "on";
                tokens.RemoveAt(0);
                var durationText = ExtractValue(tokens, "--for");
                var screenOn = ExtractSwitch(tokens, "--screen-on");
                var screenOff = ExtractSwitch(tokens, "--screen-off");
                if (screenOn && screenOff) throw new ArgumentException("屏幕选项不能同时指定。");
                if (!on && (durationText is not null || screenOn || screenOff)) throw new ArgumentException("awake off 不接受时长或屏幕选项。");
                request = new(on ? "awake-on" : "awake-off", durationText is null ? null : DurationSeconds(durationText, false),
                    KeepScreenOn: screenOn ? true : screenOff ? false : null);
                break;
            case "shutdown":
                var after = ExtractValue(tokens, "--after");
                var at = ExtractValue(tokens, "--at");
                var replace = ExtractSwitch(tokens, "--replace");
                if (after is not null && at is not null) throw new ArgumentException("--after 和 --at 不能同时使用。");
                request = new("shutdown", after is not null ? DurationSeconds(after, true) : at is null ? 0 : null,
                    at is null ? null : ParseAt(at, now ?? DateTimeOffset.Now), replace);
                break;
            default: throw new ArgumentException("未知命令。运行 kya --help 查看用法。");
        }
        if (tokens.Count != 0) throw new ArgumentException("无法识别的参数：" + string.Join(' ', tokens));
        return new(request, json, instance);
    }

    public static double DurationSeconds(string text, bool allowZero)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请输入时长，例如 30m 或 2h。");
        text = text.Trim().ToLowerInvariant();
        var unit = char.IsLetter(text[^1]) ? text[^1] : 'm';
        var number = char.IsLetter(text[^1]) ? text[..^1] : text;
        var multiplier = unit switch { 's' => 1, 'm' => 60, 'h' => 3600, 'd' => 86400, _ => throw new ArgumentException("时长单位仅支持 s、m、h、d。") };
        if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) throw new ArgumentException("无效时长。");
        var seconds = value * multiplier;
        if (!double.IsFinite(seconds) || seconds < (allowZero ? 0 : 1) || seconds > 31536000) throw new ArgumentException("时长必须在有效范围内，最长 365 天。");
        return seconds;
    }

    public static DateTimeOffset ParseAt(string text, DateTimeOffset now)
    {
        if (TimeOnly.TryParseExact(text, ["HH:mm", "HH:mm:ss", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            var localNow = now.ToLocalTime();
            var day = localNow.Date.Add(time.ToTimeSpan());
            if (day <= localNow.DateTime) day = day.AddDays(1);
            return new DateTimeOffset(DateTime.SpecifyKind(day, DateTimeKind.Local)).ToUniversalTime();
        }
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var parsed))
            throw new ArgumentException("时间格式：HH:mm、yyyy-MM-dd HH:mm 或带时区的 ISO 8601。");
        if (parsed <= now) throw new ArgumentException("关机时间必须在未来。");
        return parsed.ToUniversalTime();
    }

    private static bool ExtractSwitch(List<string> tokens, string option)
    {
        var index = tokens.IndexOf(option);
        if (index < 0) return false;
        tokens.RemoveAt(index);
        return true;
    }
    private static string? ExtractValue(List<string> tokens, string option)
    {
        var index = tokens.IndexOf(option);
        if (index < 0) return null;
        if (index + 1 >= tokens.Count || tokens[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException(option + " 缺少参数值。");
        var value = tokens[index + 1];
        tokens.RemoveRange(index, 2);
        return value;
    }
}

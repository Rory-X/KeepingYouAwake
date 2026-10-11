using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using KeepingYouAwake.Core;

namespace KeepingYouAwake.App;

internal sealed class PipeServer : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task task;
    public PipeServer(string name, Dispatcher dispatcher, Func<CommandRequest, CommandResponse> handle)
    {
        task = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                    // Bound both the line length and the time a client can monopolize the pipe.
                    var buffer = new StringBuilder();
                    var character = new char[1];
                    while (buffer.Length <= 16384)
                    {
                        var count = await reader.ReadAsync(character.AsMemory(), timeout.Token);
                        if (count == 0 || character[0] == '\n') break;
                        if (character[0] != '\r') buffer.Append(character[0]);
                    }
                    CommandResponse response;
                    try
                    {
                        if (buffer.Length > 16384) throw new JsonException("请求过长。");
                        var request = JsonSerializer.Deserialize<CommandRequest>(buffer.ToString(), Wire.Json) ?? throw new JsonException("请求为空。");
                        response = await dispatcher.InvokeAsync(() => handle(request), DispatcherPriority.Normal, timeout.Token);
                    }
                    catch (JsonException) { response = new(false, "invalid", "无效请求。"); }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, Wire.Json).AsMemory(), timeout.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { }
            }
        });
    }
    public void Dispose() { stop.Cancel(); _ = task; }
}

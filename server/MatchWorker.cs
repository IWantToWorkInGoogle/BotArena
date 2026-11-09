using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace BotArena;

internal record MatchWorkerResult(MatchResponse? Response, string? Error);

internal static class MatchWorker
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunChildAsync()
    {
        var request = await JsonSerializer.DeserializeAsync<MatchRequest>(Console.OpenStandardInput(), Json);
        if (request?.BotCode is null || request.OpponentId is null) return 1;

        MatchWorkerResult result;
        try
        {
            using var bot = new JavaScriptBot(request.BotCode);
            result = new(Match.Run(bot, request.OpponentId), null);
        }
        catch (StrategyException error)
        {
            result = new(null, error.Message);
        }
        await JsonSerializer.SerializeAsync(Console.OpenStandardOutput(), result, Json);
        return 0;
    }

    public static async Task<MatchWorkerResult> RunAsync(MatchRequest request, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet")
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--match-worker");
        // Keep the worker's managed heap bounded even when a strategy retains state across ticks.
        start.Environment["DOTNET_GCHeapHardLimit"] = "0x8000000";
        start.Environment["DOTNET_gcServer"] = "0";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start match worker.");
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, request, Json, timeout.Token);
            process.StandardInput.Close();

            var result = await JsonSerializer.DeserializeAsync<MatchWorkerResult>(
                process.StandardOutput.BaseStream, Json, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 && result is not null ? result : WorkerFailed();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new(null, "Матч превысил лимит в 5 секунд.");
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            cancellation.ThrowIfCancellationRequested();
            return WorkerFailed();
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* The worker already exited. */ }
            }
            await process.WaitForExitAsync();
            await stderr;
        }
    }

    private static MatchWorkerResult WorkerFailed() => new(null,
        "Процесс симуляции завершился досрочно. Проверьте стратегию и повторите запуск.");
}

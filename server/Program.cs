using BotArena;

if (args is ["--match-worker"]) return await MatchWorker.RunChildAsync();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5050");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 128 * 1024);

var app = builder.Build();
if (Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

using var matchSlot = new SemaphoreSlim(1);
app.MapGet("/api/opponents", () => Match.Opponents);
app.MapPost("/api/matches", async (MatchRequest request, CancellationToken cancellation) =>
{
    if (string.IsNullOrWhiteSpace(request.BotCode) || request.BotCode.Length > 16_384)
        return Results.BadRequest(new { error = "Нужен код стратегии длиной от 1 до 16 384 символов." });

    if (!Match.Opponents.Any(opponent => opponent.Id == request.OpponentId))
        return Results.BadRequest(new { error = "Выберите существующего противника." });

    if (!matchSlot.Wait(0))
        return Results.Json(new { error = "Идёт расчёт другого матча. Повторите запрос." }, statusCode: 429);

    try
    {
        var result = await MatchWorker.RunAsync(request, cancellation);
        return result.Response is { } response
            ? Results.Ok(response)
            : Results.UnprocessableEntity(new { error = result.Error });
    }
    finally
    {
        matchSlot.Release();
    }
});

await app.RunAsync();
return 0;

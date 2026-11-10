using BotArena;

if (args is ["--match-worker"]) return await MatchWorker.RunChildAsync();

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["urls"] is null)
    builder.WebHost.UseUrls("http://127.0.0.1:5050");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 128 * 1024);
builder.Services.AddSingleton<MatchQueue>();
builder.Services.AddHostedService(services => services.GetRequiredService<MatchQueue>());

var app = builder.Build();
if (Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapGet("/api/opponents", () => OpponentCatalog.Opponents);
app.MapGet("/api/strategy-languages", () => StrategyLanguages.Available());
app.MapPost("/api/match-jobs", (MatchRequest request, MatchQueue queue) =>
{
    if (Validate(request) is { } invalid) return invalid;
    if (queue.Enqueue(request) is not { } job) return QueueFull();
    return Results.Accepted($"/api/match-jobs/{job.Id}", new { id = job.Id });
});
app.MapGet("/api/match-jobs/{id:guid}", (Guid id, MatchQueue queue, HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";
    return queue.GetStatus(id) is { } status ? Results.Ok(status) : Results.NotFound();
});
app.MapPost("/api/matches", async (MatchRequest request, MatchQueue queue, CancellationToken cancellation) =>
{
    if (Validate(request) is { } invalid) return invalid;
    if (queue.Enqueue(request, cancellation) is not { } job) return QueueFull();

    using var registration = cancellation.Register(() => queue.CancelWaiting(job));
    var result = await job.Completion.Task;
    return result.Response is { } response
        ? Results.Ok(response)
        : Results.UnprocessableEntity(new { error = result.Error });
});

await app.RunAsync();
return 0;

static IResult? Validate(MatchRequest request)
{
    if (string.IsNullOrWhiteSpace(request.BotCode) || request.BotCode.Length > 16_384)
        return Results.BadRequest(new { error = "Нужен код стратегии длиной от 1 до 16 384 символов." });
    if (MatchSetup.GetOpponents(request) is null)
        return Results.BadRequest(new { error = "Выберите от одного до трёх соперников с доступными моделями движения." });
    if (request.Language is { } language && !StrategyLanguages.Available().Contains(language))
        return Results.BadRequest(new { error = "Выбранный язык стратегии недоступен." });
    if (!MovementCatalog.Supports(request.Movement))
        return Results.BadRequest(new { error = "Выбранная модель движения недоступна." });
    return null;
}

static IResult QueueFull() => Results.Json(new { error = "Очередь расчётов заполнена." }, statusCode: 429);

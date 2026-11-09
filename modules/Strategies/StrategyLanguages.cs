namespace BotArena;

public static class StrategyLanguages
{
    public const string JavaScript = "javascript";
    public const string Python = "python";
    private const string PythonRuntimeVariable = "BOTARENA_PYTHON_RUNTIME";

    private static string? PythonRuntime => Environment.GetEnvironmentVariable(PythonRuntimeVariable);

    public static IReadOnlyList<string> Available() =>
        PythonBot.IsAvailable(PythonRuntime) ? [JavaScript, Python] : [JavaScript];

    public static IBotRuntime Create(string? language, string code) => language switch
    {
        null or JavaScript => new JavaScriptBot(code),
        Python => new PythonBot(code, PythonRuntime),
        _ => throw new StrategyException("Язык стратегии не поддерживается."),
    };
}

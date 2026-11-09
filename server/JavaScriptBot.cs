using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace BotArena;

public interface IBotRuntime : IDisposable
{
    BotAction Decide(BotObservation observation);
}

public sealed class StrategyException(string message, int? tick = null, int? line = null, int? column = null) : Exception(message)
{
    public int? Tick { get; } = tick;
    public int? Line { get; } = line;
    public int? Column { get; } = column;
}

public sealed class JavaScriptBot : IBotRuntime
{
    private const string StrategySource = "strategy.js";

    private readonly Engine engine = new(options =>
    {
        options.Strict();
        options.MaxStatements(8_000);
        options.LimitMemory(4_000_000);
        options.TimeoutInterval(TimeSpan.FromMilliseconds(100));
        options.LimitRecursion(48);
        options.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(30);
        options.Constraints.MaxArraySize = 20_000;
        options.Host.StringCompilationAllowed = false;
    });
    private readonly JsValue execute;
    private readonly JsValue tick;

    public JavaScriptBot(string source)
    {
        try
        {
            // Capture the intrinsics before user code can change them. Read user getters
            // inside Invoke so property access consumes the same budget as tick().
            execute = engine.Evaluate("""
                ((parse, stringify, finite, isArray, min, max) => (tick, input) => {
                    const action = tick(parse(input));
                    if (action === null || typeof action !== 'object' ||
                        isArray(action) || typeof action.then === 'function') return '';
                    const left = action.leftTrack;
                    const right = action.rightTrack;
                    const aim = action.aimDirection;
                    if (aim !== undefined && (aim === null || typeof aim !== 'object' || isArray(aim))) return '';
                    const x = aim === undefined ? 0 : aim.x;
                    const y = aim === undefined ? 0 : aim.y;
                    const shoot = action.shoot;
                    if ((left !== undefined && !finite(left)) || (right !== undefined && !finite(right)) || !finite(x) || !finite(y) ||
                        (shoot !== undefined && typeof shoot !== 'boolean')) return '';
                    return stringify({
                        __proto__: null,
                        leftTrack: min(1, max(-1, left ?? 0)),
                        rightTrack: min(1, max(-1, right ?? 0)),
                        aimDirection: { __proto__: null, x, y },
                        shoot: shoot === true
                    });
                })(JSON.parse, JSON.stringify, Number.isFinite, Array.isArray, Math.min, Math.max)
                """);
            engine.Execute(source, StrategySource);
            tick = engine.Evaluate("typeof tick === 'function' ? tick : null");
            if (tick.IsNull()) throw new StrategyException("Объявите функцию tick(state).");
        }
        catch (Exception error)
        {
            engine.Dispose();
            if (!IsScriptError(error)) throw;
            throw Describe(error, "при загрузке стратегии");
        }
    }

    public BotAction Decide(BotObservation observation)
    {
        try
        {
            var input = JsonSerializer.Serialize(observation, MatchWorker.Json);
            var output = engine.Invoke(execute, tick, new JsString(input)).AsString();
            if (output.Length == 0)
            {
                throw new StrategyException(
                    "tick(state) должна вернуть объект с числовыми leftTrack, rightTrack и aimDirection { x, y }, а также логическим shoot.", observation.Tick);
            }
            return JsonSerializer.Deserialize<BotAction>(output, MatchWorker.Json)!;
        }
        catch (Exception error) when (IsScriptError(error))
        {
            throw Describe(error, "при вызове tick(state)", observation.Tick);
        }
    }

    private static bool IsScriptError(Exception error) =>
        error is JavaScriptException or TimeoutException or RegexMatchTimeoutException
            or StatementsCountOverflowException or MemoryLimitExceededException or RecursionDepthOverflowException
            or Acornima.ParseErrorException;

    private static StrategyException Describe(Exception error, string phase, int? tick = null)
    {
        if (error is JavaScriptException scriptError)
        {
            var message = $"Ошибка JavaScript {phase}";
            var location = scriptError.Location;
            if (location.SourceFile == StrategySource && location.Start.Line > 0)
            {
                var line = location.Start.Line;
                var column = location.Start.Column + 1;
                return new($"{message}, строка {line}, столбец {column}.", tick, line, column);
            }
            return new($"{message}.", tick);
        }
        if (error is Acornima.ParseErrorException)
            return new($"Синтаксическая ошибка {phase}.", tick);
        return new($"Превышен лимит исполнения {phase}.", tick);
    }

    public void Dispose() => engine.Dispose();
}

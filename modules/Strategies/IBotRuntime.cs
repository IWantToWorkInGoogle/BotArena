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

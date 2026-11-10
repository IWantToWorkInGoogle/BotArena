namespace BotArena;

internal static class Match
{
    public static MatchResponse Run(MatchConfig config, IReadOnlyDictionary<string, IBotRuntime> runtimes)
    {
        var duel = new Duel(config);
        var frames = new List<GameFrame> { duel.CaptureFrame() };
        var actions = new Dictionary<string, BotAction>();
        var failures = new List<string>();
        ScriptError? error = null;
        while (duel.Result is null)
        {
            actions.Clear();
            failures.Clear();
            foreach (var bot in frames[^1].Bots)
            {
                if (bot.Hp == 0 || bot.Forfeited) continue;
                try
                {
                    actions[bot.Id] = runtimes[bot.Id].Decide(duel.Observe(bot.Id));
                }
                catch (StrategyException failure)
                {
                    failures.Add(bot.Id);
                    error ??= new(bot.Id, failure.Tick, failure.Message, failure.Line, failure.Column);
                }
            }
            if (failures.Count > 0)
            {
                duel.ForfeitAll(failures);
                frames[^1] = duel.CaptureFrame();
                if (duel.Result is not null) break;
            }
            duel.Step(actions);
            frames.Add(duel.CaptureFrame());
        }
        return new(new(config.TickRate, config.Arena, frames, duel.Result!), error);
    }
}

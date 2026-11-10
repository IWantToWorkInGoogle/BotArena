namespace BotArena;

internal static class Match
{
    public static MatchResponse Run(MatchConfig config, IReadOnlyDictionary<string, IBotRuntime> runtimes)
    {
        var duel = new Duel(config);
        var frames = new List<GameFrame> { duel.CaptureFrame() };
        var behaviorSamples = new List<BehaviorSample>();
        var actions = new Dictionary<string, BotAction>();
        var failures = new List<string>();
        ScriptError? error = null;
        while (duel.Result is null)
        {
            actions.Clear();
            failures.Clear();
            var tickSamples = new List<BehaviorSample>();
            foreach (var bot in frames[^1].Bots)
            {
                if (bot.Hp == 0 || bot.Forfeited) continue;
                try
                {
                    var observation = duel.Observe(bot.Id);
                    var action = runtimes[bot.Id].Decide(observation);
                    actions[bot.Id] = action;
                    tickSamples.Add(new(observation.Tick, bot.Id, observation.Self.Position,
                        observation.Self.BodyAngle, observation.Self.TurretAngle,
                        observation.Enemy is not null, action));
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
            behaviorSamples.AddRange(tickSamples);
            frames.Add(duel.CaptureFrame());
        }
        return new(new(config.TickRate, config.Arena, frames, duel.Result!, behaviorSamples), error);
    }
}

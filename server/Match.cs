namespace BotArena;

public static class Match
{
    private const string PlayerId = "player";
    private const string OpponentId = "opponent";

    private static readonly (Opponent Info, string Script)[] Catalog =
    [
        (new("aggressive", "Штурмовик", "Обходит центральную стену сверху и сближается."),
        """
        function tick(state) {
            const { self, enemy } = state;
            const { x, y } = self.position;
            const waypoint = y > 3.85 && x > 9.3 ? { x: 10, y: 3.6 } : { x: 3.3, y: 3.65 };
            return drive(self, enemy && distance(self, enemy.position) > 2.7 ? enemy.position :
                enemy ? self.position : waypoint, enemy);
        }
        """),
        (new("defensive", "Дозорный", "Меняет позицию за укрытием и стреляет при удобном угле."),
        """
        function tick(state) {
            const { self, enemy } = state;
            const { x, y } = self.position;
            const upper = Math.floor(state.tick / 95) % 2 === 0;
            const waypoint = x > 12 && y > 5.9 ? { x: 12, y: 5.1 } :
                { x: 10.05, y: upper ? 3.55 : 6.28 };
            return drive(self, waypoint, enemy);
        }
        """)
    ];

    private const string Driving = """
        function distance(self, point) {
            return Math.hypot(point.x - self.position.x, point.y - self.position.y);
        }

        function wrapped(angle) {
            return Math.atan2(Math.sin(angle), Math.cos(angle));
        }

        function drive(self, waypoint, enemy) {
            const dx = waypoint.x - self.position.x;
            const dy = waypoint.y - self.position.y;
            const error = wrapped(Math.atan2(dy, dx) - self.bodyAngle);
            const turn = Math.hypot(dx, dy) < 0.15 ? 0 : Math.max(-1, Math.min(1, error * 2));
            const throttle = Math.abs(error) < 0.7 ? Math.min(1, Math.hypot(dx, dy) * 2) : 0;
            const aim = enemy ? {
                x: enemy.position.x - self.position.x,
                y: enemy.position.y - self.position.y
            } : { x: dx, y: dy };
            const aligned = Math.abs(wrapped(Math.atan2(aim.y, aim.x) - self.turretAngle)) < 0.055;
            return {
                leftTrack: throttle + turn,
                rightTrack: throttle - turn,
                aimDirection: aim,
                shoot: !!enemy && aligned && self.canShoot
            };
        }
        """;

    public static IReadOnlyList<Opponent> Opponents { get; } =
        Array.AsReadOnly(Catalog.Select(item => item.Info).ToArray());

    public static MatchResponse Run(IBotRuntime player, string opponentId)
    {
        using var opponent = new JavaScriptBot(Catalog.Single(item => item.Info.Id == opponentId).Script + "\n" + Driving);
        var config = MatchConfig.Classic;
        var duel = new Duel(config);
        var frames = new List<GameFrame> { duel.CaptureFrame() };
        var actions = new Dictionary<string, BotAction>();
        ScriptError? error = null;
        while (duel.Result is null)
        {
            var decidingBot = PlayerId;
            try
            {
                actions[PlayerId] = player.Decide(duel.Observe(PlayerId));
                decidingBot = OpponentId;
                actions[OpponentId] = opponent.Decide(duel.Observe(OpponentId));
            }
            catch (StrategyException failure)
            {
                duel.Forfeit(decidingBot);
                error = new(decidingBot, failure.Tick, failure.Message, failure.Line, failure.Column);
                // The failed decision never advances the world. Record the finish on its current tick.
                frames[^1] = duel.CaptureFrame();
                break;
            }
            duel.Step(actions);
            frames.Add(duel.CaptureFrame());
        }
        return new(new(config.TickRate, config.Arena, frames, duel.Result!), error);
    }
}

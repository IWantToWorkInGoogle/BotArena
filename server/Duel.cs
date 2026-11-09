namespace BotArena;

public record BotSpawn(string Id, Vec2 Position, double BodyAngle, double TurretAngle);

public sealed record MatchConfig(int TickRate, int MaxTicks, ArenaState Arena, IReadOnlyList<BotSpawn> Spawns)
{
    public double MaxForwardSpeed { get; init; } = 3.6;
    public double MaxReverseSpeed { get; init; } = 2.0;
    public double Acceleration { get; init; } = 8.0;
    public double Deceleration { get; init; } = 12.0;
    public double MaxTurnSpeed { get; init; } = 3.0;
    public double MaxTurretTurnSpeed { get; init; } = 6.0;
    public double BotLength { get; init; } = 0.68;
    public double BotWidth { get; init; } = 0.58;
    public double BulletLength { get; init; } = 0.19;
    public double BulletWidth { get; init; } = 0.09;
    public double BulletSpeed { get; init; } = 13.0;
    public int BulletDamage { get; init; } = 20;
    public int ShotCooldownTicks { get; init; } = 12;

    public static MatchConfig Classic { get; } = new(20, 600, new(16, 10, Array.AsReadOnly(new Wall[]
    {
        new(4.5, 1.9, 2.0, 1.3),
        new(7.25, 4.15, 1.5, 1.7),
        new(9.5, 6.8, 2.0, 1.3)
    })), Array.AsReadOnly(new BotSpawn[]
    {
        new("player", new(2, 5), 0, 0),
        new("opponent", new(14, 5), Math.PI, Math.PI)
    }));
}

public interface ISimulationEngine
{
    BotObservation Observe(string botId);
    void Step(IReadOnlyDictionary<string, BotAction> actions);
    GameFrame CaptureFrame();
    MatchResult? Result { get; }
}

public sealed class Duel(MatchConfig config) : ISimulationEngine
{
    private double StepSeconds => 1.0 / config.TickRate;
    private BotState[] bots = config.Spawns.Select(s =>
        new BotState(s.Id, s.Position, s.BodyAngle, s.TurretAngle, 0, 100, 100, 0)).ToArray();
    private readonly Hitbox[] walls = config.Arena.Walls.Select(w =>
        new Hitbox(new(w.X + w.Width / 2, w.Y + w.Height / 2), 0, w.Width, w.Height)).ToArray();
    private readonly List<BulletState> bullets = [];
    private readonly List<GameEvent> events = [];
    private readonly int[] cooldowns = new int[2];
    private int nextBullet;
    private int tick;

    public MatchResult? Result { get; private set; }

    public BotObservation Observe(string botId)
    {
        var index = Array.FindIndex(bots, bot => bot.Id == botId);
        if (index < 0) throw new ArgumentException("Unknown bot ID.", nameof(botId));
        var self = bots[index];
        var enemy = bots[1 - index];
        var sightLine = new Hitbox(self.Position, 0, 0, 0);
        var visible = WallImpact(sightLine, enemy.Position - self.Position) > 1;
        return new(tick, self, visible ? enemy : null, [.. bullets], config.Arena);
    }

    public GameFrame CaptureFrame() => new(tick, [.. bots], [.. bullets], [.. events]);

    public void Step(IReadOnlyDictionary<string, BotAction> actions)
    {
        if (Result is not null) throw new InvalidOperationException("The duel is finished.");
        var firstAction = actions[bots[0].Id];
        var secondAction = actions[bots[1].Id];
        events.Clear();
        tick++;

        var first = Advance(bots[0], firstAction);
        var second = Advance(bots[1], secondAction);
        var firstStart = Body(first with { Position = bots[0].Position });
        var secondStart = Body(second with { Position = bots[1].Position });
        var relativeMotion = (first.Position - bots[0].Position) - (second.Position - bots[1].Position);
        if (firstStart.Impact(secondStart, relativeMotion) <= 1)
        {
            first = first with { Position = bots[0].Position, BodyAngle = bots[0].BodyAngle, Speed = 0 };
            second = second with { Position = bots[1].Position, BodyAngle = bots[1].BodyAngle, Speed = 0 };
        }
        bots = [first, second];
        Fire(0, firstAction);
        Fire(1, secondAction);
        AdvanceBullets();

        var destroyed = bots.Any(b => b.Hp == 0);
        if (destroyed || tick == config.MaxTicks)
        {
            string? winner = bots[0].Hp == bots[1].Hp ? null : bots[0].Hp > bots[1].Hp ? bots[0].Id : bots[1].Id;
            Finish(new(winner, destroyed ? MatchEndReason.BotDestroyed : MatchEndReason.TimeLimit, tick));
        }
    }

    public void Forfeit(string botId) =>
        Finish(new(bots.Single(bot => bot.Id != botId).Id, MatchEndReason.BotError, tick));

    private void Finish(MatchResult result)
    {
        Result = result;
        events.Add(new(tick, GameEventType.MatchFinished));
    }

    private BotState Advance(BotState bot, BotAction action)
    {
        var left = Track(action.LeftTrack);
        var right = Track(action.RightTrack);
        var throttle = (left + right) / 2;
        var targetSpeed = throttle * (throttle < 0 ? config.MaxReverseSpeed : config.MaxForwardSpeed);
        var braking = Math.Abs(targetSpeed) < Math.Abs(bot.Speed) || targetSpeed * bot.Speed < 0;
        var speedDelta = (braking ? config.Deceleration : config.Acceleration) * StepSeconds;
        var speed = bot.Speed + Math.Clamp(targetSpeed - bot.Speed, -speedDelta, speedDelta);
        var bodyAngle = NormalizeAngle(bot.BodyAngle + (left - right) * 0.5 * config.MaxTurnSpeed * StepSeconds);
        var turretAngle = TurnTurret(bot.TurretAngle, action.AimDirection);
        var hull = Body(bot with { BodyAngle = bodyAngle });
        if (!Inside(hull) || WallImpact(hull, default) <= 1)
        {
            hull = Body(bot);
            bodyAngle = bot.BodyAngle;
        }

        var delta = Vec2.FromAngle(bodyAngle) * (speed * StepSeconds);
        var destination = hull with { Center = hull.Center + delta };
        if (!Inside(destination) || WallImpact(hull, delta) <= 1)
            return bot with { BodyAngle = bodyAngle, TurretAngle = turretAngle, Speed = 0 };

        return bot with { Position = destination.Center, BodyAngle = bodyAngle, TurretAngle = turretAngle, Speed = speed };
    }

    private double TurnTurret(double current, Vec2 aim)
    {
        if (aim == default || !double.IsFinite(aim.X) || !double.IsFinite(aim.Y)) return current;
        var turn = NormalizeAngle(Math.Atan2(aim.Y, aim.X) - current);
        var limit = config.MaxTurretTurnSpeed * StepSeconds;
        return NormalizeAngle(current + Math.Clamp(turn, -limit, limit));
    }

    private void Fire(int index, BotAction action)
    {
        var bot = bots[index];
        if (action.Shoot && bot.CanShoot)
        {
            var direction = Vec2.FromAngle(bot.TurretAngle);
            var bullet = new BulletState(++nextBullet, bot.Id, bot.Position, direction);
            bullets.Add(bullet);
            cooldowns[index] = config.ShotCooldownTicks;
            events.Add(new(tick, GameEventType.ShotFired, bot.Id, BulletId: bullet.Id, Position: bot.Position));
        }
        cooldowns[index] = Math.Max(0, cooldowns[index] - 1);
        bots[index] = bot with { ReloadingTime = cooldowns[index] * StepSeconds };
    }

    private void AdvanceBullets()
    {
        var survivors = new List<BulletState>();
        foreach (var bullet in bullets)
        {
            var motion = bullet.Direction * (config.BulletSpeed * StepSeconds);
            var box = new Hitbox(bullet.Position, Math.Atan2(bullet.Direction.Y, bullet.Direction.X),
                config.BulletLength, config.BulletWidth);
            var targetIndex = bots[0].Id == bullet.OwnerBotId ? 1 : 0;
            var target = bots[targetIndex];
            var hit = box.Impact(Body(target), motion);
            var wall = WallImpact(box, motion);

            if (hit <= 1 && hit < wall)
            {
                var impact = bullet.Position + motion * hit;
                var damage = Math.Min(target.Hp, config.BulletDamage);
                bots[targetIndex] = target with { Hp = target.Hp - damage };
                if (damage > 0)
                {
                    events.Add(new(tick, GameEventType.BotHit, bullet.OwnerBotId, target.Id, bullet.Id, impact, damage));
                    if (target.Hp == damage)
                        events.Add(new(tick, GameEventType.BotDestroyed, bullet.OwnerBotId, target.Id, bullet.Id, impact));
                }
            }
            else if (wall > 1 && Inside(box with { Center = bullet.Position + motion }))
            {
                survivors.Add(bullet with { Position = bullet.Position + motion });
            }
        }
        bullets.Clear();
        bullets.AddRange(survivors);
    }

    private Hitbox Body(BotState bot) => new(bot.Position, bot.BodyAngle, config.BotLength, config.BotWidth);

    private bool Inside(Hitbox box)
    {
        var x = box.Extent(new(1, 0));
        var y = box.Extent(new(0, 1));
        return box.Center.X >= x && box.Center.X <= config.Arena.Width - x
            && box.Center.Y >= y && box.Center.Y <= config.Arena.Height - y;
    }

    private double WallImpact(Hitbox box, Vec2 motion)
    {
        var impact = double.PositiveInfinity;
        foreach (var wall in walls) impact = Math.Min(impact, box.Impact(wall, motion));
        return impact;
    }

    private static double Track(double value) => double.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
    private static double NormalizeAngle(double angle) => Math.Atan2(Math.Sin(angle), Math.Cos(angle));
}

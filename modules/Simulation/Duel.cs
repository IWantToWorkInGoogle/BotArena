namespace BotArena;

public record BotSpawn(string Id, Vec2 Position, double BodyAngle, double TurretAngle);

public sealed record MatchConfig(int TickRate, int MaxTicks, ArenaState Arena, IReadOnlyList<BotSpawn> Spawns)
{
    public double MaxForwardSpeed { get; init; } = 3.6;
    public double MaxReverseSpeed { get; init; } = 2.0;
    public double Acceleration { get; init; } = 8.0;
    public double Deceleration { get; init; } = 12.0;
    public double MaxTurnSpeed { get; init; } = 3.0;
    public double TurnResponseSeconds { get; init; } = 0.20;
    public double MaxTurretTurnSpeed { get; init; } = 6.0;
    public double BotLength { get; init; } = 0.68;
    public double BotWidth { get; init; } = 0.58;
    public double BulletLength { get; init; } = 0.19;
    public double BulletWidth { get; init; } = 0.09;
    public double BulletSpeed { get; init; } = 13.0;
    public int BulletDamage { get; init; } = 20;
    public int ShotCooldownTicks { get; init; } = 12;

    public static MatchConfig Classic { get; } = new(20, 200, new(16, 10, Array.AsReadOnly(new Wall[]
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

public sealed class Duel : ISimulationEngine
{
    private readonly MatchConfig config;
    private double StepSeconds => 1.0 / config.TickRate;
    private BotState[] bots;
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
    private readonly Hitbox[] walls;
    private readonly List<BulletState> bullets = [];
    private readonly List<GameEvent> events = [];
    private readonly int[] cooldowns;
    private readonly bool[] forfeited;
    private readonly double[] bodyTurnRates;
    private readonly double[] turretTurnRates;
    private int nextBullet;
    private int tick;

    public Duel(MatchConfig config)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.TickRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.MaxTicks);
        if (config.Spawns.Count < 2)
            throw new ArgumentException("At least two robot spawns are required.", nameof(config));
        this.config = config;
        bots = config.Spawns.Select(s =>
            new BotState(s.Id, s.Position, s.BodyAngle, s.TurretAngle, 0, 100, 100, 0)).ToArray();
        walls = config.Arena.Walls.Select(w =>
            new Hitbox(new(w.X + w.Width / 2, w.Y + w.Height / 2), 0, w.Width, w.Height)).ToArray();
        cooldowns = new int[bots.Length];
        forfeited = new bool[bots.Length];
        bodyTurnRates = new double[bots.Length];
        turretTurnRates = new double[bots.Length];
        var grid = new SpatialGrid();
        var neighbors = new HashSet<int>();
        for (var i = 0; i < bots.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(bots[i].Id) || !indices.TryAdd(bots[i].Id, i))
                throw new ArgumentException("Robot IDs must be non-empty and unique.", nameof(config));
            var hull = Body(bots[i]);
            if (!Inside(hull) || WallImpact(hull, default) <= 1)
                throw new ArgumentException("Robot spawns must be clear of walls and inside the arena.", nameof(config));
            var bounds = hull.Bounds();
            grid.Find(bounds, neighbors);
            if (neighbors.Any(j => hull.Impact(Body(bots[j]), default) <= 1))
                throw new ArgumentException("Robot spawns must not overlap.", nameof(config));
            grid.Add(i, bounds);
        }
    }

    public MatchResult? Result { get; private set; }

    public BotObservation Observe(string botId)
    {
        var self = bots[FindBot(botId)];
        var sightLine = new Hitbox(self.Position, 0, 0, 0);
        var enemies = new List<BotState>();
        BotState? nearest = null;
        var nearestDistance = double.PositiveInfinity;
        for (var i = 0; i < bots.Length; i++)
        {
            var candidate = bots[i];
            if (candidate.Id == botId || !IsActive(i)) continue;
            var offset = candidate.Position - self.Position;
            if (WallImpact(sightLine, offset) <= 1) continue;
            enemies.Add(candidate);
            var distance = double.Hypot(offset.X, offset.Y);
            if (nearest is null || distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return new(tick, self, nearest, [.. bullets], config.Arena) { Enemies = enemies.AsReadOnly() };
    }

    public GameFrame CaptureFrame() => new(tick, [.. bots], [.. bullets], [.. events]);

    public void Step(IReadOnlyDictionary<string, BotAction> actions)
    {
        EnsureRunning();
        var commands = new BotAction?[bots.Length];
        for (var i = 0; i < bots.Length; i++)
        {
            if (IsActive(i))
                commands[i] = actions[bots[i].Id];
        }
        events.Clear();
        tick++;

        var next = (BotState[])bots.Clone();
        for (var i = 0; i < bots.Length; i++)
            if (commands[i] is { } action) next[i] = Advance(i, action);
        ResolveMovement(next);
        bots = next;

        for (var i = 0; i < bots.Length; i++)
            if (commands[i] is { } action) Fire(i, action);
        AdvanceBullets();

        if (!FinishIfEliminated(MatchEndReason.BotDestroyed) && tick == config.MaxTicks)
            Finish(new(null, MatchEndReason.TimeLimit, tick));
    }

    public void Forfeit(string botId)
    {
        EnsureRunning();
        var index = FindBot(botId);
        if (!IsActive(index)) return;
        forfeited[index] = true;
        bots[index] = bots[index] with { Speed = 0 };
        FinishIfEliminated(MatchEndReason.BotError);
    }

    private bool IsActive(int index) => !forfeited[index] && bots[index].Hp > 0;

    private int FindBot(string id) => indices.TryGetValue(id, out var index)
        ? index : throw new ArgumentException("Unknown bot ID.", nameof(id));

    private void EnsureRunning()
    {
        if (Result is not null) throw new InvalidOperationException("The match is finished.");
    }

    private bool FinishIfEliminated(MatchEndReason reason)
    {
        BotState? survivor = null;
        for (var i = 0; i < bots.Length; i++)
        {
            if (!IsActive(i)) continue;
            if (survivor is not null) return false;
            survivor = bots[i];
        }
        Finish(new(survivor?.Id, reason, tick));
        return true;
    }

    private void ResolveMovement(BotState[] next)
    {
        var grid = new SpatialGrid();
        var start = new Hitbox[bots.Length];
        var motion = new Vec2[bots.Length];
        var bounds = new Bounds[bots.Length];
        var blocked = new bool[bots.Length];
        var stopped = new Queue<int>();
        var neighbors = new HashSet<int>();

        for (var i = 0; i < bots.Length; i++)
        {
            if (!IsActive(i)) continue;
            var oldHull = Body(bots[i]);
            start[i] = Body(next[i]) with { Center = oldHull.Center };
            motion[i] = next[i].Position - bots[i].Position;
            bounds[i] = oldHull.Bounds().Union(start[i].Bounds()).Union(Body(next[i]).Bounds());
            grid.Add(i, bounds[i]);
        }

        for (var i = 0; i < bots.Length; i++)
        {
            if (!IsActive(i)) continue;
            grid.Find(bounds[i], neighbors);
            foreach (var j in neighbors)
            {
                if (j <= i || start[i].Impact(start[j], motion[i] - motion[j]) > 1) continue;
                Block(i);
                Block(j);
            }
        }

        // A canceled move leaves its old hull in the way of robots that expected it
        // to move. Recheck only those neighbors until no additional move is canceled.
        while (stopped.TryDequeue(out var i))
        {
            var oldHull = Body(bots[i]);
            grid.Find(oldHull.Bounds(), neighbors);
            foreach (var j in neighbors)
                if (!blocked[j] && start[j].Impact(oldHull, motion[j]) <= 1)
                    Block(j);
        }

        void Block(int i)
        {
            if (blocked[i]) return;
            blocked[i] = true;
            bodyTurnRates[i] = 0;
            next[i] = next[i] with { Position = bots[i].Position, BodyAngle = bots[i].BodyAngle, Speed = 0 };
            stopped.Enqueue(i);
        }
    }

    private void Finish(MatchResult result)
    {
        Result = result;
        events.Add(new(tick, GameEventType.MatchFinished));
    }

    private BotState Advance(int index, BotAction action)
    {
        var bot = bots[index];
        var left = Track(action.LeftTrack);
        var right = Track(action.RightTrack);
        var throttle = (left + right) / 2;
        var targetSpeed = throttle * (throttle < 0 ? config.MaxReverseSpeed : config.MaxForwardSpeed);
        var braking = Math.Abs(targetSpeed) < Math.Abs(bot.Speed) || targetSpeed * bot.Speed < 0;
        var speedDelta = (braking ? config.Deceleration : config.Acceleration) * StepSeconds;
        var speed = bot.Speed + Math.Clamp(targetSpeed - bot.Speed, -speedDelta, speedDelta);
        var targetTurnRate = (left - right) * 0.5 * config.MaxTurnSpeed;
        bodyTurnRates[index] = ApproachTurnRate(bodyTurnRates[index], targetTurnRate);
        var bodyAngle = NormalizeAngle(bot.BodyAngle + bodyTurnRates[index] * StepSeconds);
        var turretAngle = TurnTurret(index, bot.TurretAngle, action);
        var hull = Body(bot with { BodyAngle = bodyAngle });
        if (!Inside(hull) || WallImpact(hull, default) <= 1)
        {
            hull = Body(bot);
            bodyAngle = bot.BodyAngle;
            bodyTurnRates[index] = 0;
        }

        var delta = Vec2.FromAngle(bodyAngle) * (speed * StepSeconds);
        var destination = hull with { Center = hull.Center + delta };
        if (!Inside(destination) || WallImpact(hull, delta) <= 1)
            return bot with { BodyAngle = bodyAngle, TurretAngle = turretAngle, Speed = 0 };

        return bot with { Position = destination.Center, BodyAngle = bodyAngle, TurretAngle = turretAngle, Speed = speed };
    }

    private double ApproachTurnRate(double current, double target)
    {
        // Scale the response by elapsed simulation time so changing the tick rate
        // does not change how quickly the hull and turret respond to a command.
        var blend = 1 - Math.Exp(-StepSeconds / config.TurnResponseSeconds);
        return current + (target - current) * blend;
    }

    private double TurnTurret(int index, double current, BotAction action)
    {
        if (action.TurretTurn is { } turn)
        {
            var target = Track(turn) * config.MaxTurretTurnSpeed;
            turretTurnRates[index] = ApproachTurnRate(turretTurnRates[index], target);
            return NormalizeAngle(current + turretTurnRates[index] * StepSeconds);
        }

        var aim = action.AimDirection;
        if (aim == default || !double.IsFinite(aim.X) || !double.IsFinite(aim.Y))
        {
            turretTurnRates[index] = 0;
            return current;
        }

        var remaining = NormalizeAngle(Math.Atan2(aim.Y, aim.X) - current);
        var limit = config.MaxTurretTurnSpeed;
        var targetRate = Math.Clamp(remaining / StepSeconds, -limit, limit);
        turretTurnRates[index] = ApproachTurnRate(turretTurnRates[index], targetRate);
        var step = turretTurnRates[index] * StepSeconds;
        if (remaining == 0 || (step * remaining > 0 && Math.Abs(step) >= Math.Abs(remaining)))
        {
            turretTurnRates[index] = 0;
            step = remaining;
        }

        return NormalizeAngle(current + step);
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
        if (bullets.Count == 0) return;
        var survivors = new List<BulletState>();
        var grid = new SpatialGrid();
        var hulls = new Hitbox[bots.Length];
        var neighbors = new HashSet<int>();
        for (var i = 0; i < bots.Length; i++)
        {
            if (!IsActive(i)) continue;
            hulls[i] = Body(bots[i]);
            grid.Add(i, hulls[i].Bounds());
        }

        foreach (var bullet in bullets)
        {
            var motion = bullet.Direction * (config.BulletSpeed * StepSeconds);
            var box = new Hitbox(bullet.Position, Math.Atan2(bullet.Direction.Y, bullet.Direction.X),
                config.BulletLength, config.BulletWidth);
            var destination = box with { Center = bullet.Position + motion };
            var wall = WallImpact(box, motion);
            var hit = double.PositiveInfinity;
            var targetIndex = -1;
            grid.Find(box.Bounds().Union(destination.Bounds()), neighbors);
            foreach (var candidate in neighbors)
            {
                if (bots[candidate].Id == bullet.OwnerBotId) continue;
                var impact = box.Impact(hulls[candidate], motion);
                if (impact > 1 || impact > hit || (impact == hit && candidate > targetIndex)) continue;
                targetIndex = candidate;
                hit = impact;
            }

            if (hit <= 1 && hit < wall)
            {
                var target = bots[targetIndex];
                var impact = bullet.Position + motion * hit;
                var damage = Math.Min(target.Hp, config.BulletDamage);
                bots[targetIndex] = target with { Hp = target.Hp - damage, Speed = target.Hp == damage ? 0 : target.Speed };
                if (damage > 0)
                {
                    events.Add(new(tick, GameEventType.BotHit, bullet.OwnerBotId, target.Id, bullet.Id, impact, damage));
                    if (target.Hp == damage)
                        events.Add(new(tick, GameEventType.BotDestroyed, bullet.OwnerBotId, target.Id, bullet.Id, impact));
                }
            }
            else if (wall > 1 && Inside(destination))
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

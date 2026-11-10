namespace BotArena;

public readonly record struct BotMotion(double Heading, double TurnRate, Vec2 Velocity, double Speed);

/// <summary>Proposes motion for one tick. The arena can retry the proposal with rotation blocked, so implementations must be pure.</summary>
public interface IMovementModel
{
    string Id { get; }
    BotMotion Propose(BotMotion current, BotAction action, Vec2 position, double seconds, bool canRotate);
}

internal static class MotionMath
{
    public static double Input(double value) => double.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
    public static double Angle(double angle) => Math.Atan2(Math.Sin(angle), Math.Cos(angle));
    public static double ApproachRate(double current, double target, double seconds, double responseSeconds) =>
        current + (target - current) * (1 - Math.Exp(-seconds / responseSeconds));
}

internal sealed class TrackedMovement(MatchConfig config) : IMovementModel
{
    public string Id => "tracked";

    public BotMotion Propose(BotMotion current, BotAction action, Vec2 position, double seconds, bool canRotate)
    {
        var left = MotionMath.Input(action.LeftTrack);
        var right = MotionMath.Input(action.RightTrack);
        var throttle = (left + right) / 2;
        var targetSpeed = throttle * (throttle < 0 ? config.MaxReverseSpeed : config.MaxForwardSpeed);
        var braking = Math.Abs(targetSpeed) < Math.Abs(current.Speed) || targetSpeed * current.Speed < 0;
        var limit = (braking ? config.Deceleration : config.Acceleration) * seconds;
        var speed = current.Speed + Math.Clamp(targetSpeed - current.Speed, -limit, limit);
        var targetRate = (left - right) * 0.5 * config.MaxTurnSpeed;
        var turnRate = canRotate
            ? MotionMath.ApproachRate(current.TurnRate, targetRate, seconds, config.TurnResponseSeconds)
            : 0;
        var heading = canRotate ? MotionMath.Angle(current.Heading + turnRate * seconds) : current.Heading;
        return new(heading, turnRate, Vec2.FromAngle(heading) * speed, speed);
    }
}

public sealed class HoverMovement : IMovementModel
{
    private readonly double acceleration;
    private readonly double drag;
    private readonly double maxSpeed;
    private readonly double maxTurnRate;

    public HoverMovement(double acceleration = 7, double drag = 0.5, double maxSpeed = 5, double maxTurnRate = 3)
    {
        this.acceleration = NonNegative(acceleration, nameof(acceleration));
        this.drag = NonNegative(drag, nameof(drag));
        this.maxSpeed = NonNegative(maxSpeed, nameof(maxSpeed));
        this.maxTurnRate = NonNegative(maxTurnRate, nameof(maxTurnRate));
    }

    public string Id => "hover";

    public BotMotion Propose(BotMotion current, BotAction action, Vec2 position, double seconds, bool canRotate)
    {
        // Thrust uses world coordinates. Turning the hull does not rotate momentum.
        var thrust = double.IsFinite(action.Thrust.X) && double.IsFinite(action.Thrust.Y)
            ? ClampLength(action.Thrust, 1)
            : default;
        var velocity = ClampLength(current.Velocity * Math.Exp(-drag * seconds) +
            thrust * (acceleration * seconds), maxSpeed);
        var targetRate = MotionMath.Input(action.Turn) * maxTurnRate;
        var turnRate = canRotate ? MotionMath.ApproachRate(current.TurnRate, targetRate, seconds, 0.2) : 0;
        var heading = canRotate ? MotionMath.Angle(current.Heading + turnRate * seconds) : current.Heading;
        return new(heading, turnRate, velocity, double.Hypot(velocity.X, velocity.Y));
    }

    private static Vec2 ClampLength(Vec2 vector, double limit)
    {
        if (double.Hypot(vector.X, vector.Y) <= limit) return vector;
        var scale = Math.Max(Math.Abs(vector.X), Math.Abs(vector.Y));
        var direction = new Vec2(vector.X / scale, vector.Y / scale);
        return direction * (limit / double.Hypot(direction.X, direction.Y));
    }

    private static double NonNegative(double value, string name) =>
        double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(name);
}

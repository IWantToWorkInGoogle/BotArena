using System.Text.Json.Serialization;

namespace BotArena;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double scale) => new(a.X * scale, a.Y * scale);
    public double Dot(Vec2 other) => X * other.X + Y * other.Y;
    public static Vec2 FromAngle(double angle) => new(Math.Cos(angle), Math.Sin(angle));
}

public record Wall(double X, double Y, double Width, double Height);
public record ArenaState(double Width, double Height, IReadOnlyList<Wall> Walls);
public record BotState(string Id, Vec2 Position, double BodyAngle, double TurretAngle,
    double Speed, int Hp, int MaxHp, double ReloadingTime)
{
    public bool CanShoot => Hp > 0 && ReloadingTime <= 0;
}

public record BulletState(int Id, string OwnerBotId, Vec2 Position, Vec2 Direction);
[JsonConverter(typeof(JsonStringEnumConverter<GameEventType>))]
public enum GameEventType { ShotFired, BotHit, BotDestroyed, MatchFinished }

public record GameEvent(int Tick, GameEventType Type, string? SourceBotId = null,
    string? TargetBotId = null, int? BulletId = null, Vec2? Position = null, int? Damage = null);

public record GameFrame(int Tick, BotState[] Bots, BulletState[] Bullets, GameEvent[] Events);
public record BotObservation(int Tick, BotState Self, BotState? Enemy, BulletState[] Bullets, ArenaState Arena)
{
    public IReadOnlyList<BotState> Enemies { get; init; } = [];
}
public record BotAction(double LeftTrack, double RightTrack, Vec2 AimDirection, bool Shoot,
    double? TurretTurn = null);
[JsonConverter(typeof(JsonStringEnumConverter<MatchEndReason>))]
public enum MatchEndReason { BotDestroyed, TimeLimit, BotError }

public record MatchResult(string? WinnerBotId, MatchEndReason Reason, int DurationTicks);

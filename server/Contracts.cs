namespace BotArena;

public record BehaviorSample(int Tick, string BotId, Vec2 Position, double BodyAngle, double TurretAngle,
    bool EnemyVisible, BotAction Action);
public record MatchReplay(int TickRate, ArenaState Arena, IReadOnlyList<GameFrame> Frames, MatchResult Result,
    IReadOnlyList<BehaviorSample> BehaviorSamples);
public record MatchRequest(string? BotCode, string? OpponentId, string? Language = null, string? Movement = null, OpponentSlot?[]? Opponents = null);
public record OpponentSlot(string? Id, string? Movement = null);
public record ScriptError(string BotId, int? Tick, string Message, int? Line = null, int? Column = null);
public record MatchResponse(MatchReplay Replay, ScriptError? Error = null);
public record Opponent(string Id, string Name, string Description);

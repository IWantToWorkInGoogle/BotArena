namespace BotArena;

internal static class MovementCatalog
{
    private const string Default = "tracked";
    private static readonly Dictionary<string, Func<IMovementModel?>> Models = new(StringComparer.Ordinal)
    {
        [Default] = () => null,
        ["hover"] = () => new HoverMovement(),
    };

    public static bool Supports(string? id) => Models.ContainsKey(id ?? Default);

    public static MatchConfig ConfigurePlayer(MatchConfig config, string? id)
    {
        var movement = Models[id ?? Default]();
        if (movement is null) return config;
        var spawns = config.Spawns.ToArray();
        var player = Array.FindIndex(spawns, spawn => spawn.Id == "player");
        spawns[player] = spawns[player] with { Movement = movement };
        return config with { Spawns = Array.AsReadOnly(spawns) };
    }
}

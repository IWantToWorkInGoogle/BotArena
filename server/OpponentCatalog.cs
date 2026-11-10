namespace BotArena;

internal static class OpponentCatalog
{
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
            const weapon = { aimDirection: aim, shoot: !!enemy && aligned && self.canShoot };
            if (self.movement === "hover" || self.movement === "omni") {
                const damping = self.movement === "hover" ? 0.45 : 0;
                return { ...weapon, turn, thrust: {
                    x: dx * 0.65 - self.velocity.x * damping,
                    y: dy * 0.65 - self.velocity.y * damping
                }};
            }
            if (self.movement === "wheeled") {
                const steer = Math.max(-1, Math.min(1, error * 1.8));
                return { ...weapon, throttle: Math.abs(error) < 1.4 ? throttle + 0.28 * Math.abs(steer) : 0.35, turn: steer };
            }
            return {
                ...weapon,
                leftTrack: throttle + turn,
                rightTrack: throttle - turn
            };
        }
        """;

    public static IReadOnlyList<Opponent> Opponents { get; } =
        Array.AsReadOnly(Catalog.Select(item => item.Info).ToArray());

    public static bool Supports(string? id) => Catalog.Any(item => item.Info.Id == id);

    public static string GetCode(string id) =>
        Catalog.Single(item => item.Info.Id == id).Script + "\n" + Driving;
}

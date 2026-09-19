using System.Numerics;
using Annihilation.Core;

namespace Annihilation.Game;

// An input-only pilot for hidden rendering smoke tests. It never changes game state or entities.
internal static class SmokePilot
{
    private static readonly Vector2[] Directions =
    [Vector2.Zero, new(0, -1), new(0, 1), new(-1, 0), new(1, 0),
        Vector2.Normalize(new(-1, -1)), Vector2.Normalize(new(-1, 1)),
        Vector2.Normalize(new(1, -1)), Vector2.Normalize(new(1, 1))];
    private static readonly float[] Horizons = [0.12f, 0.3f, 0.55f, 0.8f];

    public static PlayerInput Input(GameWorld world)
    {
        bool nova = world.Boss is not null && world.BossAttack == BossPattern.Nova;
        bool field = world.LearnedAttack is null || nova && world.LearnedAttack != AttackKind.Nova;
        Vector2 goal = new(nova ? world.BossFinaleLaneX : world.Boss?.Position.X ?? 350, 700);
        Vector2 best = Vector2.Zero;
        float bestCost = float.MaxValue;
        foreach (var direction in Directions)
        {
            float cost = 0;
            foreach (float horizon in Horizons)
            {
                Vector2 player = Vector2.Clamp(world.PlayerPosition + direction * GameSettings.PlayerSpeed * horizon,
                    new(35, 420), new(GameSettings.FieldWidth - 35, 790));
                cost += Vector2.DistanceSquared(player, goal) * 0.00002f;
                foreach (var bullet in world.Bullets)
                {
                    if (!bullet.Alive || !bullet.Hostile || field && bullet.Absorbable) continue;
                    float distance = Vector2.Distance(player, bullet.Position + bullet.Velocity * horizon) - bullet.Radius - GameSettings.PlayerRadius;
                    cost += 1100 / Math.Max(5, distance * distance);
                    if (distance < 16) cost += 1000;
                }
            }
            if (world.PlayerPosition.Y <= 425 && direction.Y < 0 || world.PlayerPosition.Y >= 785 && direction.Y > 0 ||
                world.PlayerPosition.X <= 40 && direction.X < 0 || world.PlayerPosition.X >= GameSettings.FieldWidth - 40 && direction.X > 0)
                cost += 10000;
            if (cost < bestCost) { bestCost = cost; best = direction; }
        }
        return new(best, world.LearnedAttack.HasValue || world.Elapsed > 13, false, field,
            world.LearnedAttack.HasValue && world.ShotMode != ShotMode.Combined);
    }
}

using System.Numerics;
using Raylib_cs;
using ArtificialInnocence.Core;

namespace ArtificialInnocence.Game;

internal sealed class GameRenderer
{
    private static readonly Color Background = new(5, 10, 23, 255);
    private static readonly Color Panel = new(10, 19, 34, 255);
    private static readonly Color Line = new(33, 53, 74, 255);
    private static readonly Color Blue = new(67, 182, 255, 255);
    private static readonly Color Cyan = new(106, 255, 218, 255);
    private static readonly Color Red = new(255, 75, 108, 255);
    private static readonly Color Gold = new(255, 210, 128, 255);
    private static readonly Color White = new(232, 241, 250, 255);
    private static readonly Color Muted = new(133, 157, 180, 255);
    private static readonly AttackKind[] LearnedKinds = [AttackKind.Pulse, AttackKind.Fan, AttackKind.Seeker, AttackKind.Nova];
    private readonly (float X, float Y, float Speed)[] stars;

    public GameRenderer()
    {
        var random = new Random(7);
        stars = Enumerable.Range(0, 130).Select(_ => (
            random.NextSingle() * GameSettings.FieldWidth, random.NextSingle() * GameSettings.Height,
            12 + random.NextSingle() * 75)).ToArray();
    }

    public void Draw(GameWorld world, bool focus)
    {
        Raylib.ClearBackground(Background);
        DrawBackground(world.Elapsed);
        Raylib.BeginScissorMode(0, 54, GameSettings.FieldWidth, GameSettings.Height - 80);
        DrawBossCue(world);
        DrawField(world);
        foreach (var enemy in world.Enemies) DrawEnemy(enemy, world.Elapsed);
        // Hostile shots stay above friendly shots so danger remains legible.
        foreach (var bullet in world.Bullets.Where(b => !b.Hostile)) DrawBullet(bullet);
        foreach (var bullet in world.Bullets.Where(b => b.Hostile)) DrawBullet(bullet);
        foreach (var effect in world.CounterEffects) DrawCounter(effect, world.Elapsed);
        foreach (var particle in world.Particles)
            Raylib.DrawCircleV(particle.Position, 2.5f,
                Raylib.Fade(particle.Cyan ? Cyan : Red, Math.Clamp(particle.Life / 0.45f, 0, 1)));
        if (world.State != GameState.GameOver &&
            (world.InvincibleTime <= 0 || (int)(world.InvincibleTime * 12) % 2 == 0))
            DrawPlayer(world.PlayerPosition, focus, world.Elapsed);
        Raylib.EndScissorMode();
        DrawStageHud(world);
        DrawPanel(world);
        if (world.State == GameState.Playing && world.NoticeTime > 0)
        {
            Raylib.DrawRectangle(100, 132, 500, 43, Raylib.Fade(Panel, 0.92f));
            Raylib.DrawRectangle(100, 132, 3, 43, Cyan);
            Center(world.Notice, 148, FitSize(world.Notice, 465, 17), Cyan);
        }
        switch (world.State)
        {
            case GameState.Title: DrawTitle(); break;
            case GameState.Paused: DrawPause(); break;
            case GameState.GameOver: DrawResult(world, false); break;
            case GameState.StageClear: DrawResult(world, true); break;
        }
    }

    private void DrawBackground(float time)
    {
        Raylib.DrawCircleGradient(new(350, 50), 470, new Color(16, 35, 59, 255), Background);
        for (int i = 0; i < 5; i++)
        {
            float r = 235 + i * 91;
            Raylib.DrawRing(new(350, -120), r, r + 1, 20, 160, 90, Raylib.Fade(Line, 0.35f));
        }
        for (int i = 0; i < 12; i++)
        {
            float y = (i * 90 + time * 29) % 1080 - 120;
            float inset = 36 + y * 0.075f;
            Raylib.DrawLineEx(new(inset, y), new(inset + 25, y + 14), 1, Raylib.Fade(Line, 0.55f));
            Raylib.DrawLineEx(new(700 - inset, y), new(675 - inset, y + 14), 1, Raylib.Fade(Line, 0.55f));
        }
        foreach (var star in stars)
        {
            float y = (star.Y + time * star.Speed) % GameSettings.Height;
            Raylib.DrawRectangle((int)star.X, (int)y, 1, star.Speed > 65 ? 3 : 1,
                Raylib.Fade(star.Speed > 65 ? Muted : Line, 0.62f));
        }
        Raylib.DrawLine(18, 54, 18, 813, Raylib.Fade(Line, 0.6f));
        Raylib.DrawLine(682, 54, 682, 813, Raylib.Fade(Line, 0.6f));
        for (int y = 84; y < 800; y += 44)
        {
            Raylib.DrawLine(18, y, 23, y, Line);
            Raylib.DrawLine(677, y, 682, y, Line);
        }
    }

    private static void DrawBossCue(GameWorld world)
    {
        if (world.Boss is not { } boss) return;
        if (world.BossAttack == BossPattern.Nova)
        {
            int lane = (int)world.BossFinaleLaneX;
            Raylib.DrawRectangle(lane - 55, 176, 110, 630, Raylib.Fade(Blue, 0.035f));
            for (int y = 190; y < 800; y += 32)
            {
                Raylib.DrawLine(lane - 55, y, lane - 55, y + 12, Raylib.Fade(Blue, 0.28f));
                Raylib.DrawLine(lane + 55, y, lane + 55, y + 12, Raylib.Fade(Blue, 0.28f));
            }
        }
        if (world.BossTelegraph > 0)
        {
            float radius = 92 - world.BossTelegraph * 27;
            Raylib.DrawRing(boss.Position, radius, radius + 2, 0, 360, 64,
                Raylib.Fade(world.BossAttack == BossPattern.Nova ? Blue : Red, 0.7f));
        }
    }

    private static void DrawField(GameWorld world)
    {
        Vector2 p = world.PlayerPosition;
        if (world.FieldActive)
        {
            float r = GameSettings.FieldRadius;
            Raylib.DrawCircleV(p, r, Raylib.Fade(Blue, 0.08f));
            Raylib.DrawCircleLines((int)p.X, (int)p.Y, r, Raylib.Fade(Blue, 0.75f));
            Raylib.DrawCircleLines((int)p.X, (int)p.Y, r - 5, Raylib.Fade(Blue, 0.15f));
            for (int i = 0; i < 4; i++)
            {
                float angle = world.Elapsed * 34 + i * 90;
                Raylib.DrawRing(p, r - 1, r + 2, angle, angle + 24, 8, Blue);
            }
        }
        if (world.BombTime > 0)
        {
            float r = GameSettings.BombRadius;
            Raylib.DrawCircleV(p, r, Raylib.Fade(Gold, 0.07f));
            Raylib.DrawCircleLines((int)p.X, (int)p.Y, r, Raylib.Fade(Gold, 0.8f));
            for (int i = 0; i < 3; i++)
            {
                float ring = ((world.Elapsed * 0.7f + i / 3f) % 1) * r;
                Raylib.DrawCircleLines((int)p.X, (int)p.Y, ring, Raylib.Fade(Gold, (1 - ring / r) * 0.5f));
            }
            Raylib.DrawRing(p, r - 2, r + 2, -90,
                -90 + 360 * world.BombTime / GameSettings.BombDuration, 80, Gold);
        }
    }

    private static void DrawBullet(Bullet bullet)
    {
        Color color = bullet.Hostile ? bullet.Absorbable ? Blue : Red : Cyan;
        Vector2 p = bullet.Position;
        float rotation = MathF.Atan2(bullet.Velocity.Y, bullet.Velocity.X) * 180 / MathF.PI;
        if (!bullet.Hostile && bullet.Kind == AttackKind.Basic)
        {
            Raylib.DrawLineEx(p + new Vector2(0, 7), p + new Vector2(0, -9), 3, Raylib.Fade(Cyan, 0.75f));
            return;
        }
        Raylib.DrawCircleV(p, bullet.Radius + 5, Raylib.Fade(color, 0.10f));
        if (bullet.Velocity.LengthSquared() > 0)
            Raylib.DrawLineEx(p - Vector2.Normalize(bullet.Velocity) * 12, p, 2, Raylib.Fade(color, 0.3f));
        DrawAttackGlyph(p, bullet.Kind, bullet.Radius, color, rotation);
    }

    private static void DrawAttackGlyph(Vector2 p, AttackKind kind, float r, Color color, float rotation = -90)
    {
        switch (kind)
        {
            case AttackKind.Fan:
                Raylib.DrawPoly(p, 4, r + 1, 0, color);
                Raylib.DrawPoly(p, 4, r * 0.48f, 0, Background);
                break;
            case AttackKind.Seeker:
                Raylib.DrawPoly(p, 3, r + 2, rotation, color);
                Raylib.DrawPoly(p, 3, r * 0.44f, rotation, Background);
                break;
            case AttackKind.Nova:
                Raylib.DrawPoly(p, 6, r + 1, 30, color);
                Raylib.DrawPoly(p, 6, r * 0.68f, 30, Background);
                Raylib.DrawLineEx(p + new Vector2(-r * 0.5f, 0), p + new Vector2(r * 0.5f, 0), 2, color);
                Raylib.DrawLineEx(p + new Vector2(0, -r * 0.5f), p + new Vector2(0, r * 0.5f), 2, color);
                break;
            default:
                Raylib.DrawCircleV(p, r, color);
                Raylib.DrawCircleV(p, r * 0.48f, Background);
                Raylib.DrawCircleV(p, 1.4f, White);
                break;
        }
    }

    private static void DrawCounter(CounterEffect effect, float time)
    {
        float alpha = Math.Clamp(effect.Life / 0.36f, 0, 1);
        Color color = effect.Bomb ? Gold : effect.Kind == AttackKind.Nova ? Blue : Cyan;
        float width = effect.Kind == AttackKind.Nova ? 12 : 7;
        Raylib.DrawLineEx(effect.Position, effect.Target, width * 3, Raylib.Fade(color, alpha * 0.11f));
        Raylib.DrawLineEx(effect.Position, effect.Target, width, Raylib.Fade(color, alpha * 0.65f));
        Raylib.DrawLineEx(effect.Position, effect.Target, 2, Raylib.Fade(White, alpha));
        float r = 9 + (1 - alpha) * 39;
        Raylib.DrawPolyLinesEx(effect.Position, 6, r, time * 80, 2, Raylib.Fade(color, alpha));
        Raylib.DrawCircleV(effect.Target, 12 * alpha, Raylib.Fade(White, alpha * 0.7f));
    }

    private static void DrawPlayer(Vector2 p, bool focus, float time)
    {
        Raylib.DrawCircleV(p, 28, Raylib.Fade(Cyan, 0.035f));
        float flame = 10 + MathF.Sin(time * 35) * 4;
        Raylib.DrawTriangle(p + new Vector2(-5, 13), p + new Vector2(0, 24 + flame), p + new Vector2(5, 13), Blue);
        Raylib.DrawTriangle(p + new Vector2(0, -24), p + new Vector2(-19, 18), p + new Vector2(0, 10), White);
        Raylib.DrawTriangle(p + new Vector2(0, -24), p + new Vector2(0, 10), p + new Vector2(19, 18), Cyan);
        Raylib.DrawTriangle(p + new Vector2(0, -13), p + new Vector2(-4, 6), p + new Vector2(4, 6), Panel);
        if (focus)
        {
            Raylib.DrawCircleV(p, GameSettings.PlayerRadius + 2, Background);
            Raylib.DrawCircleLines((int)p.X, (int)p.Y, GameSettings.PlayerRadius, White);
            Raylib.DrawCircleV(p, 2, Red);
        }
    }

    private static void DrawEnemy(Enemy enemy, float time)
    {
        Vector2 p = enemy.Position;
        float r = enemy.Radius;
        Color color = enemy.HitFlash > 0 ? White : enemy.BluePhase ? Blue : Red;
        Raylib.DrawCircleV(p, r + 10, Raylib.Fade(color, 0.07f));
        if (enemy.Kind == EnemyKind.Boss)
        {
            Raylib.DrawRing(p, 57, 59, time * 16, time * 16 + 280, 70, Raylib.Fade(color, 0.5f));
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 wing = p + new Vector2(side * 59, -8);
                Raylib.DrawLineEx(p, wing, 20, Line);
                Raylib.DrawPoly(wing, 4, 30, 45, Panel);
                Raylib.DrawPolyLinesEx(wing, 4, 30, 45, 2, color);
                Raylib.DrawRectangle((int)wing.X - 4, (int)wing.Y - 8, 8, 21, color);
                Raylib.DrawLineEx(wing + new Vector2(side * 16, -14), wing + new Vector2(side * 26, 31), 4, color);
            }
            Raylib.DrawPoly(p, 6, 48, 30, Line);
            Raylib.DrawPoly(p, 6, 41, 30, Panel);
            Raylib.DrawPolyLinesEx(p, 6, 35, -time * 18, 2, color);
            DrawAttackGlyph(p, AttackKind.Nova, 21, color);
            return;
        }
        int sides = enemy.Kind switch { EnemyKind.Hunter => 3, EnemyKind.Prism => 6, _ => 4 };
        float angle = enemy.Kind == EnemyKind.Hunter ? 90 : 0;
        Raylib.DrawPoly(p, sides, r, angle, Line);
        Raylib.DrawPolyLinesEx(p, sides, r, angle, 2, color);
        Raylib.DrawPoly(p, sides, r - 5, angle, Panel);
        DrawAttackGlyph(p, enemy.Kind switch
        {
            EnemyKind.Striker => AttackKind.Fan, EnemyKind.Hunter => AttackKind.Seeker,
            EnemyKind.Prism => AttackKind.Fan, _ => AttackKind.Pulse
        }, 6, color, 90);
        Raylib.DrawLineEx(p + new Vector2(-r + 2, -7), p + new Vector2(-r - 6, 14), 3, color);
        Raylib.DrawLineEx(p + new Vector2(r - 2, -7), p + new Vector2(r + 6, 14), 3, color);
        if (enemy.Kind == EnemyKind.Prism)
            Raylib.DrawRing(p, r + 5, r + 6, time * 55, time * 55 + 220, 32, color);
        if (enemy.Health < enemy.MaxHealth)
        {
            Raylib.DrawRectangle((int)p.X - 18, (int)p.Y - (int)r - 10, 36, 3, Line);
            Raylib.DrawRectangle((int)p.X - 18, (int)p.Y - (int)r - 10,
                (int)(36 * Math.Clamp(enemy.Health / enemy.MaxHealth, 0, 1)), 3, color);
        }
    }

    private static void DrawStageHud(GameWorld world)
    {
        Raylib.DrawRectangle(0, 0, 700, 54, Background);
        Text("01  /  THE MIRROR ARRAY", 27, 14, 14, White);
        string stage = world.Stage switch
        {
            StagePhase.Approach => "APPROACH", StagePhase.Pursuit => "PURSUIT",
            StagePhase.Gate => "PRISM GATE", _ => "WARDEN / BOSS"
        };
        Right(stage, 674, 14, 13, world.Stage == StagePhase.Boss ? Red : Muted);
        Raylib.DrawRectangle(27, 41, 646, 2, Line);
        Raylib.DrawRectangle(27, 41, (int)(646 * Math.Clamp(world.Elapsed / GameSettings.StageDuration, 0, 1)), 2, Blue);
        if (world.Boss is { } boss)
        {
            Raylib.DrawRectangle(25, 59, 650, 38, Raylib.Fade(Background, 0.9f));
            Text("WARDEN", 35, 66, 13, White);
            Right($"{world.BossAttack.ToString().ToUpperInvariant()} / {Math.Ceiling(boss.Health)}", 665, 66, 13, Red);
            Raylib.DrawRectangle(35, 86, 630, 4, Line);
            Raylib.DrawRectangle(35, 86, (int)(630 * Math.Clamp(boss.Health / boss.MaxHealth, 0, 1)), 4, Red);
        }
        Raylib.DrawRectangle(0, 814, 700, 26, Background);
        Text("RED: EVADE", 26, 822, 11, Red);
        Text("BLUE: HOLD X TO ABSORB", 151, 822, 11, Blue);
        Right($"{(int)world.Elapsed / 60:00}:{(int)world.Elapsed % 60:00}", 674, 820, 13, Muted);
    }

    private static void DrawPanel(GameWorld world)
    {
        const int x = 726;
        Raylib.DrawRectangle(700, 0, 340, 840, Panel);
        Raylib.DrawLine(700, 0, 700, 840, Line);
        Text("A / I     COMBAT LEARNING SYSTEM", x, 23, 12, Cyan);
        Text("ARTIFICIAL", x, 48, 25, White);
        Text("INNOCENCE", x, 78, 34, White);
        Rule(128);
        Text("SCORE", x, 145, 12, Muted);
        Right($"BEST {world.BestScore:000000}", 1014, 145, 12, Muted);
        Text($"{world.Score:000000}", x, 165, 33, White);
        Text("LIVES", x, 216, 11, Muted);
        for (int i = 0; i < GameSettings.InitialLives; i++)
            Raylib.DrawRectangle(x + 48 + i * 19, 217, 13, 7, i < world.Lives ? Cyan : Line);
        Text("BOMBS", 895, 216, 11, Muted);
        for (int i = 0; i < GameSettings.InitialBombs; i++)
            Raylib.DrawCircle(951 + i * 23, 221, 5, i < world.Bombs ? Gold : Line);
        Rule(244);
        Text("LEARNED WEAPON", x, 260, 12, Cyan);
        Right("1 SLOT", 1014, 260, 11, Muted);
        Raylib.DrawRectangle(x, 284, 288, 61, Background);
        Raylib.DrawRectangle(x, 284, 3, 61, world.LearnedAttack.HasValue ? Cyan : Line);
        if (world.LearnedAttack is { } learned)
        {
            DrawAttackGlyph(new(x + 30, 314), learned, 12, Cyan);
            Text(GameSettings.AttackName(learned), x + 56, 296, 20, White);
            Text("UNLIMITED / MATCH TO COUNTER", x + 56, 322, 10, Muted);
        }
        else
        {
            Raylib.DrawCircleLines(x + 30, 314, 10, Line);
            Text("AWAITING PATTERN", x + 54, 297, 17, Muted);
            Text("ABSORB BLUE TO ACQUIRE", x + 54, 322, 11, Blue);
        }
        Text("C  /  SHOT MODE", x, 362, 12, Muted);
        string[] names = ["NORMAL", "LEARNED", "BOTH"];
        for (int i = 0; i < 3; i++)
        {
            bool active = (int)world.ShotMode == i;
            int px = x + i * 98;
            Raylib.DrawRectangle(px, 384, 92, 29, active ? Cyan : Background);
            Text(names[i], px + (92 - Raylib.MeasureText(names[i], 12)) / 2, 393, 12, active ? Background : Muted);
        }
        Text("ABSORPTION / REQUIRED", x, 436, 12, Muted);
        for (int i = 0; i < LearnedKinds.Length; i++)
        {
            AttackKind kind = LearnedKinds[i];
            int y = 465 + i * 31;
            int threshold = GameSettings.LearnThreshold(kind);
            bool held = world.LearnedAttack == kind;
            int progress = held ? threshold : Math.Min(world.LearningProgress(kind), threshold);
            Color color = held ? Cyan : Blue;
            DrawAttackGlyph(new(x + 8, y + 5), kind, 5, color);
            Text(GameSettings.AttackName(kind), x + 24, y, 12, held ? White : Muted);
            Raylib.DrawRectangle(x + 103, y + 3, 120, 5, Line);
            Raylib.DrawRectangle(x + 103, y + 3, (int)(120f * progress / threshold), 5, color);
            Right(held ? "HELD" : $"{progress:00}/{threshold:00}", 1014, y, 12, held ? Cyan : Muted);
        }
        Text("New learning replaces the held weapon.", x, 590, 11, Muted);
        Rule(615);
        Text("PILOT INPUT", x, 633, 12, Cyan);
        Control("WASD / ARROWS", "Move", 658);
        Control("Z / SPACE", "Fire", 680);
        Control("X / RMB", "Absorb field (hold)", 702);
        Control("SHIFT", "Focus / slow", 724);
        Control("V / M", "Bomb / mute", 746);
        Control("P / ESC", "Pause / exit", 768);
        Text($"ABSORBED {world.TotalAbsorbed:000}", x, 808, 11, Blue);
        Right($"COUNTERS {world.AnnihilationCount:000}", 1014, 808, 11, Cyan);
    }

    private static void DrawTitle()
    {
        Raylib.DrawRectangle(0, 54, 700, 760, Raylib.Fade(Background, 0.87f));
        Text("STEAL THEIR PATTERN. RETURN THEIR FIRE.", 57, 94, 14, Cyan);
        Text("ARTIFICIAL", 51, 130, 58, White);
        Text("INNOCENCE", 51, 193, 58, White);
        Text("A BULLET-LEARNING SHOOTER", 57, 267, 17, Muted);
        string[] steps = ["ABSORB", "LEARN", "COUNTER"];
        string[] detail = ["Take the blue", "Keep one pattern", "Match their shape"];
        for (int i = 0; i < 3; i++)
        {
            int x = 56 + i * 199;
            Raylib.DrawRectangle(x, 318, 188, 91, Panel);
            Text($"0{i + 1}", x + 14, 332, 12, Blue);
            Text(steps[i], x + 14, 354, 20, White);
            Text(detail[i], x + 14, 383, 12, Muted);
        }
        DrawAttackGlyph(new(67, 448), AttackKind.Pulse, 7, Red);
        Text("RED / Always dangerous. The field cannot stop it.", 86, 442, 14, Red);
        DrawAttackGlyph(new(67, 483), AttackKind.Pulse, 7, Blue);
        Text("BLUE / Hold X or right mouse to absorb. No limit.", 86, 477, 14, Blue);
        Text("Blue bullets also hurt when your field is OFF.", 86, 502, 13, Muted);
        Text("01", 57, 542, 13, Cyan);
        Text("Absorb a full pattern to learn it. See progress at right.", 86, 542, 13, White);
        Text("02", 57, 573, 13, Cyan);
        Text("A new pattern replaces the old one. Release X to keep it.", 86, 573, 13, White);
        Text("03", 57, 604, 13, Cyan);
        Text("Fire the same shape into enemy shots for a heavy counter.", 86, 604, 13, White);
        Raylib.DrawRectangle(56, 659, 588, 61, Cyan);
        Center("ENTER  /  LAUNCH", 680, 24, Background);
        Center("Z: FIRE     C: SHOT MODE     V: EMERGENCY BOMB", 742, 13, White);
        Center("One stage. One Warden. Learn, hold, return.", 778, 13, Muted);
    }

    private static void DrawPause()
    {
        Shade();
        Text("FLIGHT SYSTEMS ON HOLD", 100, 293, 13, Cyan);
        Text("PAUSED", 95, 325, 57, White);
        Text("P  /  RESUME", 100, 410, 23, Cyan);
        Text("The game pauses automatically when focus is lost.", 100, 459, 14, Muted);
        Text("Hold X for blue. Avoid red. Match shapes to counter.", 100, 487, 14, Muted);
    }

    private static void DrawResult(GameWorld world, bool clear)
    {
        Shade();
        Text(clear ? "WARDEN DEFEATED / PATTERN COMPLETE" : "FLIGHT RECORDER / SIGNAL LOST", 84, 221, 13, clear ? Cyan : Red);
        Text(clear ? "STAGE CLEAR" : "SIGNAL LOST", 78, 259, 48, White);
        Text(clear ? "Their strongest pattern became your answer." : "Read the next pattern. Choose what to keep.", 84, 325, 15, Muted);
        Raylib.DrawLine(84, 369, 616, 369, Line);
        Text("FINAL SCORE", 84, 394, 13, Muted);
        Text($"{world.Score:000000}", 80, 422, 47, White);
        Text("BLUE ABSORBED", 84, 505, 12, Blue);
        Text($"{world.TotalAbsorbed:000}", 84, 530, 29, White);
        Text("ANNIHILATIONS", 310, 505, 12, Cyan);
        Text($"{world.AnnihilationCount:000}", 310, 530, 29, White);
        Text($"FLIGHT TIME  {(int)world.Elapsed / 60:00}:{(int)world.Elapsed % 60:00}", 84, 596, 14, Muted);
        Raylib.DrawRectangle(84, 644, 532, 54, clear ? Cyan : White);
        Center("ENTER / R  TO FLY AGAIN", 662, 20, Background);
    }

    private static void Shade() => Raylib.DrawRectangle(0, 54, 700, 760, Raylib.Fade(Background, 0.92f));
    private static void Rule(int y) => Raylib.DrawLine(726, y, 1014, y, Line);
    private static void Control(string key, string action, int y)
    {
        Text(key, 726, y, 12, White);
        Text(action, 862, y, 12, Muted);
    }
    private static int FitSize(string text, int width, int size)
    {
        while (size > 9 && Raylib.MeasureText(text, size) > width) size--;
        return size;
    }
    private static void Text(string text, int x, int y, int size, Color color) => Raylib.DrawText(text, x, y, size, color);
    private static void Right(string text, int x, int y, int size, Color color) =>
        Text(text, x - Raylib.MeasureText(text, size), y, size, color);
    private static void Center(string text, int y, int size, Color color) =>
        Text(text, (GameSettings.FieldWidth - Raylib.MeasureText(text, size)) / 2, y, size, color);
}

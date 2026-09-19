using System.Numerics;
using ArtificialInnocence.Core;

// Dependency-free runner. Tests exercise gameplay through the public simulation API.
var tests = new (string Name, Action Run)[]
{
    ("Title and invalid updates cannot advance the simulation", () =>
    {
        var w = new GameWorld();
        Tick(w, 120, new(Vector2.One, true, false, true, true, true));
        Check(w.State == GameState.Title && w.Elapsed == 0 && w.Bullets.Count == 0, "Title advanced");
        w.TogglePause(); Check(w.State == GameState.Title, "Pause changed title state");
        w.Start();
        foreach (float dt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            w.Update(dt, new(Vector2.One, true, false, true, true, true));
        Check(w.Elapsed == 0 && w.Bullets.Count == 0 && w.Bombs == GameSettings.InitialBombs,
            "Invalid delta time changed the world");
    }),
    ("Movement is normalized and the absorption field has no speed penalty", () =>
    {
        var straight = Started(); var diagonal = Started(); var focused = Started(); var field = Started();
        Vector2 origin = straight.PlayerPosition;
        Tick(straight, 30, new(new(1, 0), false, false));
        Tick(diagonal, 30, new(new(1, -1), false, false));
        Tick(focused, 30, new(new(1, 0), false, true));
        Tick(field, 30, new(new(1, 0), false, false, true));
        Near(Vector2.Distance(origin, straight.PlayerPosition), Vector2.Distance(origin, diagonal.PlayerPosition));
        Near(Vector2.Distance(origin, focused.PlayerPosition), GameSettings.FocusSpeed * 30 * GameSettings.FixedStep);
        Check(field.PlayerPosition == straight.PlayerPosition && field.FieldActive, "Field slowed movement");
        Tick(field, 1); Check(!field.FieldActive, "Released field stayed active");
    }),
    ("Player movement remains inside the playfield", () =>
    {
        var w = Started();
        CleanTick(w, 600, new(new(-1, 1), false, false));
        Check(w.PlayerPosition.X >= GameSettings.PlayerRadius && w.PlayerPosition.Y <= GameSettings.Height,
            "Player left the lower-left boundary");
        CleanTick(w, 600, new(new(1, -1), false, false));
        Check(w.PlayerPosition.X <= GameSettings.FieldWidth - GameSettings.PlayerRadius && w.PlayerPosition.Y >= 0,
            "Player left the upper-right boundary");
    }),
    ("Blue bullets require the field and red bullets bypass it", () =>
    {
        var blue = Vulnerable();
        blue.Bullets.Add(new(blue.PlayerPosition, Vector2.Zero, true, AttackKind.Pulse, true)); Tick(blue, 1);
        Check(blue.Lives == GameSettings.InitialLives - 1 && blue.TotalAbsorbed == 0,
            "Blue bullet was safe without the field");
        var red = Vulnerable();
        red.Bullets.Add(new(red.PlayerPosition, Vector2.Zero, true, AttackKind.Pulse, false));
        Tick(red, 1, new(Vector2.Zero, false, false, true));
        Check(red.Lives == GameSettings.InitialLives - 1 && red.TotalAbsorbed == 0,
            "Field blocked or absorbed a red bullet");
        var active = Vulnerable(); Absorb(active, AttackKind.Pulse, 1);
        Check(active.Lives == GameSettings.InitialLives && active.TotalAbsorbed == 1 && active.Bullets.Count == 0,
            "Field did not safely absorb a blue bullet");
        Check(active.LearningProgress(AttackKind.Pulse) == 1 && active.LastAbsorbedAttack == AttackKind.Pulse,
            "Absorption did not record its attack type");
    }),
    ("Field can remain active indefinitely", () =>
    {
        var w = Started(); CleanTick(w, 1200, new(Vector2.Zero, false, false, true));
        Check(w.FieldActive, "Held field expired");
        Absorb(w, AttackKind.Fan, 1);
        Check(w.TotalAbsorbed == 1 && w.Lives == GameSettings.InitialLives, "Held field stopped absorbing");
    }),
    ("Each attack learns at its own threshold", () =>
    {
        foreach (var kind in LearnableAttacks())
        {
            var w = Started(); int threshold = GameSettings.LearnThreshold(kind);
            Check(threshold > 1, $"Invalid threshold for {kind}");
            Absorb(w, kind, threshold - 1);
            Check(w.LearnedAttack is null && w.LearningProgress(kind) == threshold - 1, $"{kind} learned early");
            Absorb(w, kind, 1);
            Check(w.LearnedAttack == kind && w.TotalAbsorbed == threshold, $"{kind} did not learn at threshold");
            Check(w.ShotMode == ShotMode.Normal, "Learning unexpectedly changed selected shot mode");
        }
        Check(GameSettings.LearnThreshold(AttackKind.Pulse) != GameSettings.LearnThreshold(AttackKind.Nova),
            "Ordinary and boss attacks use an identical learning threshold");
    }),
    ("Learning progress is per attack and replacement clears stale progress", () =>
    {
        var w = Started();
        Absorb(w, AttackKind.Pulse, GameSettings.LearnThreshold(AttackKind.Pulse) - 1);
        Absorb(w, AttackKind.Fan, GameSettings.LearnThreshold(AttackKind.Fan) - 1);
        Check(w.LearnedAttack is null, "Mixed attacks incorrectly pooled their progress");
        Absorb(w, AttackKind.Pulse, 1);
        Check(w.LearnedAttack == AttackKind.Pulse, "Pulse acquisition failed");
        Check(LearnableAttacks().All(k => w.LearningProgress(k) == 0), "Acquisition retained stale progress");
        Absorb(w, AttackKind.Fan, 1);
        Check(w.LearnedAttack == AttackKind.Pulse, "One leftover fan absorption immediately replaced the weapon");
        Absorb(w, AttackKind.Fan, GameSettings.LearnThreshold(AttackKind.Fan) - 1);
        Check(w.LearnedAttack == AttackKind.Fan, "Newly learned attack did not replace the old one");
    }),
    ("Held weapon persists when blue bullets are avoided or its own type is absorbed", () =>
    {
        var w = Started(); Learn(w, AttackKind.Nova); int score = w.Score;
        Absorb(w, AttackKind.Nova, GameSettings.LearnThreshold(AttackKind.Nova) * 2);
        Check(w.LearnedAttack == AttackKind.Nova && w.Score > score, "Held attack was lost or absorption stopped scoring");
        var avoided = new Bullet(w.PlayerPosition + new Vector2(35, 0), Vector2.Zero, true, AttackKind.Pulse, true);
        w.Bullets.Add(avoided); Tick(w, 30);
        Check(w.LearnedAttack == AttackKind.Nova && w.LearningProgress(AttackKind.Pulse) == 0 && avoided.Alive,
            "Avoiding another attack changed the held weapon");
    }),
    ("Shot modes cycle only after learning and emit the selected attack types", () =>
    {
        var w = Started(); Switch(w);
        Check(w.ShotMode == ShotMode.Normal, "Unlearned player could select a missing weapon");
        Learn(w, AttackKind.Fan);
        Check(ShotKinds(w).SetEquals([AttackKind.Basic]), "Normal mode fired learned bullets");
        Switch(w);
        Check(w.ShotMode == ShotMode.Learned && ShotKinds(w).SetEquals([AttackKind.Fan]),
            "Learned mode emitted incorrect attacks");
        Switch(w);
        Check(w.ShotMode == ShotMode.Combined && ShotKinds(w).SetEquals([AttackKind.Basic, AttackKind.Fan]),
            "Combined mode did not fire both attacks");
        Switch(w);
        Check(w.ShotMode == ShotMode.Normal && ShotKinds(w).SetEquals([AttackKind.Basic]),
            "Shot cycle did not return to normal");
    }),
    ("Learned attacks can be fired without consuming ammunition", () =>
    {
        var w = Started(); Learn(w, AttackKind.Seeker); Switch(w); int emitted = 0;
        for (int i = 0; i < 1200; i++)
        {
            w.Enemies.Clear(); w.Bullets.Clear(); Tick(w, 1, new(Vector2.Zero, true, false));
            emitted += w.Bullets.Count(b => !b.Hostile && b.Kind == AttackKind.Seeker);
        }
        Check(emitted > GameSettings.LearnThreshold(AttackKind.Seeker) * 3 && w.LearnedAttack == AttackKind.Seeker,
            "Learned weapon exhausted its supply");
        Check(w.Bombs == GameSettings.InitialBombs, "Firing consumed emergency bombs");
    }),
    ("Same-type learned shots cancel either color and counter the actual firing enemy", () =>
    {
        foreach (var kind in LearnableAttacks())
        foreach (bool blue in new[] { false, true })
        {
            var w = Started(); var source = Target(new(530, 150), 100); var decoy = Target(new(200, 200), 100);
            w.Enemies.Add(source); w.Enemies.Add(decoy);
            var hostile = new Bullet(new(200, 300), Vector2.Zero, true, kind, blue, source);
            var friendly = new Bullet(new(200, 300), Vector2.Zero, false, kind);
            w.Bullets.Add(hostile); w.Bullets.Add(friendly); Tick(w, 1);
            Check(!hostile.Alive && !friendly.Alive && w.AnnihilationCount == 1,
                $"{kind} did not annihilate {(blue ? "blue" : "red")} counterpart");
            Near(source.Health, 100 - GameSettings.CounterDamage(kind)); Near(decoy.Health, 100);
            Check(w.CounterEffects.Count > 0 && w.Score > 0 && w.TotalAbsorbed == 0,
                "Annihilation lacked an effect, score, or was mistaken for learning");
        }
    }),
    ("Different attacks and normal shots cannot annihilate hostile bullets", () =>
    {
        foreach (var pair in new[]
        {
            (Friendly: AttackKind.Fan, Hostile: AttackKind.Pulse),
            (Friendly: AttackKind.Basic, Hostile: AttackKind.Pulse),
            (Friendly: AttackKind.Basic, Hostile: AttackKind.Basic)
        })
        {
            var w = Started();
            var hostile = new Bullet(new(200, 300), Vector2.Zero, true, pair.Hostile);
            var friendly = new Bullet(new(200, 300), Vector2.Zero, false, pair.Friendly);
            w.Bullets.Add(hostile); w.Bullets.Add(friendly); Tick(w, 1);
            Check(hostile.Alive && friendly.Alive && w.AnnihilationCount == 0, "Unmatched attack annihilated");
        }
    }),
    ("Fast bullets cannot tunnel through enemies or matching hostile bullets", () =>
    {
        var hit = Started(); var target = Target(new(250, 250), 2); hit.Enemies.Add(target);
        hit.Bullets.Add(new(new(250, 350), new(0, -24000), false)); Tick(hit, 1);
        Near(target.Health, 1);
        var counter = Started();
        counter.Bullets.Add(new(new(250, 350), new(0, -24000), false, AttackKind.Pulse));
        counter.Bullets.Add(new(new(250, 250), Vector2.Zero, true, AttackKind.Pulse)); Tick(counter, 1);
        Check(counter.AnnihilationCount == 1 && counter.Bullets.Count == 0, "Fast matching shot tunneled through");
    }),
    ("Normal shots kill enemies once and award their configured score", () =>
    {
        var w = Started(); var enemy = Target(new(200, 200), 2); w.Enemies.Add(enemy);
        w.Bullets.Add(new(enemy.Position, Vector2.Zero, false)); Tick(w, 1);
        Check(enemy.Alive && enemy.Health == 1 && w.Score == 0, "Nonlethal normal hit was miscounted");
        w.Bullets.Add(new(enemy.Position, Vector2.Zero, false));
        w.Bullets.Add(new(enemy.Position, Vector2.Zero, false)); Tick(w, 1);
        Check(!enemy.Alive && w.Score == enemy.Points && w.BestScore == enemy.Points, "Kill was not scored exactly once");
        Check(w.Bullets.Count(b => !b.Hostile) == 1, "Dead enemy consumed another shot");
    }),
    ("Prism changes color for each direct hit but not counter damage", () =>
    {
        var w = Started();
        var prism = new Enemy(new(200, 200), EnemyKind.Prism, 0)
        {
            Health = 100, MaxHealth = 100, ShotTimer = float.MaxValue
        };
        w.Enemies.Add(prism);
        w.Bullets.Add(new(prism.Position, Vector2.Zero, false) { Damage = 2 }); Tick(w, 1);
        Check(prism.BluePhase && prism.Health == 98, "Direct hit did not switch the Prism from red to blue");
        w.Bullets.Add(new(new(200, 300), Vector2.Zero, true, AttackKind.Pulse, false, prism));
        w.Bullets.Add(new(new(200, 300), Vector2.Zero, false, AttackKind.Pulse)); Tick(w, 1);
        Check(prism.BluePhase, "Indirect counter damage changed Prism color");
        Near(prism.Health, 98 - GameSettings.CounterDamage(AttackKind.Pulse));
        w.Bullets.Add(new(prism.Position, Vector2.Zero, false) { Damage = 2 }); Tick(w, 1);
        Check(!prism.BluePhase, "Second direct hit did not switch the Prism back to red");
    }),
    ("Evaded seekers turn blue without changing speed or damage", () =>
    {
        var redWorld = Started(); var blueWorld = Started();
        var red = new Bullet(new(100, 200), new(0, 155), true, AttackKind.Seeker) { BlueAfter = 0.1f };
        var blue = new Bullet(new(100, 200), new(0, 155), true, AttackKind.Seeker, true) { BlueAfter = 0.1f };
        redWorld.Bullets.Add(red); blueWorld.Bullets.Add(blue);
        float damage = red.Damage;
        Tick(redWorld, 10); Tick(blueWorld, 10);
        Check(!red.Absorbable && blue.Absorbable, "Seeker changed color before its evasion window");
        Check(red.Position == blue.Position && red.Velocity == blue.Velocity && red.Damage == blue.Damage,
            "Color changed seeker movement or firepower");
        Tick(redWorld, 4); Tick(blueWorld, 4);
        Check(red.Absorbable && red.Damage == damage, "Evaded seeker did not turn blue with its damage preserved");
        Near(red.Velocity.Length(), 155);
        Check(red.Position == blue.Position && red.Velocity == blue.Velocity, "Color transition altered seeker motion");
    }),
    ("Bomb cancels every attack and color within its field without learning", () =>
    {
        var w = Vulnerable(); var source = Target(new(350, 160), 1000); w.Enemies.Add(source);
        var near = new List<Bullet>();
        foreach (var kind in Enum.GetValues<AttackKind>())
        foreach (bool blue in new[] { false, true })
        {
            var bullet = new Bullet(w.PlayerPosition + new Vector2(20, 0), Vector2.Zero, true, kind, blue, source);
            near.Add(bullet); w.Bullets.Add(bullet);
        }
        var far = new Bullet(w.PlayerPosition - new Vector2(0, GameSettings.BombRadius + 100),
            Vector2.Zero, true, AttackKind.Nova, true, source);
        var friendly = new Bullet(w.PlayerPosition + new Vector2(40, 0), Vector2.Zero, false);
        w.Bullets.Add(far); w.Bullets.Add(friendly);
        Tick(w, 1, new(Vector2.Zero, false, false, true, false, true));
        Check(w.Bombs == GameSettings.InitialBombs - 1 && w.BombTime > 0, "Bomb did not activate or consume stock");
        Check(near.All(b => !b.Alive) && far.Alive && friendly.Alive, "Bomb range or bullet ownership was wrong");
        Check(w.Lives == GameSettings.InitialLives && w.TotalAbsorbed == 0 && w.LearnedAttack is null,
            "Bomb was unsafe or incorrectly learned canceled attacks");
        Check(source.Health < 1000 && w.CounterEffects.Any(e => e.Bomb), "Bomb lacked offensive counter damage");
        w.Bullets.Add(new(w.PlayerPosition, Vector2.Zero, true, AttackKind.Seeker)); Tick(w, 1);
        Check(w.Lives == GameSettings.InitialLives && !w.Bullets.Any(b => b.Hostile && b.Kind == AttackKind.Seeker),
            "Active bomb failed to cancel newly arriving bullets");
    }),
    ("Bomb duration, stock limit and active-bomb presses are respected", () =>
    {
        var w = Started();
        for (int use = 0; use < GameSettings.InitialBombs; use++)
        {
            int stock = w.Bombs;
            Tick(w, 1, new(Vector2.Zero, false, false, false, false, true));
            Check(w.Bombs == stock - 1 && w.BombTime > GameSettings.BombDuration - 0.1f, "Bomb activation failed");
            Tick(w, 1, new(Vector2.Zero, false, false, false, false, true));
            Check(w.Bombs == stock - 1, "Active bomb consumed an extra charge");
            CleanTick(w, (int)((GameSettings.BombDuration - 0.2f) / GameSettings.FixedStep));
            Check(w.BombTime > 0, "Bomb ended early"); CleanTick(w, 60);
            Check(w.BombTime == 0, "Bomb did not expire");
        }
        Tick(w, 1, new(Vector2.Zero, false, false, false, false, true));
        Check(w.Bombs == 0 && w.BombTime == 0, "Empty bomb stock activated another bomb");
    }),
    ("Pause freezes movement, bullets, bombs and mode changes", () =>
    {
        var w = Started(); Learn(w, AttackKind.Pulse);
        Tick(w, 1, new(Vector2.Zero, false, false, false, false, true));
        var bullet = new Bullet(new(100, 150), new(0, -100), false); w.Bullets.Add(bullet); w.TogglePause();
        float elapsed = w.Elapsed, bombTime = w.BombTime;
        Vector2 player = w.PlayerPosition, bulletPosition = bullet.Position; int stock = w.Bombs;
        Tick(w, 180, new(Vector2.One, true, true, true, true, true));
        Check(w.State == GameState.Paused && w.Elapsed == elapsed && w.BombTime == bombTime && w.Bombs == stock,
            "Paused simulation consumed time or bombs");
        Check(w.PlayerPosition == player && bullet.Position == bulletPosition && w.ShotMode == ShotMode.Normal,
            "Paused input changed gameplay");
        w.TogglePause(); Tick(w, 1);
        Check(w.State == GameState.Playing && w.Elapsed > elapsed && w.BombTime < bombTime, "Resume failed");
    }),
    ("Damage preserves learning, grants recovery and reaches game over", () =>
    {
        var w = Vulnerable(); Learn(w, AttackKind.Nova); Absorb(w, AttackKind.Fan, 2);
        for (int hit = 0; hit < GameSettings.InitialLives; hit++)
        {
            if (hit > 0) CleanTick(w, (int)(GameSettings.InvincibleSeconds / GameSettings.FixedStep) + 2);
            for (int i = 0; i < 3; i++) w.Bullets.Add(new(w.PlayerPosition, Vector2.Zero, true, AttackKind.Pulse));
            Tick(w, 1);
            Check(w.Lives == GameSettings.InitialLives - hit - 1, "A single collision group removed multiple lives");
            Check(w.LearnedAttack == AttackKind.Nova && w.LearningProgress(AttackKind.Fan) == 2,
                "Death discarded learned weapon or partial progress");
            Check(w.Bullets.All(b => !b.Hostile), "Damage did not clear hostile bullets");
            Check(w.Bombs == GameSettings.InitialBombs, "Damage changed bomb stock");
            w.Bullets.Add(new(w.PlayerPosition, Vector2.Zero, true, AttackKind.Pulse)); Tick(w, 1);
            Check(w.Lives == GameSettings.InitialLives - hit - 1, "Recovery invincibility failed");
        }
        Check(w.State == GameState.GameOver, "Zero lives did not finish the game"); float elapsed = w.Elapsed;
        Tick(w, 120, new(Vector2.One, true, false, true, true, true));
        Check(w.Elapsed == elapsed && w.Bombs == GameSettings.InitialBombs, "Game-over input advanced gameplay");
    }),
    ("Restart resets the run and retains only the best score", () =>
    {
        var w = Started(); Learn(w, AttackKind.Fan); Switch(w); Absorb(w, AttackKind.Pulse, 3);
        Tick(w, 1, new(Vector2.Zero, false, false, true, false, true)); int best = w.BestScore;
        Check(best > 0, "Setup did not produce a best score"); w.Start();
        Check(w.State == GameState.Playing && w.Lives == GameSettings.InitialLives && w.Elapsed == 0 && w.Score == 0,
            "Restart did not reset run state");
        Check(w.BestScore == best && w.LearnedAttack is null && w.ShotMode == ShotMode.Normal,
            "Restart did not reset weapon or preserve best score");
        Check(w.Bombs == GameSettings.InitialBombs && w.BombTime == 0 && !w.FieldActive, "Restart did not reset fields and bombs");
        Check(w.TotalAbsorbed == 0 && w.AnnihilationCount == 0 && w.LastAbsorbedAttack is null &&
            LearnableAttacks().All(k => w.LearningProgress(k) == 0), "Restart retained learning/counter statistics");
        Check(w.Bullets.Count == 0 && w.Enemies.Count == 0 && w.Particles.Count == 0 && w.CounterEffects.Count == 0,
            "Restart retained entities/effects");
        Check(w.Stage == StagePhase.Approach && w.Boss is null, "Restart retained the previous stage");
    }),
    ("Authored stage reaches a repeating boss and normal shots can clear it", () =>
    {
        var w = Started(); var phases = new HashSet<StagePhase>();
        var attacks = new HashSet<AttackKind>(); var colors = new HashSet<bool>();
        int frames = (int)((GameSettings.StageDuration + 1) / GameSettings.FixedStep);
        for (int i = 0; i < frames && w.Boss is null; i++)
        {
            Tick(w, 1); phases.Add(w.Stage);
            foreach (var b in w.Bullets.Where(b => b.Hostile)) { attacks.Add(b.Kind); colors.Add(b.Absorbable); }
            // Remove incoming hazards after observing attacks; retain enemies long enough to fire.
            w.Bullets.Clear();
            w.Enemies.RemoveAll(e => e.Kind != EnemyKind.Boss && e.Position.Y > w.PlayerPosition.Y - 100);
        }
        Check(phases.SetEquals(Enum.GetValues<StagePhase>()), "Authored stage skipped a phase");
        Check(attacks.Count >= 3 && colors.SetEquals([false, true]), "Stage did not demonstrate varied red and blue attacks");
        var boss = w.Boss;
        Check(boss is not null && boss.Alive && w.State == GameState.Playing, "Timed stage did not reach a living boss");
        var patterns = new HashSet<BossPattern>(); int novaEntries = 0; BossPattern? previous = null;
        for (int i = 0; i < (int)((GameSettings.BossCycleDuration * 2 + 2) / GameSettings.FixedStep); i++)
        {
            Tick(w, 1); patterns.Add(w.BossAttack);
            if (w.BossAttack == BossPattern.Nova && previous != BossPattern.Nova) novaEntries++;
            previous = w.BossAttack; w.Bullets.Clear();
        }
        Check(patterns.SetEquals(Enum.GetValues<BossPattern>()) && novaEntries >= 2,
            "Boss did not repeat its learn/hold/counter cycle");
        Check(w.LearnedAttack is null, "Stage survival granted an unsolicited learned attack");
        // Apply actual normal-shot damage to full boss health, without learning or bombs.
        for (int i = 0; i < 2000 && w.State == GameState.Playing; i++)
        {
            w.Bullets.Add(new(boss!.Position, Vector2.Zero, false, AttackKind.Basic)); Tick(w, 1);
            w.Bullets.RemoveAll(b => b.Hostile);
        }
        Check(w.State == GameState.StageClear && !boss!.Alive && w.Score >= boss.Points,
            "Normal shots could not complete the stage");
        float elapsed = w.Elapsed; Tick(w, 120, new(Vector2.One, true, false, true, true, true)); w.TogglePause();
        Check(w.State == GameState.StageClear && w.Elapsed == elapsed, "Completed stage kept simulating");
        w.Start();
        Check(w.Stage == StagePhase.Approach && w.Boss is null && w.Elapsed == 0, "Clear-to-restart retained the boss");
    }),
    ("One authored Nova barrage can be learned in its central lane without damage", () =>
    {
        var w = Started();
        // Skip earlier hazards to isolate the real, normally timed finale window.
        int setupLimit = (int)((GameSettings.StageDuration + GameSettings.BossCycleDuration + 2) / GameSettings.FixedStep);
        for (int i = 0; i < setupLimit && (w.Boss is null || w.BossAttack != BossPattern.Nova); i++)
        {
            Tick(w, 1); w.Bullets.Clear();
            w.Enemies.RemoveAll(e => e.Kind != EnemyKind.Boss && e.Position.Y > w.PlayerPosition.Y - 100);
        }
        Check(w.Boss is not null && w.BossAttack == BossPattern.Nova && w.InvincibleTime == 0,
            "Setup did not reach a vulnerable player at the authored Nova window");
        int initialLives = w.Lives;
        // From here retain every authored red/blue projectile and use only the absorption field.
        for (int i = 0; i < (int)(GameSettings.BossCycleDuration / GameSettings.FixedStep); i++)
        {
            Tick(w, 1, new(Vector2.Zero, false, false, true));
            if (w.BossAttack != BossPattern.Nova || w.State != GameState.Playing) break;
        }
        Check(w.LearnedAttack == AttackKind.Nova && w.TotalAbsorbed >= GameSettings.LearnThreshold(AttackKind.Nova),
            "A full central-lane Nova barrage did not supply enough reachable learning bullets");
        Check(w.Lives == initialLives && w.InvincibleTime == 0 && w.Bombs == GameSettings.InitialBombs,
            "Learning the authored Nova required damage recovery or a bomb");
    })
};

int failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {e.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static GameWorld Started() { var world = new GameWorld(42); world.Start(); return world; }
static GameWorld Vulnerable()
{
    var world = Started(); CleanTick(world, (int)(GameSettings.InvincibleSeconds / GameSettings.FixedStep) + 2);
    Check(world.InvincibleTime == 0, "Test setup remained invincible"); return world;
}
static Enemy Target(Vector2 position, float health) => new(position, EnemyKind.Scout, 0)
{
    Health = health, MaxHealth = health, ShotTimer = float.MaxValue
};
static AttackKind[] LearnableAttacks() => [AttackKind.Pulse, AttackKind.Fan, AttackKind.Seeker, AttackKind.Nova];
static void Absorb(GameWorld world, AttackKind kind, int count)
{
    for (int i = 0; i < count; i++)
        world.Bullets.Add(new(world.PlayerPosition + new Vector2(20, 0), Vector2.Zero, true, kind, true));
    Tick(world, 1, new(Vector2.Zero, false, false, true));
}
static void Learn(GameWorld world, AttackKind kind)
{
    Absorb(world, kind, GameSettings.LearnThreshold(kind));
    Check(world.LearnedAttack == kind, $"Setup could not learn {kind}");
}
static void Switch(GameWorld world) => Tick(world, 1, new(Vector2.Zero, false, false, false, true));
static HashSet<AttackKind> ShotKinds(GameWorld world)
{
    var kinds = new HashSet<AttackKind>();
    for (int i = 0; i < 90; i++)
    {
        world.Bullets.Clear(); world.Enemies.Clear(); Tick(world, 1, new(Vector2.Zero, true, false));
        foreach (var bullet in world.Bullets.Where(b => !b.Hostile)) kinds.Add(bullet.Kind);
    }
    world.Bullets.Clear(); world.Enemies.Clear(); return kinds;
}
static void Tick(GameWorld world, int count, PlayerInput input = default)
{
    for (int i = 0; i < count; i++) world.Update(GameSettings.FixedStep, input);
}
static void CleanTick(GameWorld world, int count, PlayerInput input = default)
{
    for (int i = 0; i < count; i++)
    {
        world.Enemies.Clear(); world.Bullets.Clear(); Tick(world, 1, input);
    }
    world.Enemies.Clear(); world.Bullets.Clear();
}
static void Near(float actual, float expected, float tolerance = 0.01f) =>
    Check(MathF.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

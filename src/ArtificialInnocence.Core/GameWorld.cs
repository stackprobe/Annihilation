using System.Numerics;

namespace ArtificialInnocence.Core;

// Deterministic gameplay, independent of raylib and the rendering frame rate.
public sealed partial class GameWorld
{
    private readonly Random random;
    private readonly int[] learning = new int[Enum.GetValues<AttackKind>().Length];
    private float normalShotTimer;
    private float learnedShotTimer;
    public GameWorld(int seed = 12345) { random = new Random(seed); }

    public GameState State { get; private set; } = GameState.Title;
    public Vector2 PlayerPosition { get; private set; } = new(GameSettings.FieldWidth / 2f, 710);
    public int Lives { get; private set; } = GameSettings.InitialLives;
    public int Score { get; private set; }
    public int BestScore { get; private set; }
    public float Elapsed { get; private set; }
    public float InvincibleTime { get; private set; }
    public AttackKind? LearnedAttack { get; private set; }
    public AttackKind? LastAbsorbedAttack { get; private set; }
    public ShotMode ShotMode { get; private set; } = ShotMode.Normal;
    public int TotalAbsorbed { get; private set; }
    public int AnnihilationCount { get; private set; }
    public int Bombs { get; private set; } = GameSettings.InitialBombs;
    public float BombTime { get; private set; }
    public bool FieldActive { get; private set; }
    public string Notice { get; private set; } = "";
    public float NoticeTime { get; private set; }

    // Collections are also available to author encounters and run headless regressions.
    public List<Bullet> Bullets { get; } = [];
    public List<Enemy> Enemies { get; } = [];
    public List<Particle> Particles { get; } = [];
    public List<CounterEffect> CounterEffects { get; } = [];
    public int LearningProgress(AttackKind kind) => learning[(int)kind];

    public void Start()
    {
        State = GameState.Playing;
        PlayerPosition = new(GameSettings.FieldWidth / 2f, 710);
        Lives = GameSettings.InitialLives;
        Score = 0;
        Elapsed = 0;
        InvincibleTime = 1;
        normalShotTimer = learnedShotTimer = 0;
        LearnedAttack = LastAbsorbedAttack = null;
        Array.Clear(learning);
        ShotMode = ShotMode.Normal;
        TotalAbsorbed = AnnihilationCount = 0;
        Bombs = GameSettings.InitialBombs;
        BombTime = 0;
        FieldActive = false;
        Bullets.Clear();
        Enemies.Clear();
        Particles.Clear();
        CounterEffects.Clear();
        ResetStage();
        SetNotice("HOLD X TO ABSORB BLUE / AVOID RED", 5);
    }

    public void TogglePause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
        else if (State == GameState.Paused) State = GameState.Playing;
    }

    public void Update(float dt, PlayerInput input)
    {
        if (State != GameState.Playing || dt <= 0 || !float.IsFinite(dt)) return;
        dt = Math.Min(dt, 1f / 30f);
        Elapsed += dt;
        InvincibleTime = Math.Max(0, InvincibleTime - dt);
        BombTime = Math.Max(0, BombTime - dt);
        NoticeTime = Math.Max(0, NoticeTime - dt);
        normalShotTimer = Math.Max(0, normalShotTimer - dt);
        learnedShotTimer = Math.Max(0, learnedShotTimer - dt);

        var movement = input.Movement;
        if (!float.IsFinite(movement.X) || !float.IsFinite(movement.Y)) movement = Vector2.Zero;
        if (movement.LengthSquared() > 1) movement = Vector2.Normalize(movement);
        Vector2 previousPlayer = PlayerPosition;
        PlayerPosition += movement * (input.Focus ? GameSettings.FocusSpeed : GameSettings.PlayerSpeed) * dt;
        PlayerPosition = Vector2.Clamp(PlayerPosition, new(22, 80), new(GameSettings.FieldWidth - 22, GameSettings.Height - 30));
        FieldActive = input.Field;
        if (input.SwitchShot) CycleShot();
        if (input.Bomb && BombTime <= 0 && Bombs > 0)
        {
            Bombs--;
            BombTime = GameSettings.BombDuration;
            SetNotice("BOMB / ALL ATTACKS COUNTERED", 2);
            Burst(PlayerPosition, true);
        }
        if (input.Shooting) FirePlayer();
        UpdateStage(dt);
        MoveBullets(dt);

        // The bomb takes precedence: cancelled bullets never become learning progress.
        foreach (var bullet in Bullets)
        {
            if (!bullet.Alive || !bullet.Hostile) continue;
            if (BombTime > 0 && SweptOverlap(bullet.PreviousPosition - previousPlayer,
                bullet.Position - PlayerPosition, Vector2.Zero, GameSettings.BombRadius + bullet.Radius))
            {
                Counter(bullet, true);
            }
            else if (FieldActive && bullet.Absorbable && SweptOverlap(bullet.PreviousPosition - previousPlayer,
                bullet.Position - PlayerPosition, Vector2.Zero, GameSettings.FieldRadius + bullet.Radius))
            {
                Absorb(bullet);
            }
        }

        // Relative sweeps catch fast opposing bullets even between fixed simulation ticks.
        foreach (var friendly in Bullets)
        {
            if (!friendly.Alive || friendly.Hostile || friendly.Kind == AttackKind.Basic) continue;
            foreach (var hostile in Bullets)
            {
                if (!hostile.Alive || !hostile.Hostile || hostile.Kind != friendly.Kind) continue;
                if (!SweptOverlap(friendly.PreviousPosition - hostile.PreviousPosition,
                    friendly.Position - hostile.Position, Vector2.Zero, friendly.Radius + hostile.Radius)) continue;
                friendly.Alive = false;
                Counter(hostile, false);
                break;
            }
        }

        foreach (var bullet in Bullets)
        {
            if (!bullet.Alive || State != GameState.Playing) continue;
            if (bullet.Hostile)
            {
                if (InvincibleTime <= 0 && SweptOverlap(bullet.PreviousPosition - previousPlayer,
                    bullet.Position - PlayerPosition, Vector2.Zero, bullet.Radius + GameSettings.PlayerRadius))
                    DamagePlayer();
            }
            else
            {
                foreach (var enemy in Enemies)
                {
                    if (!enemy.Alive || !SweptOverlap(bullet.PreviousPosition, bullet.Position, enemy.Position, bullet.Radius + enemy.Radius)) continue;
                    bullet.Alive = false;
                    if (enemy.Kind == EnemyKind.Prism) enemy.BluePhase = !enemy.BluePhase;
                    DamageEnemy(enemy, bullet.Damage);
                    break;
                }
            }
        }

        foreach (var enemy in Enemies)
        {
            if (!enemy.Alive || InvincibleTime > 0 || State != GameState.Playing) continue;
            if (Overlaps(enemy.Position, enemy.Radius, PlayerPosition, GameSettings.PlayerRadius))
                DamagePlayer();
        }
        foreach (var particle in Particles)
        {
            particle.Position += particle.Velocity * dt;
            particle.Life -= dt;
        }
        foreach (var effect in CounterEffects) effect.Life -= dt;
        Bullets.RemoveAll(b => !b.Alive);
        Enemies.RemoveAll(e => !e.Alive);
        Particles.RemoveAll(p => p.Life <= 0);
        CounterEffects.RemoveAll(e => e.Life <= 0);
    }

    private void CycleShot()
    {
        if (LearnedAttack is null)
        {
            SetNotice("ABSORB BLUE TO LEARN A WEAPON", 2);
            return;
        }
        ShotMode = (ShotMode)(((int)ShotMode + 1) % 3);
        SetNotice(ShotMode switch
        {
            ShotMode.Normal => "SHOT / NORMAL", ShotMode.Learned => "SHOT / LEARNED", _ => "SHOT / NORMAL + LEARNED"
        }, 1.5f);
    }

    private void FirePlayer()
    {
        if (ShotMode is ShotMode.Normal or ShotMode.Combined && normalShotTimer <= 0)
        {
            // One impact per shot keeps the Prism turret's color switch legible.
            Bullets.Add(new(PlayerPosition + new Vector2(0, -22), new(0, -GameSettings.BulletSpeed), false) { Damage = 2 });
            normalShotTimer = GameSettings.ShotInterval;
        }
        if (ShotMode == ShotMode.Normal || LearnedAttack is not { } kind || learnedShotTimer > 0) return;
        switch (kind)
        {
            case AttackKind.Pulse:
                AddLearned(kind, new(-10, -23), 0, 560);
                AddLearned(kind, new(10, -23), 0, 560);
                break;
            case AttackKind.Fan:
                for (int i = -2; i <= 2; i++) AddLearned(kind, new(0, -23), i * 0.13f, 500);
                break;
            case AttackKind.Seeker:
                AddLearned(kind, new(0, -23), 0, 480);
                break;
            case AttackKind.Nova:
                for (int i = -1; i <= 1; i++) AddLearned(kind, new(i * 22, -25), i * 0.025f, 540);
                break;
        }
        learnedShotTimer = GameSettings.LearnedInterval(kind);
    }

    private void AddLearned(AttackKind kind, Vector2 offset, float angle, float speed) =>
        Bullets.Add(new(PlayerPosition + offset, Rotate(-Vector2.UnitY, angle) * speed, false, kind));

    private void MoveBullets(float dt)
    {
        foreach (var bullet in Bullets)
        {
            if (!bullet.Alive) continue;
            bullet.PreviousPosition = bullet.Position;
            bullet.Age += dt;
            if (bullet.BlueAfter > 0 && bullet.Age >= bullet.BlueAfter) bullet.Absorbable = true;
            if (bullet.Kind == AttackKind.Seeker)
            {
                Vector2? target = null;
                // Turning is age-based, independent of the projectile's red/blue state.
                if (bullet.Hostile && bullet.Age < 1.35f) target = PlayerPosition;
                else if (!bullet.Hostile) target = NearestEnemy(bullet.Position)?.Position;
                if (target is { } position)
                {
                    Vector2 delta = position - bullet.Position;
                    if (delta.LengthSquared() > 1)
                    {
                        float speed = bullet.Velocity.Length();
                        Vector2 direction = Vector2.Lerp(SafeDirection(bullet.Velocity), Vector2.Normalize(delta), dt * (bullet.Hostile ? 1.2f : 4f));
                        bullet.Velocity = SafeDirection(direction) * speed;
                    }
                }
            }
            bullet.Position += bullet.Velocity * dt;
            if (bullet.Position.Y < -80 || bullet.Position.Y > GameSettings.Height + 80 ||
                bullet.Position.X < -80 || bullet.Position.X > GameSettings.FieldWidth + 80)
                bullet.Alive = false;
        }
    }

    private void Absorb(Bullet bullet)
    {
        bullet.Alive = false;
        TotalAbsorbed++;
        LastAbsorbedAttack = bullet.Kind;
        AddScore(25);
        SmallBurst(bullet.Position, true, 4);
        if (bullet.Kind == AttackKind.Basic || LearnedAttack == bullet.Kind) return;
        int threshold = GameSettings.LearnThreshold(bullet.Kind);
        if (++learning[(int)bullet.Kind] < threshold) return;
        bool replaced = LearnedAttack.HasValue;
        LearnedAttack = bullet.Kind;
        Array.Clear(learning);
        learnedShotTimer = 0;
        AddScore(500);
        SetNotice($"{GameSettings.AttackName(bullet.Kind)} {(replaced ? "REPLACED WEAPON" : "LEARNED / C TO SWITCH")}", 3.5f);
        Burst(PlayerPosition, true);
    }

    private void Counter(Bullet hostile, bool bomb)
    {
        hostile.Alive = false;
        AnnihilationCount++;
        var target = hostile.Source is { Alive: true } source ? source : NearestEnemy(hostile.Position);
        CounterEffects.Add(new(hostile.Position, target?.Position ?? hostile.Position - new Vector2(0, 100), hostile.Kind, bomb));
        SmallBurst(hostile.Position, true, bomb ? 3 : 6);
        AddScore(bomb ? 30 : hostile.Kind == AttackKind.Nova ? 300 : 100);
        if (target is not null)
            DamageEnemy(target, bomb ? 2f : GameSettings.CounterDamage(hostile.Kind));
    }

    private Enemy? NearestEnemy(Vector2 position)
    {
        Enemy? nearest = null;
        float distance = float.MaxValue;
        foreach (var enemy in Enemies)
        {
            if (!enemy.Alive) continue;
            float candidate = Vector2.DistanceSquared(enemy.Position, position);
            if (candidate >= distance) continue;
            distance = candidate;
            nearest = enemy;
        }
        return nearest;
    }

    private void DamageEnemy(Enemy enemy, float damage)
    {
        if (!enemy.Alive || damage <= 0) return;
        enemy.Health -= damage;
        enemy.HitFlash = 0.08f;
        if (enemy.Health > 0) return;
        enemy.Health = 0;
        enemy.Alive = false;
        AddScore(enemy.Points);
        Burst(enemy.Position, false);
        if (enemy.Kind != EnemyKind.Boss) return;
        State = GameState.StageClear;
        FieldActive = false;
        BombTime = 0;
        foreach (var bullet in Bullets) bullet.Alive = false;
        foreach (var other in Enemies) other.Alive = false;
        AddScore(Lives * 2000 + Bombs * 1000);
        SetNotice("SECTOR LIBERATED", 5);
    }

    private void DamagePlayer()
    {
        if (InvincibleTime > 0 || State != GameState.Playing) return;
        Lives--;
        Burst(PlayerPosition, false);
        InvincibleTime = GameSettings.InvincibleSeconds;
        foreach (var bullet in Bullets)
            if (bullet.Hostile) bullet.Alive = false;
        if (Lives <= 0)
        {
            State = GameState.GameOver;
            FieldActive = false;
            BombTime = 0;
        }
    }

    private void AddScore(int points)
    {
        Score += points;
        BestScore = Math.Max(BestScore, Score);
    }
    private void SetNotice(string text, float seconds = 3)
    {
        Notice = text;
        NoticeTime = seconds;
    }
    private void Burst(Vector2 position, bool cyan) => SmallBurst(position, cyan, 20);
    private void SmallBurst(Vector2 position, bool cyan, int count)
    {
        for (int i = 0; i < count && Particles.Count < 800; i++)
        {
            float angle = random.NextSingle() * MathF.Tau;
            Particles.Add(new(position, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * random.Next(35, 200), cyan));
        }
    }
    private static Vector2 SafeDirection(Vector2 vector) => vector.LengthSquared() > 0.0001f ? Vector2.Normalize(vector) : -Vector2.UnitY;
    private static Vector2 Rotate(Vector2 v, float angle) => new(
        v.X * MathF.Cos(angle) - v.Y * MathF.Sin(angle), v.X * MathF.Sin(angle) + v.Y * MathF.Cos(angle));
    private static bool SweptOverlap(Vector2 start, Vector2 end, Vector2 center, float radius)
    {
        Vector2 travel = end - start;
        float lengthSquared = travel.LengthSquared();
        float t = lengthSquared > 0.000001f ? Math.Clamp(Vector2.Dot(center - start, travel) / lengthSquared, 0, 1) : 0;
        return Vector2.DistanceSquared(start + travel * t, center) <= radius * radius;
    }
    public static bool Overlaps(Vector2 a, float ar, Vector2 b, float br) =>
        Vector2.DistanceSquared(a, b) <= (ar + br) * (ar + br);
}

using System.Numerics;

namespace Annihilation.Core;

public sealed partial class GameWorld
{
    private static readonly float[] FormationTimes =
        [0.8f, 6f, 11.5f, 18.4f, 22.5f, 26.4f, 29.7f, 34f, 39f, 43f, 46f, 51f, 55f];
    private int nextFormation;
    private bool bossSpawned;
    private int bossSegment;
    private float bossShotTimer;
    private float bossSideTimer;

    public StagePhase Stage { get; private set; } = StagePhase.Approach;
    public Enemy? Boss => Enemies.FirstOrDefault(e => e.Alive && e.Kind == EnemyKind.Boss);
    public BossPattern BossAttack { get; private set; } = BossPattern.Pulse;
    public float BossPatternTime { get; private set; }
    // A short charge cue before the first volley of each pattern.
    public float BossTelegraph => Boss is not null && BossPatternTime < 0.85f
        ? 1 - BossPatternTime / 0.85f : 0;
    public float BossFinaleLaneX => GameSettings.FieldWidth / 2f;

    private void ResetStage()
    {
        nextFormation = 0;
        bossSpawned = false;
        bossSegment = -1;
        bossShotTimer = 0;
        bossSideTimer = 0;
        Stage = StagePhase.Approach;
        BossAttack = BossPattern.Pulse;
        BossPatternTime = 0;
    }

    private void UpdateStage(float dt)
    {
        var nextStage = Elapsed < 18 ? StagePhase.Approach
            : Elapsed < 38 ? StagePhase.Pursuit
            : Elapsed < GameSettings.StageDuration ? StagePhase.Gate : StagePhase.Boss;
        if (Stage != nextStage)
        {
            Stage = nextStage;
            SetNotice(Stage switch
            {
                StagePhase.Pursuit => "MIXED ATTACKS / EVADE SEEKERS UNTIL THEY TURN BLUE",
                StagePhase.Gate => "PRISM GATE / EACH SHOT HIT SWITCHES TURRET COLOR",
                _ => "BOSS / LEARN, HOLD, THEN COUNTER THE NEXT ATTACK"
            }, 4);
        }

        while (nextFormation < FormationTimes.Length && Elapsed >= FormationTimes[nextFormation])
            SpawnFormation(nextFormation++);

        if (Stage == StagePhase.Boss && !bossSpawned)
        {
            // A clean entrance separates the learning stage from the boss encounter.
            foreach (var enemy in Enemies) enemy.Alive = false;
            foreach (var bullet in Bullets)
                if (bullet.Hostile) bullet.Alive = false;
            Enemies.Add(new(new(BossFinaleLaneX, -70), EnemyKind.Boss, 190));
            bossSpawned = true;
        }
        foreach (var enemy in Enemies)
        {
            if (!enemy.Alive) continue;
            enemy.Age += dt;
            enemy.HitFlash = Math.Max(0, enemy.HitFlash - dt);
            if (enemy.Kind == EnemyKind.Boss)
            {
                UpdateBoss(enemy, dt);
                continue;
            }

            MoveStageEnemy(enemy);
            enemy.ShotTimer -= dt;
            if (enemy.Kind == EnemyKind.Scout) enemy.BluePhase = enemy.Age >= 3;
            // Never shoot from behind the player or at point-blank range.
            if (enemy.ShotTimer <= 0 && enemy.Position.Y > 40 && enemy.Position.Y < PlayerPosition.Y - 110)
                FireStageEnemy(enemy);
            if (enemy.Position.Y > GameSettings.Height + 70) enemy.Alive = false;
        }
    }

    private void SpawnFormation(int index)
    {
        switch (index)
        {
            case 0:
                SpawnStageEnemy(350, EnemyKind.Scout);
                SetNotice("SPARE SCOUTS / THEY TURN BLUE AFTER A SHORT WAIT", 5);
                break;
            case 1:
                SpawnStageEnemy(210, EnemyKind.Scout);
                SpawnStageEnemy(490, EnemyKind.Scout);
                break;
            case 2:
                SpawnStageEnemy(130, EnemyKind.Scout);
                SpawnStageEnemy(350, EnemyKind.Scout);
                SpawnStageEnemy(570, EnemyKind.Scout);
                break;
            case 3:
                SpawnStageEnemy(180, EnemyKind.Striker, true);
                SpawnStageEnemy(520, EnemyKind.Striker);
                break;
            case 4:
                SpawnStageEnemy(350, EnemyKind.Hunter);
                SpawnStageEnemy(110, EnemyKind.Scout);
                break;
            case 5:
                SpawnStageEnemy(150, EnemyKind.Striker);
                SpawnStageEnemy(550, EnemyKind.Striker, true);
                break;
            case 6:
                SpawnStageEnemy(180, EnemyKind.Hunter);
                SpawnStageEnemy(520, EnemyKind.Hunter);
                break;
            case 7:
                SpawnStageEnemy(350, EnemyKind.Striker, true);
                SpawnStageEnemy(110, EnemyKind.Hunter);
                SpawnStageEnemy(590, EnemyKind.Hunter);
                break;
            case 8:
                SpawnStageEnemy(180, EnemyKind.Prism, true);
                SpawnStageEnemy(520, EnemyKind.Prism);
                break;
            case 9:
                SpawnStageEnemy(350, EnemyKind.Hunter);
                break;
            case 10:
                SpawnStageEnemy(350, EnemyKind.Prism, true);
                SpawnStageEnemy(100, EnemyKind.Striker);
                SpawnStageEnemy(600, EnemyKind.Striker, true);
                break;
            case 11:
                SpawnStageEnemy(140, EnemyKind.Prism);
                SpawnStageEnemy(560, EnemyKind.Prism, true);
                break;
            case 12:
                SpawnStageEnemy(350, EnemyKind.Hunter);
                break;
        }
    }

    private void SpawnStageEnemy(float x, EnemyKind kind, bool blue = false)
    {
        var enemy = new Enemy(new(x, -45), kind, kind == EnemyKind.Hunter ? 125 : 170)
        {
            BluePhase = blue,
            ShotTimer = kind == EnemyKind.Scout ? 1.65f : 1.9f
        };
        Enemies.Add(enemy);
    }

    private static void MoveStageEnemy(Enemy enemy)
    {
        if (enemy.Speed == 0) return;
        float holdY = enemy.Kind switch
        {
            EnemyKind.Prism => 185,
            EnemyKind.Hunter => 135,
            EnemyKind.Striker => 155,
            _ => 120
        };
        float leaveAt = enemy.Kind == EnemyKind.Prism ? 8.5f : 7.8f;
        enemy.Position.Y = Math.Min(holdY, enemy.Origin.Y + enemy.Age * enemy.Speed)
            + Math.Max(0, enemy.Age - leaveAt) * 165;
        float sway = enemy.Kind switch { EnemyKind.Striker => 52, EnemyKind.Hunter => 25, EnemyKind.Prism => 0, _ => 14 };
        enemy.Position.X = Math.Clamp(enemy.Origin.X + MathF.Sin(enemy.Age * 1.1f) * sway,
            enemy.Radius + 12, GameSettings.FieldWidth - enemy.Radius - 12);
    }

    private void FireStageEnemy(Enemy enemy)
    {
        switch (enemy.Kind)
        {
            case EnemyKind.Scout:
                // Waiting preserves a source of easily collected, tightly grouped blue shots.
                FireAimedSpread(enemy, AttackKind.Pulse, enemy.BluePhase, enemy.BluePhase ? 3 : 1, 0.055f, 178);
                enemy.ShotTimer = enemy.BluePhase ? 0.72f : 1.5f;
                break;
            case EnemyKind.Striker:
                FireAimedSpread(enemy, AttackKind.Fan, enemy.BluePhase, 5, 0.115f, 180);
                enemy.BluePhase = !enemy.BluePhase;
                enemy.ShotTimer = 1.35f;
                break;
            case EnemyKind.Hunter:
                FireAimedSpread(enemy, AttackKind.Seeker, false, 2, 0.15f, 155, 1.6f);
                enemy.ShotTimer = 1.6f;
                break;
            case EnemyKind.Prism:
                // BluePhase is controlled exclusively by direct player shot hits in GameWorld.
                FireAimedSpread(enemy, AttackKind.Fan, enemy.BluePhase, 3, 0.095f, 180);
                enemy.ShotTimer = 0.9f;
                break;
        }
        enemy.ShotsFired++;
    }

    private void UpdateBoss(Enemy boss, float dt)
    {
        float combatTime = Math.Max(0, boss.Age - 1.1f);
        float cycleTime = combatTime % GameSettings.BossCycleDuration;
        BossAttack = cycleTime < 5 ? BossPattern.Pulse : cycleTime < 10 ? BossPattern.Fan
            : cycleTime < 16 ? BossPattern.Seeker : BossPattern.Nova;
        BossPatternTime = cycleTime - (BossAttack switch { BossPattern.Fan => 5, BossPattern.Seeker => 10, BossPattern.Nova => 16, _ => 0 });
        float desiredX = BossAttack == BossPattern.Nova ? BossFinaleLaneX
            : BossFinaleLaneX + MathF.Sin(combatTime * 0.42f) * 118;
        boss.Position.X += Math.Clamp(desiredX - boss.Position.X, -200 * dt, 200 * dt);
        boss.Position.Y = Math.Min(115, -70 + boss.Age * boss.Speed) + MathF.Sin(combatTime * 0.8f) * 9;
        boss.ShotTimer = Math.Max(0, 0.85f - BossPatternTime);
        if (boss.Age < 1.1f) return;

        int segment = (int)(combatTime / GameSettings.BossCycleDuration) * 4 + (int)BossAttack;
        if (segment != bossSegment)
        {
            bossSegment = segment;
            bossShotTimer = 0.85f;
            bossSideTimer = 1.6f;
            boss.ShotsFired = 0;
            boss.BluePhase = BossAttack != BossPattern.Seeker;
            SetNotice(BossAttack switch
            {
                BossPattern.Pulse => "PULSE / EVADE RED, READ BLUE",
                BossPattern.Fan => "FAN / CHOOSE TO KEEP OR REPLACE YOUR WEAPON",
                BossPattern.Seeker => "SEEKER / EVADE THE CHASE TO TURN THEM BLUE",
                _ => LearnedAttack == AttackKind.Nova
                    ? "NOVA / MATCH THEIR SHOTS. COUNTER NOW!"
                    : "NOVA / ABSORB THE BLUE CENTER, COUNTER NEXT TIME"
            }, BossAttack == BossPattern.Nova ? 5.5f : 2.7f);
        }

        bossShotTimer -= dt;
        bossSideTimer -= dt;
        if (bossShotTimer <= 0)
        {
            switch (BossAttack)
            {
                case BossPattern.Pulse:
                    FireAimedSpread(boss, AttackKind.Pulse, boss.BluePhase, 5, 0.085f, 190);
                    bossShotTimer += 0.62f;
                    boss.BluePhase = (boss.ShotsFired + 1) % 3 != 2;
                    break;
                case BossPattern.Fan:
                    float fanSweep = MathF.Sin(boss.ShotsFired * 0.85f) * 0.19f;
                    FireDownwardSpread(boss, AttackKind.Fan, boss.BluePhase, 7, 0.19f, 184, fanSweep);
                    bossShotTimer += 0.82f;
                    boss.BluePhase = !boss.BluePhase;
                    break;
                case BossPattern.Seeker:
                    FireAimedSpread(boss, AttackKind.Seeker, false, 3, 0.21f, 160, 1.6f);
                    bossShotTimer += 1.12f;
                    break;
                case BossPattern.Nova:
                    // Three bullets fit in the absorption field. At default player height,
                    // six volleys (18 bullets) can arrive before the finale ends.
                    for (int i = -1; i <= 1; i++)
                        AddEnemyBullet(boss, new(BossFinaleLaneX + i * 18, boss.Position.Y + 35),
                            Vector2.UnitY * 245, AttackKind.Nova, true);
                    if (boss.ShotsFired % 2 == 0)
                    {
                        AddEnemyBullet(boss, boss.Position + new Vector2(-65, 20), DownDirection(-0.43f) * 245, AttackKind.Nova, false);
                        AddEnemyBullet(boss, boss.Position + new Vector2(65, 20), DownDirection(0.43f) * 245, AttackKind.Nova, false);
                    }
                    bossShotTimer += 0.55f;
                    break;
            }
            boss.ShotsFired++;
        }

        if (bossSideTimer <= 0)
        {
            if (BossAttack == BossPattern.Fan)
                FireAimedSpread(boss, AttackKind.Pulse, false, 1, 0, 205);
            else if (BossAttack == BossPattern.Seeker)
                FireDownwardSpread(boss, AttackKind.Pulse, true, 3, 0.34f, 174);
            else
            {
                // Outward red fans leave the central learning lane open during NOVA.
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < 3; i++)
                        AddEnemyBullet(boss, boss.Position + new Vector2(side * 60, 10),
                            DownDirection(side * (0.55f + i * 0.14f)) * 186, AttackKind.Fan, false);
            }
            bossSideTimer += BossAttack == BossPattern.Nova ? 1.25f : 1.6f;
        }
        boss.ShotTimer = bossShotTimer;
    }

    private void FireAimedSpread(Enemy source, AttackKind kind, bool blue, int count,
        float spacing, float speed, float blueAfter = 0)
    {
        Vector2 target = PlayerPosition;
        target.Y = Math.Max(target.Y, source.Position.Y + 120);
        Vector2 aim = target - source.Position;
        float direction = MathF.Atan2(aim.X, aim.Y);
        FireDownwardSpread(source, kind, blue, count, spacing, speed, direction, blueAfter);
    }

    private void FireDownwardSpread(Enemy source, AttackKind kind, bool blue, int count,
        float spacing, float speed, float direction = 0, float blueAfter = 0)
    {
        for (int i = 0; i < count; i++)
            AddEnemyBullet(source, source.Position + new Vector2(0, source.Radius * 0.6f),
                DownDirection(direction + (i - (count - 1) / 2f) * spacing) * speed,
                kind, blue, blueAfter);
    }

    private void AddEnemyBullet(Enemy source, Vector2 position, Vector2 velocity,
        AttackKind kind, bool blue, float blueAfter = 0)
    {
        Bullets.Add(new(position, velocity, true, kind, blue, source) { BlueAfter = blueAfter });
    }

    private static Vector2 DownDirection(float angle) => new(MathF.Sin(angle), MathF.Cos(angle));
}

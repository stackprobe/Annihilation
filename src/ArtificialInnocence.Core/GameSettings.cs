namespace ArtificialInnocence.Core;

public static class GameSettings
{
    public const int Width = 1040;
    public const int Height = 840;
    public const int FieldWidth = 700;
    public const float FixedStep = 1f / 120f;
    public const float PlayerSpeed = 320f;
    public const float FocusSpeed = 145f;
    public const float PlayerRadius = 4f;
    public const float FieldRadius = 54f;
    public const float ShotInterval = 0.13f;
    public const float BulletSpeed = 680f;
    public const float InvincibleSeconds = 2f;
    public const int InitialLives = 3;
    public const int InitialBombs = 3;
    public const float BombDuration = 3f;
    public const float BombRadius = 260f;
    public const float StageDuration = 60f;
    public const float BossCycleDuration = 22f;

    public static int LearnThreshold(AttackKind kind) => kind switch
    {
        AttackKind.Pulse => 10, AttackKind.Fan => 8, AttackKind.Seeker => 6, AttackKind.Nova => 16, _ => 0
    };
    public static float LearnedInterval(AttackKind kind) => kind switch
    {
        AttackKind.Fan => 0.3f, AttackKind.Seeker => 0.24f, AttackKind.Nova => 0.20f, _ => 0.12f
    };
    public static float CounterDamage(AttackKind kind) => kind switch
    {
        AttackKind.Nova => 22, AttackKind.Seeker => 8, AttackKind.Fan => 5, _ => 4
    };
    public static string AttackName(AttackKind kind) => kind switch
    {
        AttackKind.Pulse => "PULSE", AttackKind.Fan => "FAN", AttackKind.Seeker => "SEEKER", AttackKind.Nova => "NOVA", _ => "NORMAL"
    };
}

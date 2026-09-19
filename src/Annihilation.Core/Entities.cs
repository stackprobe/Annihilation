using System.Numerics;

namespace Annihilation.Core;

public enum GameState { Title, Playing, Paused, GameOver, StageClear }
public enum EnemyKind { Scout, Striker, Hunter, Prism, Boss }
public enum AttackKind { Basic, Pulse, Fan, Seeker, Nova }
public enum ShotMode { Normal, Learned, Combined }
public enum StagePhase { Approach, Pursuit, Gate, Boss }
public enum BossPattern { Pulse, Fan, Seeker, Nova }

// SwitchShot and Bomb are press edges; Field and Shooting are held actions.
public readonly record struct PlayerInput(Vector2 Movement, bool Shooting, bool Focus,
	bool Field = false, bool SwitchShot = false, bool Bomb = false);

public sealed class Bullet(Vector2 position, Vector2 velocity, bool hostile,
	AttackKind kind = AttackKind.Basic, bool absorbable = false, Enemy? source = null)
{
	public Vector2 Position = position;
	public Vector2 PreviousPosition = position;
	public Vector2 Velocity = velocity;
	public bool Hostile = hostile;
	public AttackKind Kind = kind;
	public bool Absorbable = absorbable;
	public Enemy? Source = source;
	public bool Alive = true;
	public float Age;
	// A positive value changes a red seeker to blue after it has been evaded.
	public float BlueAfter;
	// NOVA's payoff is its powerful counter, rather than passive direct-shot damage.
	public float Damage = 1;
	public float Radius => Kind switch { AttackKind.Nova => 9, AttackKind.Seeker => 7, AttackKind.Fan => 6, _ => Hostile ? 5 : 4 };
}

public sealed class Enemy(Vector2 position, EnemyKind kind, float speed)
{
	public Vector2 Position = position;
	public Vector2 Origin = position;
	public EnemyKind Kind = kind;
	public float Speed = speed;
	public float Age;
	public float ShotTimer = 1.2f;
	public float Health = HealthFor(kind);
	public float MaxHealth = HealthFor(kind);
	public bool Alive = true;
	public bool BluePhase;
	public int ShotsFired;
	public float HitFlash;
	public float Radius => Kind switch { EnemyKind.Boss => 54, EnemyKind.Prism => 25, EnemyKind.Striker => 23, _ => 18 };
	public int Points => Kind switch { EnemyKind.Boss => 15000, EnemyKind.Prism => 650, EnemyKind.Hunter => 400, EnemyKind.Striker => 300, _ => 150 };
	private static float HealthFor(EnemyKind kind) => kind switch
	{
		EnemyKind.Boss => 1350,
		EnemyKind.Prism => 30,
		EnemyKind.Hunter => 12,
		EnemyKind.Striker => 9,
		EnemyKind.Scout => 6,
		_ => 6
	};
}

public sealed class Particle(Vector2 position, Vector2 velocity, bool cyan)
{
	public Vector2 Position = position;
	public Vector2 Velocity = velocity;
	public float Life = 0.45f;
	public bool Cyan = cyan;
}

// A visible counter beam links the annihilation point to the firing enemy.
public sealed class CounterEffect(Vector2 position, Vector2 target, AttackKind kind, bool bomb)
{
	public Vector2 Position = position;
	public Vector2 Target = target;
	public AttackKind Kind = kind;
	public bool Bomb = bomb;
	public float Life = 0.36f;
}

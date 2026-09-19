using System.Numerics;
using Raylib_cs;
using Annihilation.Core;
using Annihilation.Game;

bool smokeBoss = args.Contains("--smoke-boss");
bool smokeTitle = args.Contains("--smoke-title");
bool smokeTest = smokeBoss || smokeTitle || args.Contains("--smoke-test");
Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | (smokeTest ? ConfigFlags.HiddenWindow : 0));
Raylib.InitWindow(GameSettings.Width, GameSettings.Height, "Annihilation | COUNTERLEARN");
Raylib.SetTargetFPS(smokeTest ? 0 : 120);
int result = 0;
try
{
	var world = new GameWorld();
	var renderer = new GameRenderer();
	using var audio = new GameAudio(enabled: !smokeTest);
	if (smokeTest && !smokeTitle) world.Start();
	float accumulator = 0;
	bool pendingSwitch = false, pendingBomb = false;
	int frames = 0;
	while (!Raylib.WindowShouldClose())
	{
		float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
		if (Raylib.IsKeyPressed(KeyboardKey.M)) audio.ToggleMute();
		bool terminal = world.State is GameState.GameOver or GameState.StageClear;
		bool start = Raylib.IsKeyPressed(KeyboardKey.Enter) && (terminal || world.State == GameState.Title);
		if (start || Raylib.IsKeyPressed(KeyboardKey.R) && terminal)
		{
			world.Start();
			accumulator = 0;
			pendingSwitch = pendingBomb = false;
		}
		if (Raylib.IsKeyPressed(KeyboardKey.P))
		{
			world.TogglePause();
			accumulator = 0;
			pendingSwitch = pendingBomb = false;
		}
		if (!smokeTest && !Raylib.IsWindowFocused() && world.State == GameState.Playing)
		{
			world.TogglePause();
			accumulator = 0;
			pendingSwitch = pendingBomb = false;
		}

		float x = (Down(KeyboardKey.Right, KeyboardKey.D) ? 1 : 0) - (Down(KeyboardKey.Left, KeyboardKey.A) ? 1 : 0);
		float y = (Down(KeyboardKey.Down, KeyboardKey.S) ? 1 : 0) - (Down(KeyboardKey.Up, KeyboardKey.W) ? 1 : 0);
		var input = new PlayerInput(new Vector2(x, y), Down(KeyboardKey.Space, KeyboardKey.Z),
			Raylib.IsKeyDown(KeyboardKey.LeftShift), Raylib.IsKeyDown(KeyboardKey.X) || Raylib.IsMouseButtonDown(MouseButton.Right));
		if (world.State == GameState.Playing)
		{
			// Keep a press until a simulation tick consumes it; never repeat it across catch-up ticks.
			pendingSwitch |= Raylib.IsKeyPressed(KeyboardKey.C);
			pendingBomb |= Raylib.IsKeyPressed(KeyboardKey.V);
			accumulator += smokeTest ? GameSettings.FixedStep * 8 : dt;
			while (accumulator >= GameSettings.FixedStep)
			{
				var tickInput = smokeTest ? SmokePilot.Input(world) : input with { SwitchShot = pendingSwitch, Bomb = pendingBomb };
				world.Update(GameSettings.FixedStep, tickInput);
				pendingSwitch = pendingBomb = false;
				accumulator -= GameSettings.FixedStep;
				if (world.State != GameState.Playing) { accumulator = 0; break; }
			}
		}
		else
		{
			accumulator = 0;
			pendingSwitch = pendingBomb = false;
		}
		audio.Update(world, dt);
		Raylib.BeginDrawing();
		renderer.Draw(world, smokeTest || input.Focus);
		Raylib.EndDrawing();
		frames++;
		bool capture = smokeTitle ? frames >= 3 :
			world.Elapsed >= (smokeBoss ? 101 : 14) ||
			smokeBoss && world.Elapsed >= 100 && world.CounterEffects.Any(e => e.Kind == AttackKind.Nova) ||
			world.State != GameState.Playing || frames >= 2400;
		if (smokeTest && capture)
		{
			Directory.CreateDirectory("tmp");
			string relativePath = "tmp/" + (smokeTitle ? "smoke-title.png" : smokeBoss ? "smoke-boss.png" : "smoke-test.png");
			string path = Path.GetFullPath(relativePath);
			DateTime previousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
			Raylib.TakeScreenshot(relativePath);
			Console.WriteLine($"SMOKE state={world.State} time={world.Elapsed:F2} lives={world.Lives} learned={world.LearnedAttack} absorbed={world.TotalAbsorbed} counters={world.AnnihilationCount} image={path}");
			if (!smokeTitle && (world.State != GameState.Playing || smokeBoss && world.Boss is null)) result = 1;
			if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) == previousWrite) result = 2;
			break;
		}
	}
}
finally
{
	Raylib.CloseWindow();
}
return result;

static bool Down(KeyboardKey first, KeyboardKey second) => Raylib.IsKeyDown(first) || Raylib.IsKeyDown(second);

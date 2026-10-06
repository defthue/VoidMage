using System.Linq;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Boss;

/// <summary>
/// The boss test scene's driver (scenes/boss_test.scene): no village, no run, no cards. On play, the host takes
/// the stage standing in the scene (<see cref="Stage"/>, or any root object named `hell_arena` / `stage`; a
/// prefab instance dropped into the scene counts) as the arena, or clones the run's stage prefab here when the
/// scene has none; spawns the boss dormant at the stage's markers; bakes the arena navmesh; spawns the player at
/// the `spawn` marker; and wakes the boss after <see cref="WakeDelay"/>. Stop and play again to reset. The stage
/// itself is edited like any level: lay it out in this scene (then save it as a prefab for the run and point
/// `boss_arena_prefab` at it), or edit the prefab and keep an instance of it here to see it.
/// </summary>
[Title( "Boss Test Stage" ), Category( "Void Mage" ), Icon( "sports_martial_arts" )]
public sealed class BossTestStage : Component
{
	/// <summary>The stage root in this scene. Empty: a root named `hell_arena` or `stage` is looked for, else the run's prefab is cloned at this object.</summary>
	[Property] public GameObject Stage { get; set; }
	/// <summary>The player prefab (prefabs/player.prefab when empty).</summary>
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public bool WakeOnStart { get; set; } = true;
	[Property] public float WakeDelay { get; set; } = 3f;
	/// <summary>Swap the stage's materials for the hell variant (the demo stage's look); off for a stage with its own materials.</summary>
	[Property] public bool HellMaterials { get; set; } = true;

	private GameObject _player;

	protected override void OnStart()
	{
		if ( !Game.IsPlaying || Scene.IsEditor || !Networking.IsHost ) return;
		HellArena.HellMaterials = HellMaterials;
		var root = Stage.IsValid() ? Stage
			: Scene.Children.FirstOrDefault( c => c.IsValid() && c != GameObject && c.Name is "hell_arena" or "stage" );
		if ( root.IsValid() ) HellArena.Adopt( Scene, root );
		else HellArena.Build( Scene, WorldPosition );
		if ( !HellArena.Built ) { Log.Error( "[BossTest] no stage" ); return; }
		HellArena.SpawnBossHost( Scene );
		_ = HellArena.BakeNavMeshAsync( Scene );
		SpawnPlayer();
		_ = Drive();
	}

	private void SpawnPlayer()
	{
		var look = (HellArena.SkullMarker.Position - HellArena.Spawn.Position).WithZ( 0f );
		var at = new Transform( HellArena.Spawn.Position, look.IsNearZeroLength ? Rotation.Identity : Rotation.LookAt( look.Normal ) );
		_player = PlayerPrefab.IsValid()
			? PlayerPrefab.Clone( new CloneConfig { Transform = at, StartEnabled = true, Name = "Player" } )
			: GameObject.Clone( Run.RunManager.DefaultPlayerPrefab, new CloneConfig { Transform = at, StartEnabled = true, Name = "Player" } );
		if ( !_player.IsValid() ) { Log.Error( "[BossTest] the player prefab did not clone" ); return; }
		_player.Flags |= GameObjectFlags.NotSaved;
		_player.Components.GetOrCreate<PlayerShards>();
		_player.Components.GetOrCreate<Boons.PlayerBoons>();
		Inventory.PlayerInventory.EnsureOn( _player );
		PotionDrinker.EnsureOn( _player );
		_player.Components.Get<PlayerPotions>()?.LoadSaved();
		CharacterAppearance.ApplySaved( _player );
	}

	private async System.Threading.Tasks.Task Drive()
	{
		// the player's components start next frame: then it can be faced at the skull
		await GameTask.DelaySeconds( 0.1f );
		if ( !Scene.IsValid() || !HellArena.Built ) return;
		HellArena.TeleportLocalPlayer();
		if ( !WakeOnStart ) return;
		await GameTask.DelaySeconds( WakeDelay );
		if ( !Scene.IsValid() ) return;
		BossBrain.Current?.Wake();
	}

	protected override void OnDestroy()
	{
		if ( HellArena.Built ) HellArena.Clear();
	}
}

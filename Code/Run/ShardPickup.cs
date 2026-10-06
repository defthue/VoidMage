using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Run;

/// <summary>
/// One void shard lying where something died or broke: a small crystal with a purple glow that bobs, then flies
/// to the player once they come near and is counted on touch. No physics: it is dropped a little above the floor
/// and settles by a short fall of its own, so nothing here can get stuck in a prop or fall through a stair.
/// Parented under the dungeon builder so a floor change sweeps whatever was left behind.
///
/// Solo for now: the host drops these and only the local player collects them; nothing is networked (the run
/// loop itself is single-player first, per the 2026-09-13 decisions).
/// </summary>
[Title( "Shard Pickup" ), Category( "Void Mage" ), Icon( "diamond" )]
public sealed class ShardPickup : Component
{
	public const string ModelPath = "darkfortress/sm_prop_crystal_01.vmdl";

	/// <summary>Distance at which a shard starts flying to the player.</summary>
	[ConVar( "shard_magnet" )] public static float MagnetRange { get; set; } = 260f;
	/// <summary>Distance at which it counts as picked up.</summary>
	[ConVar( "shard_collect" )] public static float CollectRange { get; set; } = 30f;
	/// <summary>Seconds a shard lies around before it expires (a floor's leftovers are swept anyway).</summary>
	[ConVar( "shard_life" )] public static float Lifetime { get; set; } = 90f;
	/// <summary>Sound played on pickup, empty for none.</summary>
	[ConVar( "shard_sound" )] public static string PickupSound { get; set; } = VoidMage.Audio.GameAudio.ReceiveItemSound;

	private const float ModelScale = 0.22f;
	private const float SettleSpeed = 220f;
	private const float MagnetDelay = 0.4f;

	/// <summary>Every shard still lying about (the minimap marks them).</summary>
	public static readonly List<ShardPickup> All = new();

	private float _bornAt;
	private float _phase;
	private float _restZ;
	private float _speed;
	private bool _settled;

	/// <summary>Host: `count` shards scattered a hand's width around `at`, under `parent` (the dungeon builder).</summary>
	public static void Drop( Scene scene, Vector3 at, int count, GameObject parent )
	{
		if ( !Networking.IsHost || count <= 0 || !scene.IsValid() ) return;
		count = Math.Min( count, 16 );   // a knight with Hoarder is 16
		for ( int i = 0; i < count; i++ )
		{
			var jitter = count == 1 ? Vector3.Zero : new Vector3( Game.Random.Float( -22f, 22f ), Game.Random.Float( -22f, 22f ), Game.Random.Float( 0f, 14f ) );
			var go = new GameObject( true, "shard" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			if ( parent.IsValid() ) go.SetParent( parent, false );
			go.WorldPosition = at + jitter;
			go.WorldRotation = Rotation.FromYaw( Game.Random.Float( 0f, 360f ) );
			go.Components.Create<ShardPickup>();
		}
	}

	/// <summary>
	/// Chance a broken crate, barrel or vase drops shards, and how many. It was a 20% chance of ONE, which read as
	/// crates never dropping anything (user, 2026-09-19: the floors felt empty); now most of the furniture pays out.
	/// </summary>
	[ConVar( "shard_break_chance" )] public static float BreakChance { get; set; } = 0.65f;
	[ConVar( "shard_break_min" )] public static int BreakMin { get; set; } = 1;
	[ConVar( "shard_break_max" )] public static int BreakMax { get; set; } = 2;

	/// <summary>
	/// How often fodder (a plain skeleton, a monster) drops its shards, and how many. It was every kill; the enemy
	/// count then tripled and the income with it (a full run paid over 2000 against a purse of 999), so 0.34 brought a
	/// full clear back to roughly 700-900. Raised again 2026-09-22 (user: "make monsters drop more shards"; the purse
	/// caps at 9999 now and shards survive a death): three kills in five pay 1-3, and every tier above pays half again.
	/// </summary>
	[ConVar( "shard_fodder_chance" )] public static float FodderChance { get; set; } = 0.6f;
	[ConVar( "shard_fodder_min" )] public static int FodderMin { get; set; } = 1;
	[ConVar( "shard_fodder_max" )] public static int FodderMax { get; set; } = 3;

	/// <summary>
	/// What a kill is worth, by the spawner's kind name (DungeonBuilder's tables): fodder 1-3 three kills in five,
	/// light armor and the soul 5, heavy armor 6, the swordsman 8, the knight 12; critters (frogs) nothing. Rolled on
	/// the host. (Before 2026-09-22: 1-2 one kill in three, 3, 4, 5, 8.)
	/// </summary>
	public static int CountFor( string kind )
	{
		if ( string.IsNullOrEmpty( kind ) ) return 1;
		if ( kind == "undead_knight" ) return 12;
		if ( Array.IndexOf( Dungeon.DungeonBuilder.SwordsmanKinds, kind ) >= 0 ) return 8;
		if ( Array.IndexOf( Dungeon.DungeonBuilder.HeavyArmorKinds, kind ) >= 0 ) return 6;
		if ( kind == "tormented_soul" || Array.IndexOf( Dungeon.DungeonBuilder.LightArmorKinds, kind ) >= 0 ) return 5;
		if ( kind.StartsWith( "monster_", StringComparison.Ordinal ) )
		{
			var entry = Npc.MonsterKinds.Find( kind.Substring( "monster_".Length ) );
			if ( entry is { Critter: true } ) return 0;
		}
		int lo = Math.Max( 0, Math.Min( FodderMin, FodderMax ) ), hi = Math.Max( FodderMin, FodderMax );
		return Game.Random.Float() < FodderChance ? Game.Random.Int( lo, hi ) : 0;
	}

	protected override void OnStart()
	{
		_bornAt = Time.Now;
		_phase = Game.Random.Float( 0f, 6.28f );
		var renderer = Components.Create<ModelRenderer>();
		renderer.Model = Model.Load( ModelPath );
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		renderer.Tint = new Color( 0.75f, 0.55f, 1f );
		LocalScale = new Vector3( ModelScale );
		// a faint purple breath on the floor around it, not a lamp (user 2026-09-14)
		var light = Components.Create<PointLight>();
		light.LightColor = new Color( 0.62f, 0.32f, 1f ) * 0.3f;
		light.Radius = 24f;
		light.Shadows = false;
		// where the floor is: a short trace down, so the drop point (the body's chest, a crate's centre) settles onto it
		var tr = Scene.Trace.Ray( WorldPosition + Vector3.Up * 10f, WorldPosition - Vector3.Up * 400f ).WithoutTags( "player", "enemy", "npc", "ragdoll", "debris", "trigger" ).Run();
		_restZ = tr.Hit ? tr.HitPosition.z + 14f : WorldPosition.z;
		if ( !All.Contains( this ) ) All.Add( this );
	}

	protected override void OnDestroy() => All.Remove( this );

	protected override void OnUpdate()
	{
		float age = Time.Now - _bornAt;
		if ( age > Lifetime ) { GameObject.Destroy(); return; }

		var pos = WorldPosition;
		var player = PlayerHealth.Local;
		var target = player.IsValid() && !player.IsDead ? player.FeetPosition + Vector3.Up * 40f : (Vector3?)null;

		var magnet = MagnetRange * VoidMage.Boons.PlayerBoons.LocalScale( VoidMage.Boons.BoonStat.PickupRange );
		if ( target.HasValue && age > MagnetDelay && pos.Distance( target.Value ) < magnet )
		{
			// fly to the chest, faster the longer the pull lasts
			_speed = MathF.Min( 1400f, _speed + Time.Delta * 2600f );
			var dir = (target.Value - pos).Normal;
			pos += dir * _speed * Time.Delta;
			WorldPosition = pos;
			if ( pos.Distance( target.Value ) < CollectRange ) { Collect(); return; }
			WorldRotation *= Rotation.FromYaw( Time.Delta * 720f );
			return;
		}

		_speed = 0f;
		if ( !_settled )
		{
			pos.z = MathX.Approach( pos.z, _restZ, SettleSpeed * Time.Delta );
			if ( MathF.Abs( pos.z - _restZ ) < 0.5f ) _settled = true;
		}
		else pos.z = _restZ + MathF.Sin( Time.Now * 2.4f + _phase ) * 3f;
		WorldPosition = pos;
		WorldRotation *= Rotation.FromYaw( Time.Delta * 90f );
	}

	private void Collect()
	{
		PlayerShards.Local?.Add( 1 );
		if ( !string.IsNullOrEmpty( PickupSound ) ) VoidMage.Audio.GameAudio.Play( PickupSound, WorldPosition );
		GameObject.Destroy();
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Character;
using VoidMage.Dungeon;

namespace VoidMage.Npc;

/// <summary>
/// Puts skeletons in the world, host only: the dungeon's ambush rooms after a build, and a console command
/// that drops one in front of the local player for testing
/// (`skeleton_spawn [kind] [aggressive|defensive] [sword index] [shield index] [ally]`, kind one of slave, corpse,
/// df_skeleton, df_flesh, df_light, df_heavy, soldier_01, soldier_02, realms_01..03, undead_knight, spirit_demon,
/// dark_lord, tormented_soul, or a tier: `base`, `light`, `heavy`, `soldier` (any armored), `realms`, `df`, `knight`,
/// `swordsman` (either skin), `soul`).
/// Kind, brain, sword and shield are independent rolls: the slave has two brains x two swords, each soldier two
/// brains x two greatswords x two heater shields, each Realms skeleton the soldier brains x two FantasyKingdom
/// swords x the gravestone shield. The Fortress corpse is a slave with no shield (corpse_* brains never guard) and
/// the Dark Fantasy skins copy the kind of their tier (DungeonBuilder.BaseSkeletonKinds / LightArmorKinds / HeavyArmorKinds).
/// Skeletons are parented under the dungeon like the player, so the same trace rules apply to them.
/// </summary>
public static class SkeletonSpawner
{
	/// <summary>
	/// One NPC kind: its prefab, the prefix of its brains (`soldier_aggressive`) and how many weapon choices its
	/// vmdl has (tools/skeleton_kinds.py). `Brain`, when set, names ONE brain outright instead of rolling among a
	/// prefix's: every Quaternius monster has exactly one personality, not an aggressive/defensive pair.
	/// </summary>
	public sealed record Kind( string Name, string Prefab, string BrainPrefix, int Swords, int Shields, string Brain = "" );

	public static readonly Kind[] Kinds = BuildKinds();

	private static Kind[] BuildKinds()
	{
		var kinds = new List<Kind>
		{
			new( "slave", "prefabs/skeleton.prefab", "", 2, 1 ),
			// the light armor tier (soldier stats) and the heavy one (soldier_02 + the Dark Fantasy heavy: DamageScale 1.7,
			// GuardPoints 4, heavy_* brains)
			new( "soldier_01", "prefabs/skeleton_soldier_01.prefab", "soldier_", 2, 2 ),
			new( "soldier_02", "prefabs/skeleton_soldier_02.prefab", "heavy_", 2, 2 ),
			// the Dark Fortress corpse: a slave without a shield (corpse_* brains roll no guard and no bash), and it bleeds
			new( "corpse", "prefabs/skeleton_corpse.prefab", "corpse_", 2, 0 ),
			// the Dark Fantasy skins (tools/skeleton_kinds.py df_*): two slave variants, one per armor tier
			new( "df_skeleton", "prefabs/skeleton_df_skeleton.prefab", "", 2, 1 ),
			new( "df_flesh", "prefabs/skeleton_df_flesh.prefab", "", 2, 1 ),
			new( "df_light", "prefabs/skeleton_df_light.prefab", "soldier_", 2, 2 ),
			new( "df_heavy", "prefabs/skeleton_df_heavy.prefab", "heavy_", 2, 2 ),
			// the flying ghost: no weapons, its own graph and brains (soul_*), the same host brain with hover and code lunges
			new( "tormented_soul", "prefabs/tormented_soul.prefab", "soul_", 0, 0 ),
			// the Dungeon Realms skeletons (forge-hold skins, tools/skeleton_kinds.py realms_0N): soldier stats and
			// brains, FantasyKingdom swords 03 / 04, the gravestone shield
			new( "realms_01", "prefabs/skeleton_realms_01.prefab", "soldier_", 2, 1 ),
			new( "realms_02", "prefabs/skeleton_realms_02.prefab", "soldier_", 2, 1 ),
			new( "realms_03", "prefabs/skeleton_realms_03.prefab", "soldier_", 2, 1 ),
			// the forge-hold's big one: heden's boss grammar as a strong enemy (attack table, posture, the shield read),
			// one brain (knight.brain), one sword and one shield (tools/gen_knight_prefab.py)
			new( "undead_knight", "prefabs/undead_knight.prefab", "knight_", 1, 1, "knight" ),
			// the swordsman, two skins of one enemy on Synty's Sword Animations (tools/gen_swordsman_prefab.py): its own
			// attack table, one brain (swordsman.brain), one sword, no shield; comes alone or in twos (DungeonBuilder.SwordsmanChance)
			new( "spirit_demon", "prefabs/spirit_demon.prefab", "swordsman_", 1, 0, "swordsman" ),
			new( "dark_lord", "prefabs/dark_lord.prefab", "swordsman_", 1, 0, "swordsman" ),
		};
		// the Quaternius monsters (MonsterKinds.Generated.cs from tools/monster_kinds.py): same components, one
		// brain each, no weapons. Their kind names are prefixed so `monster_bat` can never collide with a skeleton.
		foreach ( var m in MonsterKinds.All )
			kinds.Add( new( "monster_" + m.Name, m.Prefab, "monster_", 0, 0, m.Brain ) );
		return kinds.ToArray();
	}

	public static Kind Slave => Kinds[0];

	[ConVar( "skeleton_navdraw" )]
	public static bool NavDraw { get; set; }

	/// <summary>Spawn one; a negative sword/shield index rolls it, an out-of-range one wraps. `brain` is aggressive/defensive (the kind's prefix is added).</summary>
	/// <summary>How much tougher each floor past the first is (0.1 = +10% health a floor).</summary>
	[ConVar( "npc_floor_health" )] public static float FloorHealthStep { get; set; } = 0.12f;
	/// <summary>How much harder each floor past the first hits (0.06 = +6% damage a floor).</summary>
	[ConVar( "npc_floor_damage" )] public static float FloorDamageStep { get; set; } = 0.06f;
	/// <summary>Every hostile body on every floor: health and damage multipliers on top of the per-floor ramp (user, 2026-09-24: "increase all enemy damage and hp by 10%"). The boss has its own vitals (boss_hp_*) and is not touched.</summary>
	[ConVar( "npc_health_scale" )] public static float BaseHealthScale { get; set; } = 1.1f;
	[ConVar( "npc_damage_scale" )] public static float BaseDamageScale { get; set; } = 1.1f;
	/// <summary>What every body spawned from now on multiplies its health and its damage by: set by the dungeon builder from its depth.</summary>
	public static float FloorHealthScale { get; set; } = 1f;
	public static float FloorDamageScale { get; set; } = 1f;
	public static void SetFloor( int depth )
	{
		FloorHealthScale = BaseHealthScale * (1f + MathF.Max( 0f, FloorHealthStep ) * Math.Max( 0, depth - 1 ));
		FloorDamageScale = BaseDamageScale * (1f + MathF.Max( 0f, FloorDamageStep ) * Math.Max( 0, depth - 1 ));
	}

	public static GameObject Spawn( Scene scene, Vector3 worldPosition, Rotation rotation, GameObject parent, string kindName, string brain, int sword, int shield, bool ambush )
		=> Spawn( scene, worldPosition, rotation, parent, kindName, brain, sword, shield, ambush, VoidMage.Combat.Team.Hostile, null, 0 );

	/// <summary>The full form: `team` and, for The Turncoat, who raised it and with how many stacks (set before the spawn snapshot goes out).</summary>
	public static GameObject Spawn( Scene scene, Vector3 worldPosition, Rotation rotation, GameObject parent, string kindName, string brain, int sword, int shield, bool ambush, VoidMage.Combat.Team team, GameObject summoner, int raisedStacks )
	{
		if ( !Networking.IsHost ) return null;
		var kind = FindKind( kindName );
		var profile = FindProfile( kind, brain );
		sword = sword < 0 ? Game.Random.Next( kind.Swords ) : sword % Math.Max( 1, kind.Swords );
		shield = shield < 0 ? Game.Random.Next( kind.Shields ) : shield % Math.Max( 1, kind.Shields );
		// CloneConfig.Transform is applied as the clone's LOCAL transform and CloneConfig.Parent is then set WITHOUT
		// keeping the world position (GameObject.Clone -> InitClone -> Parent setter, checked in the engine 2026-09-13),
		// so a parent given here would land the body at parent + world. The test scene never showed it (its builder
		// stands at the origin); the run's builder is 40k units out and every skeleton spawned a floor's width past
		// the floor, dormant forever. Clone at the scene root, then reparent keeping the world position.
		var go = GameObject.Clone( kind.Prefab, new CloneConfig
		{
			Transform = new Transform( worldPosition, rotation ),
			StartEnabled = true,
			Name = $"skeleton {kind.Name} ({profile?.ResourceName ?? "default"}, sword {sword}, shield {shield})",
		} );
		if ( !go.IsValid() ) { Log.Error( $"[Skeleton] prefab {kind.Prefab} did not clone" ); return null; }
		if ( parent.IsValid() ) go.SetParent( parent, true );
		go.Flags |= GameObjectFlags.NotSaved;
		if ( !go.Tags.Has( "npc" ) || !go.Tags.Has( "enemy" ) )
		{
			// prefab tags are comma separated ("enemy,npc"); a bad string is rejected whole and the skeleton
			// then hits its own capsule in every line-of-sight trace and never wakes up
			Log.Error( $"[Skeleton] prefab tags are '{go.Tags}', expected enemy and npc; adding them" );
			go.Tags.Add( "enemy" );
			go.Tags.Add( "npc" );
		}
		if ( team == VoidMage.Combat.Team.Ally )
		{
			// a raised minion: the player's blade and bolts pass over `ally`, hostiles hunt it
			go.Tags.Remove( "enemy" );
			go.Tags.Add( "ally" );
			go.Name = "ally " + go.Name;
		}
		var npc = go.Components.Get<SkeletonNpc>();
		if ( npc.IsValid() )
		{
			npc.Profile = profile;
			npc.SetAmbush( ambush && team == VoidMage.Combat.Team.Hostile );
			npc.Configure( profile, Game.Random.Next(), sword, shield );   // rolled before the spawn snapshot goes out
			npc.Team = team;
			if ( team == VoidMage.Combat.Team.Ally )
			{
				npc.Raised = true;
				npc.RaisedUntil = Time.Now + VoidMage.Spells.SpellHostTuning.TurncoatSeconds;
				npc.Summoner = summoner;
				npc.RaisedStacks = raisedStacks;
			}
		}
		// a critter (the frog) is catchable with E for as long as it is alive: the component is added here rather
		// than baked into the prefab, because the prefabs are generated from tools/monster_kinds.py
		if ( MonsterKinds.Find( kind.Name )?.Critter == true && !go.Components.Get<CritterPickup>( FindMode.EverythingInSelf ).IsValid() )
			go.Components.Create<CritterPickup>();
		go.NetworkSpawn( null );
		return go;
	}

	/// <summary>The tier / family aliases the spawn command accepts besides the kind names.</summary>
	public static readonly string[] Aliases = { "base", "light", "heavy", "soldier", "realms", "df", "knight", "swordsman", "soul", "ghost", "random", "any", "monster" };

	/// <summary>By name, by tier (`base`, `light`, `heavy`: DungeonBuilder's arrays; `soldier` = any armored) or by family (`realms`, `df`), else the slave.</summary>
	public static Kind FindKind( string name )
	{
		var n = (name ?? "").Trim().ToLowerInvariant();
		if ( n == "base" ) return OneOf( DungeonBuilder.BaseSkeletonKinds );
		if ( n == "light" ) return OneOf( DungeonBuilder.LightArmorKinds );
		if ( n == "heavy" ) return OneOf( DungeonBuilder.HeavyArmorKinds );
		if ( n == "soldier" ) return OneOf( DungeonBuilder.LightArmorKinds.Concat( DungeonBuilder.HeavyArmorKinds ).ToArray() );
		if ( n == "realms" ) return OneOf( "realms_01", "realms_02", "realms_03" );
		if ( n == "df" ) return OneOf( "df_skeleton", "df_flesh", "df_light", "df_heavy" );
		if ( n == "knight" ) n = "undead_knight";
		if ( n == "swordsman" ) return OneOf( DungeonBuilder.SwordsmanKinds );
		if ( n is "soul" or "ghost" ) return Kinds.First( k => k.Name == "tormented_soul" );
		if ( n == "random" || n == "any" ) return Kinds[Game.Random.Next( Kinds.Length )];
		// a monster answers to its bare name too (`bat`), and `monster` picks any hostile one
		if ( n == "monster" )
		{
			var hostile = MonsterKinds.All.Where( m => !m.Critter ).ToList();
			return FindKind( "monster_" + hostile[Game.Random.Next( hostile.Count )].Name );
		}
		return Kinds.FirstOrDefault( k => k.Name == n )
			?? Kinds.FirstOrDefault( k => k.Name == "monster_" + n )
			?? Slave;
	}

	/// <summary>A random one of the named kinds (by name, never by table index: the table grows).</summary>
	private static Kind OneOf( params string[] names )
	{
		var pick = names[Game.Random.Next( names.Length )];
		return Kinds.FirstOrDefault( k => k.Name == pick ) ?? Slave;
	}

	/// <summary>
	/// The kind's brain of that name (`soldier_` + aggressive), a bare match as a fallback, else a random one of the
	/// kind's own brains (a slave never runs on a soldier brain and vice versa).
	/// </summary>
	public static SkeletonBrainProfile FindProfile( Kind kind, string name )
	{
		var all = ResourceLibrary.GetAll<SkeletonBrainProfile>().ToList();
		if ( all.Count == 0 ) return null;
		// a kind with exactly one brain takes it and nothing else: "monster_snake" must never pick up
		// "monster_snake_angry" from a prefix match, and no monster may ever roll a skeleton's brain
		if ( !string.IsNullOrEmpty( kind.Brain ) )
			return all.FirstOrDefault( p => string.Equals( p.ResourceName, kind.Brain, StringComparison.OrdinalIgnoreCase ) );
		if ( !string.IsNullOrWhiteSpace( name ) )
		{
			// a prefixed kind never falls back to the bare name: "aggressive" on a soul must not load the slave's brain
			// (a full resource name, "soldier_aggressive", is accepted as-is: a raised minion keeps its victim's brain)
			var match = all.FirstOrDefault( p => string.Equals( p.ResourceName, kind.BrainPrefix + name, StringComparison.OrdinalIgnoreCase ) )
				?? (kind.BrainPrefix.Length > 0 && name.StartsWith( kind.BrainPrefix, StringComparison.OrdinalIgnoreCase ) ? all.FirstOrDefault( p => string.Equals( p.ResourceName, name, StringComparison.OrdinalIgnoreCase ) ) : null)
				?? (kind.BrainPrefix.Length == 0 ? all.FirstOrDefault( p => string.Equals( p.ResourceName, name, StringComparison.OrdinalIgnoreCase ) ) : null);
			if ( match is not null ) return match;
		}
		var own = all.Where( p => IsOwnBrain( p.ResourceName ) ).ToList();
		if ( own.Count == 0 ) own = all;
		return own[Game.Random.Next( own.Count )];

		bool IsOwnBrain( string resource )
		{
			if ( kind.BrainPrefix.Length > 0 ) return resource.StartsWith( kind.BrainPrefix, StringComparison.OrdinalIgnoreCase );
			return Kinds.All( k => k.BrainPrefix.Length == 0 || !resource.StartsWith( k.BrainPrefix, StringComparison.OrdinalIgnoreCase ) );
		}
	}

	/// <summary>Tokens in any order: a kind name, a brain name, then up to two integers (sword, shield). Missing = rolled.</summary>
	[ConCmd( "skeleton_spawn" )]
	public static void SpawnCommand( string a = "", string b = "", string c = "", string d = "" )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		var kind = ""; var brain = ""; var sword = -1; var shield = -1; var team = VoidMage.Combat.Team.Hostile;
		foreach ( var raw in new[] { a, b, c, d } )
		{
			var token = (raw ?? "").Trim().ToLowerInvariant();
			if ( token.Length == 0 ) continue;
			if ( token is "ally" or "friend" or "raised" ) { team = VoidMage.Combat.Team.Ally; continue; }
			if ( int.TryParse( token, out var index ) ) { if ( sword < 0 ) sword = index; else shield = index; }
			else if ( Aliases.Contains( token ) || Kinds.Any( k => k.Name == token || k.Name == "monster_" + token ) ) kind = token;
			else if ( token is "broken" or "greatsword" ) sword = 0;
			else if ( token is "sword" or "unbroken" or "whole" or "curved" ) sword = 1;
			else brain = token;
		}
		var player = scene.GetAllComponents<PlayerHealth>().FirstOrDefault( p => !p.IsProxy );
		var camera = scene.Camera;
		if ( !player.IsValid() || !camera.IsValid() ) { Log.Warning( "[Skeleton] no local player" ); return; }
		var forward = camera.WorldRotation.Forward.WithZ( 0f ).Normal;
		var pos = player.WorldPosition + forward * 300f;
		var nav = scene.NavMesh.GetClosestPoint( pos, 200f );
		if ( nav.HasValue ) pos = nav.Value;
		var dungeon = scene.GetAllComponents<DungeonBuilder>().FirstOrDefault();
		var go = Spawn( scene, pos, Rotation.LookAt( -forward ), dungeon.IsValid() ? dungeon.GameObject : null, kind, brain, sword, shield, false, team, team == VoidMage.Combat.Team.Ally ? player.GameObject : null, 2 );
		var ready = dungeon.IsValid() && dungeon.NavMeshReady;
		Log.Info( go.IsValid() ? $"[Skeleton] spawned {go.Name} at {pos} (navmesh {(nav.HasValue ? "hit" : "MISS")}, dungeon says {(ready ? "ready" : "NOT ready: straight-line steering")})" : "[Skeleton] spawn failed (host only)" );
	}

	/// <summary>
	/// `monster_spawn bat 3` — a whole group of one monster in front of the player, the way the dungeon places
	/// them. No argument spawns one of a random hostile kind; `monster_spawn frog` drops the ambient critter.
	/// </summary>
	[ConCmd( "monster_spawn" )]
	public static void SpawnMonsterCommand( string which = "monster", int count = 1 )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		var player = scene.GetAllComponents<PlayerHealth>().FirstOrDefault( p => !p.IsProxy );
		var camera = scene.Camera;
		if ( !player.IsValid() || !camera.IsValid() ) { Log.Warning( "[Monster] no local player" ); return; }
		var kind = FindKind( which );
		if ( !kind.Name.StartsWith( "monster_" ) )
		{
			Log.Warning( $"[Monster] no monster called '{which}'; known: {string.Join( ", ", MonsterKinds.All.Select( m => m.Name ) )}" );
			return;
		}
		var forward = camera.WorldRotation.Forward.WithZ( 0f ).Normal;
		var dungeon = scene.GetAllComponents<DungeonBuilder>().FirstOrDefault();
		var spawned = 0;
		for ( var i = 0; i < Math.Max( 1, count ); i++ )
		{
			// fan them out so a group does not start life inside itself
			var offset = Rotation.FromYaw( i * 360f / Math.Max( 1, count ) ).Forward * (i == 0 ? 0f : 60f);
			var pos = player.WorldPosition + forward * 250f + offset;
			var nav = scene.NavMesh.GetClosestPoint( pos, 200f );
			if ( nav.HasValue ) pos = nav.Value;
			var go = Spawn( scene, pos + Vector3.Up * 4f, Rotation.LookAt( -forward ), dungeon.IsValid() ? dungeon.GameObject : null,
				kind.Name, "", 0, 0, false );
			if ( go.IsValid() ) spawned++;
		}
		Log.Info( $"[Monster] spawned {spawned}x {kind.Name}" );
	}

	/// <summary>
	/// Why is there no navmesh here? Lists the physics bodies around the local player with their body type and
	/// shape kinds (hull = box colliders, mesh = model colliders), regenerates that one tile, and probes it.
	/// </summary>
	[ConCmd( "skeleton_navprobe" )]
	public static void NavProbeCommand()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		var player = scene.GetAllComponents<PlayerHealth>().FirstOrDefault( p => !p.IsProxy );
		if ( !player.IsValid() ) { Log.Warning( "[NavProbe] no local player" ); return; }
		_ = NavProbeAsync( scene, player.WorldPosition );
	}

	private static async System.Threading.Tasks.Task NavProbeAsync( Scene scene, Vector3 at )
	{
		var nav = scene.NavMesh;
		var box = BBox.FromPositionAndSize( at, 1024f );
		Log.Info( $"[NavProbe] navmesh enabled {nav.IsEnabled}, generating {nav.IsGenerating}, custom bounds {nav.CustomBounds} {nav.Bounds.Mins:0}..{nav.Bounds.Maxs:0}, at {at}" );
		var bodies = 0; var hulls = 0; var meshes = 0; var others = 0;
		var byType = new System.Collections.Generic.Dictionary<string, int>();
		foreach ( var body in scene.PhysicsWorld.Bodies )
		{
			if ( body.ShapeCount == 0 || !body.GetBounds().Overlaps( box ) ) continue;
			bodies++;
			var key = body.BodyType.ToString();
			byType[key] = byType.GetValueOrDefault( key ) + 1;
			foreach ( var shape in body.Shapes )
			{
				if ( shape.IsHullShape ) hulls++;
				else if ( shape.IsMeshShape ) meshes++;
				else others++;
			}
			if ( bodies <= 12 )
				Log.Info( $"[NavProbe]  body {body.GameObject?.Name ?? "?"} {body.BodyType} shapes {body.ShapeCount} bounds {body.GetBounds().Mins:0} .. {body.GetBounds().Maxs:0}" );
		}
		Log.Info( $"[NavProbe] {bodies} bodies within 512 u ({string.Join( ", ", byType.Select( kv => $"{kv.Key} {kv.Value}" ) )}); shapes: hull {hulls}, mesh {meshes}, other {others}" );

		var t0 = Time.Now;
		try
		{
			nav.IsEnabled = true;
			await nav.GenerateTiles( scene.PhysicsWorld, box );
		}
		catch ( Exception e ) { Log.Warning( $"[NavProbe] GenerateTiles threw: {e.Message}" ); }
		var near = nav.GetClosestPoint( at, 300f );
		var far = nav.GetClosestPoint( at, 2000f );
		var any = nav.GetRandomPoint();
		Log.Info( $"[NavProbe] regenerated tile in {Time.Now - t0:0.00} s: closest within 300 u {(near.HasValue ? near.Value.ToString() : "MISS")}, within 2000 u {(far.HasValue ? far.Value.ToString() : "MISS")}, any polygon anywhere {(any.HasValue ? any.Value.ToString() : "NONE")}" );
	}

	/// <summary>
	/// skeleton_speeds: what every awake skeleton is actually doing, for chasing down "they walk too slowly".
	/// Asked = what the brain requested, Agent = what the crowd agent was told, Real = how fast the body is
	/// actually travelling. Asked high and Real low means the agent is being interrupted, not throttled.
	/// </summary>
	[ConCmd( "skeleton_speeds" )]
	public static void SpeedsCommand()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		var player = VoidMage.Character.PlayerHealth.Local;
		var n = 0;
		foreach ( var npc in scene.GetAllComponents<SkeletonNpc>() )
		{
			if ( !npc.IsValid() || npc.IsDead || npc.Dormant ) continue;
			n++;
			var d = player.IsValid() ? npc.WorldPosition.WithZ( 0f ).Distance( player.WorldPosition.WithZ( 0f ) ) : -1f;
			var body = npc.Components.Get<SkeletonBody>( FindMode.EverythingInSelfAndDescendants );
			var blend = body.IsValid() ? $" blend {body.BlendX:0.00}/{body.BlendY:0.00} seen {body.MeasuredSpeed,4:0}" : "";
			Log.Info( $"[Skeleton] {npc.KindName,-14} {npc.State,-9} d {d,6:0} dz {npc.HeightGapToTarget,5:0} seen {npc.LastSeenAgo,5:0.0}s asked {npc.AskedSpeed,5:0} agent {npc.AgentSpeed,5:0} real {npc.RealSpeed,5:0}{blend}{(npc.Guard ? " guard" : "")}{(npc.Busy ? " busy" : "")}{(npc.SteeringStraight ? " nofallbackagent" : "")}{(npc.SlowAmount > 0f ? $" slow {npc.SlowAmount:0.00}" : "")}{(npc.MoveTarget is null ? " standing" : "")}" );
		}
		if ( n == 0 ) Log.Info( "[Skeleton] none awake" );
	}

	[ConCmd( "skeleton_kill_all" )]
	public static void KillAllCommand()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		foreach ( var npc in scene.GetAllComponents<SkeletonNpc>().ToList() ) npc.GameObject.Destroy();
	}
}

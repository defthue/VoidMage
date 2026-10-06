using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The sword's audio, heden's set: one whoosh per swing (HitNothingLight / HitNothingHeavy) at the
/// moment the blade swipes, and a hit layered on top when the swipe connects. The moment is per clip,
/// from the clip's synced start time: <see cref="WhooshSeconds"/> holds the frame where each swing's
/// visible cut is fastest, measured by tools/measure_sword_tip.py (the light swings peak on frame 5,
/// the heavies wind up for over half a second and peak on frame 18). Clips not in the table fall back
/// to the light/heavy delays. Every peer hears the whoosh from the synced clip; the owner sweeps for a
/// hit and broadcasts it.
/// </summary>
[Title( "Sword Sounds" ), Category( "Void Mage" ), Icon( "volume_up" )]
public sealed class SwordSounds : Component
{
	/// <summary>Fallback for attack clips without a measured moment.</summary>
	[Property] public float LightWhooshDelay { get; set; } = 0.15f;
	[Property] public float HeavyWhooshDelay { get; set; } = 0.35f;

	/// <summary>
	/// Seconds after the clip starts that the blade is fastest in front of the eye, per clip (peak frame
	/// at 30 fps, less half a frame so the sound's onset leads the picture). The lights all peak on
	/// frame 5, which is the 0.15 s that felt right by ear; the heavies peak on frame 18.
	/// </summary>
	public static readonly Dictionary<string, float> WhooshSeconds = new()
	{
		["anim_attack_swing_right_01"] = 0.15f,
		["anim_attack_swing_left_01"] = 0.15f,
		["anim_attack_swing_down"] = 0.15f,
		["anim_attack_swing_right_02"] = 0.15f,
		["anim_attack_swing_left_02"] = 0.15f,
		["anim_attack_swing_right_03"] = 0.15f,
		["anim_attack_heavy_swing_right"] = 0.58f,
		["anim_attack_heavy_swing_left"] = 0.58f,
		["anim_attack_heavy_swing_down"] = 0.58f,
	};
	/// <summary>How far from the eye the blade reaches.</summary>
	[Property] public float Reach { get; set; } = 60f;
	/// <summary>Radius of the sweep along the view that decides whether the blade met the world (the stone clang).</summary>
	[Property] public float SweepRadius { get; set; } = 12f;
	/// <summary>Half-angle of the cone in front of the eye the swing covers: every body inside it, in reach and in view, is hit.</summary>
	[Property, Range( 10f, 90f )] public float HitArcDegrees { get; set; } = 50f;
	[Property, Group( "Damage" )] public float LightDamage { get; set; } = 10f;
	[Property, Group( "Damage" )] public float HeavyDamage { get; set; } = 15f;
	/// <summary>How hard a light hit shoves the target (units/s of push direction); heavies push 1.5x.</summary>
	[Property, Group( "Damage" )] public float LightPush { get; set; } = 1f;
	/// <summary>
	/// The pack's "swing_right" clips were mirrored onto the left hand: whether the blade travels toward the
	/// player's right on screen decides which way the target staggers. Flip if the stagger goes the wrong way.
	/// </summary>
	[Property, Group( "Damage" )] public bool SwingRightMovesRight { get; set; } = true;

	public const string WhooshLight = "sounds/hitnothinglight.sound";
	public const string WhooshHeavy = "sounds/hitnothingheavy.sound";
	/// <summary>heden's monster hit; played on anything tagged enemy or npc.</summary>
	public const string HitBody = "sounds/lightabhit.sound";
	/// <summary>The blade meeting flesh (a bleeding body, its ragdoll, a corpse prop): a .sound path. No wet take exists yet, so it is the body hit until one is dropped in.</summary>
	[Property] public string HitFlesh { get; set; } = HitBody;
	/// <summary>The blade meeting the dungeon.</summary>
	public const string HitStone = "sounds/swordhitstone.sound";
	/// <summary>The blade meeting wooden furniture (tables, beds, crates...).</summary>
	public const string HitWood = "sounds/swordhittable.sound";

	private ClipPoseSource _source;
	private PlayerController _controller;
	private int _seenVersion = -1;
	private float _swipeAt = float.PositiveInfinity;
	private bool _heavy;
	private string _clip = "";

	private float _armsHitstopPending;

	protected override void OnUpdate()
	{
		if ( !_source.IsValid() ) _source = ClipPoseSource.Find( this );
		if ( !_controller.IsValid() ) _controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		if ( _armsHitstopPending > 0f )
		{
			// the frame after the strike: the skeleton is already frozen, now the arms
			Components.Get<FirstPersonArms>( FindMode.EverythingInSelfAndAncestors )?.Hitstop( _armsHitstopPending );
			_armsHitstopPending = 0f;
		}
		if ( !_source.IsValid() ) return;

		if ( _source.Version != _seenVersion )
		{
			_seenVersion = _source.Version;
			var clip = _source.Clip ?? "";
			if ( clip.StartsWith( "anim_attack_", StringComparison.Ordinal ) )
			{
				_clip = clip;
				_heavy = clip.Contains( "heavy", StringComparison.Ordinal );
				var delay = WhooshSeconds.TryGetValue( clip, out var measured ) ? measured : (_heavy ? HeavyWhooshDelay : LightWhooshDelay);
				_swipeAt = _source.StartTime + delay / MathF.Max( 0.05f, _source.Rate );
			}
			else
			{
				_swipeAt = float.PositiveInfinity;
			}
		}

		if ( Time.Now < _swipeAt ) return;
		_swipeAt = float.PositiveInfinity;

		var mouth = WorldPosition + Vector3.Up * 56f;
		VoidMage.Audio.GameAudio.Play( _heavy ? WhooshHeavy : WhooshLight, mouth );

		if ( IsProxy || !_controller.IsValid() ) return;

		// The swing is an arc, not a ray: every body inside the cone in front of the eye and within reach is hit,
		// as long as nothing solid stands between the eye and it (a wall, a pillar, a shield-sized crate). The
		// player is ignored by its own object, never GameObject.Root: the dungeon builder parents the player
		// under the dungeon, so Root is the dungeon and ignoring it would ignore every wall.
		var eye = _controller.EyeTransform;
		var forward = eye.Rotation.Forward;
		var cosArc = MathF.Cos( HitArcDegrees.DegreeToRadian() );
		// the run's boons: reach (Long Blade) and damage (sword, heavy and all-damage lines add up)
		var boons = VoidMage.Boons.PlayerBoons.Find( _controller.GameObject );
		var reach = Reach * (boons?.Scale( VoidMage.Boons.BoonStat.SwordReach ) ?? 1f);
		var damage = SwordDamage( boons, _heavy );
		// the way the blade actually travels (the measured crescent), the coarse left/right/forward rule as fallback
		if ( !SwordSlash.TryBladeDirection( _clip, eye.Rotation, out var blade, out var bladeEye ) ) blade = SwingPush( _clip, eye.Rotation );
		var push = blade * (_heavy ? 1.5f : 1f) * LightPush;
		var sway = Components.Get<SwordSway>( FindMode.EverythingInSelfAndAncestors );
		var hits = 0;
		GameObject firstBody = null;
		var firstContact = Vector3.Zero;
		_struck.Clear();
		foreach ( var go in Scene.FindInPhysics( new Sphere( eye.Position, reach + BodyRadius ) ) )
		{
			if ( go.Tags.Has( "boss" ) ) continue;   // the boss's parts are judged below, by the nearest point of their box
			if ( !VoidMage.Combat.DamageRouter.IsHostileBody( go, VoidMage.Combat.Team.Ally ) || !_struck.Add( go ) ) continue;
			var chest = go.WorldPosition + Vector3.Up * BodyChestHeight;
			var to = chest - eye.Position;
			var dist = to.Length;
			if ( dist > reach + BodyRadius || dist < 1f ) continue;
			if ( Vector3.Dot( to / dist, forward ) < cosArc ) continue;
			var los = Scene.Trace.Ray( eye.Position, chest )
				.WithoutTags( "player", "viewmodel", "posesource", "spellfire", "trigger", "npc", "enemy", "ally", VoidMage.Combat.Debris.Tag )
				.IgnoreGameObjectHierarchy( _controller.GameObject )
				.Run();
			if ( los.Hit ) continue;   // something solid in the way

			var contact = go.WorldPosition + Vector3.Up * BodyChestHeight - to / dist * BodyRadius;
			contact.z = AimHeight( go, eye.Position, forward, contact );
			StruckBody( contact, blade, _heavy, hits == 0, Bleeds( go ) );
			if ( hits == 0 ) { firstBody = go; firstContact = contact; }
			hits++;
			VoidMage.Combat.DamageRouter.Apply( go, new VoidMage.Combat.DamageRouter.Hit
			{
				Damage = damage,
				Position = contact,
				Push = push,
				Attacker = _controller.GameObject,
				Kind = _heavy ? "sword_heavy" : "sword_light",
			} );
		}

		// the Bone Colossus: a 7 m hand or the skull is judged like a crate, by the nearest point of its box, never as a
		// 14 u capsule at its origin (which sits in the lava under a hand). Bone does not bleed.
		var tip = eye.Position + forward * reach;
		foreach ( var part in VoidMage.Boss.BossNet.Near( eye.Position, reach ) )
		{
			if ( !_struck.Add( part.GameObject ) ) continue;
			Vector3 closest;
			if ( part.Contains( eye.Position ) )
			{
				// standing inside the hand or the skull: the blade is in it whichever way it swings
				closest = eye.Position + forward * BladeHalfWidth;
			}
			else
			{
				// judged by the point of it nearest the BLADE'S LINE, in the cone or right on the line: what you swing at.
				// (Nearest-to-the-eye failed beside a hand: that point is the palm top under you, out of the arc.)
				closest = part.ClosestPointToSegment( eye.Position, tip, out var onBlade );
				var to = closest - eye.Position;
				var dist = to.Length;
				if ( dist > reach + BladeHalfWidth ) continue;
				var onLine = closest.DistanceSquared( onBlade ) <= BladeHalfWidth * BladeHalfWidth;
				if ( !onLine && (dist < 1f || Vector3.Dot( to / dist, forward ) < cosArc) ) continue;
				var los = Scene.Trace.Ray( eye.Position, closest + to.Normal * 2f )
					.WithoutTags( "player", "viewmodel", "posesource", "spellfire", "trigger", "npc", "enemy", "ally", VoidMage.Combat.Debris.Tag )
					.IgnoreGameObjectHierarchy( _controller.GameObject )
					.IgnoreGameObjectHierarchy( part.GameObject )
					.Run();
				if ( los.Hit ) continue;
			}
			StruckBody( closest, blade, _heavy, hits == 0, false );
			if ( hits == 0 ) { firstBody = part.GameObject; firstContact = closest; }
			hits++;
			VoidMage.Combat.DamageRouter.Apply( part.GameObject, new VoidMage.Combat.DamageRouter.Hit
			{
				Damage = damage,
				Position = closest,
				Push = push,
				Attacker = _controller.GameObject,
				Kind = _heavy ? "sword_heavy" : "sword_light",
			} );
		}

		// crates, barrels and vases in the arc: every one the blade sweeps through breaks, judged against the nearest
		// point of its box (a wide crate at the edge of the arc still counts). Walls and other breakables block the
		// line, so a stack comes down front to back rather than all at once.
		var broke = 0;
		foreach ( var prop in VoidMage.Combat.BreakableNet.Near( eye.Position, reach ) )
		{
			var closest = prop.ClosestPoint( eye.Position );
			var to = closest - eye.Position;
			var dist = to.Length;
			if ( dist > reach ) continue;
			var toCentre = prop.WorldBounds.Center - eye.Position;
			if ( Vector3.Dot( toCentre.Normal, forward ) < cosArc && (dist < 1f || Vector3.Dot( to / dist, forward ) < cosArc) ) continue;
			var los = Scene.Trace.Ray( eye.Position, closest + to.Normal * 2f )
				.WithoutTags( "player", "viewmodel", "posesource", "spellfire", "trigger", "npc", "enemy", "ally", VoidMage.Combat.Debris.Tag )
				.IgnoreGameObjectHierarchy( _controller.GameObject )
				.IgnoreGameObject( prop.GameObject )
				.Run();
			if ( los.Hit ) continue;
			prop.Strike( closest, push, _heavy, localAttacker: true );
			broke++;
		}

		if ( hits > 0 )
		{
			// mana for every body the blade landed on: the base point every player gets, plus Spellsword / Blood Mana
			var mana = Components.Get<PlayerMana>( FindMode.EverythingInSelfAndAncestors );
			var manaPerHit = (mana?.ManaPerSwordHit ?? 0f) + (boons?.Get( VoidMage.Boons.BoonStat.ManaPerSwordHit ) ?? 0f);
			if ( manaPerHit > 0f ) mana?.Restore( manaPerHit * hits );
			// one hitstop and one kick per swing however many it caught: the feel does not stack. The arms freeze one
			// frame AFTER the skeleton (it stops on its hurt pulse) so the blade visibly sticks in it.
			_armsHitstopPending = _heavy ? VoidMage.Combat.HitFeel.DealtHeavyHitstopSeconds : VoidMage.Combat.HitFeel.DealtHitstopSeconds;
			// the kick is against the blade's travel in eye space (x forward, y left, z up -> yaw left, pitch down)
			var against = -new Vector2( bladeEye.y, -bladeEye.z );
			VoidMage.Combat.HitFeel.DealtHit( against, -blade, _heavy );
			sway?.Connect( blade, eye.Rotation, _heavy );
			// The Conduit: once per swing that connected, the staff may answer with a miniature spell
			Components.Get<VoidMage.Spells.SpellCaster>( FindMode.EverythingInSelfAndAncestors )?.OnSwordHit( firstContact, firstBody );
			return;
		}

		if ( broke > 0 )
		{
			// wood gives way: a short freeze, a light kick, and the blade carries on through
			_armsHitstopPending = VoidMage.Combat.BreakableProp.HitstopSeconds;
			var against = -new Vector2( bladeEye.y, -bladeEye.z );
			VoidMage.Combat.HitFeel.DealtHit( against, -blade, false );
			sway?.Connect( blade, eye.Rotation, false );
			return;
		}

		// the corpses lying about (FleshProp: most have no collider, so the sweep below would never meet them): the
		// nearest one in the arc takes the blade, blood and a soft freeze, no damage and nothing moves
		VoidMage.Combat.FleshProp flesh = null;
		var fleshAt = Vector3.Zero;
		var fleshDist = float.MaxValue;
		foreach ( var prop in VoidMage.Combat.FleshNet.Near( eye.Position, reach ) )
		{
			var closest = prop.ClosestPoint( eye.Position );
			var to = closest - eye.Position;
			var dist = to.Length;
			if ( dist > reach || dist >= fleshDist ) continue;
			var toCentre = prop.WorldBounds.Center - eye.Position;
			if ( Vector3.Dot( toCentre.Normal, forward ) < cosArc && (dist < 1f || Vector3.Dot( to / dist, forward ) < cosArc) ) continue;
			var los = Scene.Trace.Ray( eye.Position, closest + to.Normal * 2f )
				.WithoutTags( "player", "viewmodel", "posesource", "spellfire", "trigger", "npc", "enemy", "ally", VoidMage.Combat.Debris.Tag )
				.IgnoreGameObjectHierarchy( _controller.GameObject )
				.IgnoreGameObjectHierarchy( prop.GameObject )
				.Run();
			if ( los.Hit ) continue;
			flesh = prop; fleshAt = closest; fleshDist = dist;
		}
		if ( flesh is not null )
		{
			StruckFlesh( fleshAt, blade, _heavy );
			_armsHitstopPending = VoidMage.Combat.BreakableProp.HitstopSeconds;
			var against = -new Vector2( bladeEye.y, -bladeEye.z );
			VoidMage.Combat.HitFeel.DealtHit( against, -blade, false );
			sway?.Connect( blade, eye.Rotation, false );
			return;
		}

		// nobody in the arc: did the blade meet the dungeon? A short sphere sweep along the view for the clang.
		var world = Scene.Trace.Ray( eye.Position, eye.Position + forward * reach )
			.Radius( SweepRadius )
			.WithoutTags( "player", "viewmodel", "posesource", "spellfire", "trigger", "npc", "enemy", "ally", VoidMage.Combat.Debris.Tag )
			.IgnoreGameObjectHierarchy( _controller.GameObject )
			.Run();
		if ( !world.Hit ) return;
		if ( world.GameObject.IsValid() && world.GameObject.Tags.Has( VoidMage.Npc.SkeletonCorpse.RagdollTag ) )
		{
			// a corpse: bat it along the blade, sparks (blood if it was flesh), no damage
			VoidMage.Npc.SkeletonCorpse.Bat( world.GameObject, world.HitPosition, blade, _heavy );
			StruckBody( world.HitPosition, blade, _heavy, true, VoidMage.Npc.SkeletonCorpse.BleedsAt( world.GameObject ) );
			sway?.Connect( blade, eye.Rotation, _heavy );
			return;
		}
		StruckWorld( world.HitPosition, world.Normal, blade, _heavy, IsWood( world.GameObject ) );
		sway?.Connect( blade, eye.Rotation, false );
	}

	/// <summary>
	/// A swing's damage with the run's boons: the sword, heavy and all-damage lines add into one multiplier (never
	/// under 10%), and the lot is rolled +-15 % (HitFeel.Vary) so no two swings read the same. One roll per swing,
	/// shared by every body the arc catches.
	/// </summary>
	private float SwordDamage( VoidMage.Boons.PlayerBoons boons, bool heavy )
	{
		var baseDamage = heavy ? HeavyDamage : LightDamage;
		if ( boons is null ) return VoidMage.Combat.HitFeel.Vary( baseDamage );
		var bonus = boons.Get( VoidMage.Boons.BoonStat.SwordDamage ) + boons.Get( VoidMage.Boons.BoonStat.Damage );
		if ( heavy ) bonus += boons.Get( VoidMage.Boons.BoonStat.HeavyDamage );
		return VoidMage.Combat.HitFeel.Vary( baseDamage * MathF.Max( 0.1f, 1f + bonus ) );
	}

	/// <summary>
	/// Where on a body the blade lands, up and down: the height the view ray crosses the body at, kept inside its colliders'
	/// height (a little in from the feet and the top), so a swing aimed at the legs bleeds at the legs and one aimed at the
	/// head at the head. The chest height when the view is near vertical or the body has no collider to measure.
	/// </summary>
	private static float AimHeight( GameObject body, Vector3 eye, Vector3 forward, Vector3 contact )
	{
		var flat = forward.WithZ( 0f ).Length;
		if ( flat < 0.1f ) return contact.z;
		var bottom = float.MaxValue;
		var top = float.MinValue;
		foreach ( var collider in body.Components.GetAll<Collider>( FindMode.EnabledInSelfAndDescendants ) )
		{
			if ( collider.IsTrigger ) continue;
			var bounds = collider.GetWorldBounds();
			bottom = MathF.Min( bottom, bounds.Mins.z );
			top = MathF.Max( top, bounds.Maxs.z );
		}
		if ( top <= bottom ) return contact.z;
		var height = top - bottom;
		var along = (contact - eye).WithZ( 0f ).Length / flat;
		return Math.Clamp( eye.z + forward.z * along, bottom + height * 0.15f, top - height * 0.08f );
	}

	/// <summary>Whether a live body is flesh (its SkeletonBody.Bleeds; the body component sits on the physics root, the renderer on a child).</summary>
	private static bool Bleeds( GameObject body )
		=> body.IsValid() && body.Components.Get<VoidMage.Npc.SkeletonBody>( FindMode.EverythingInSelfAndDescendants ) is { Bleeds: true };

	private static readonly string[] StoneWords = { "stone", "metal", "iron", "doorway", "sm_env_", "wall", "floor", "pillar", "stair", "arch", "ceiling", "tile" };
	private static readonly string[] WoodWords = { "wood", "table", "bed", "bench", "stool", "bookcase", "shelf", "crate", "barrel", "chest", "coffin", "plank", "chair", "door", "ladder", "cart", "rack", "stretchtable" };

	/// <summary>Wooden furniture and clutter by name (the dungeon has no surface assets); anything "stone" stays stone.</summary>
	private static bool IsWood( GameObject go )
	{
		if ( !go.IsValid() ) return false;
		var name = go.Name?.ToLowerInvariant() ?? "";
		var model = go.Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants )?.Model?.ResourceName?.ToLowerInvariant() ?? "";
		var hay = name + " " + model;
		// the building itself is stone whatever its name says (a "doorway" is not a door); the one wooden env piece is rubble planks
		if ( hay.Contains( "plank", StringComparison.Ordinal ) ) return true;
		foreach ( var s in StoneWords ) if ( hay.Contains( s, StringComparison.Ordinal ) ) return false;
		foreach ( var w in WoodWords ) if ( hay.Contains( w, StringComparison.Ordinal ) ) return true;
		return false;
	}

	/// <summary>A body's capsule radius and the height of the point the blade is judged against (the skeleton capsule is 14 x 12..62).</summary>
	private const float BodyRadius = 14f;
	private const float BodyChestHeight = 40f;
	/// <summary>How far off the blade's line a boss shape may sit and still be on the blade (the swing has width).</summary>
	private const float BladeHalfWidth = 20f;
	private readonly HashSet<GameObject> _struck = new();

	/// <summary>World direction a swing shoves what it hits: lateral swings along the player's right or left, overheads forward.</summary>
	private Vector3 SwingPush( string clip, Rotation eye )
	{
		var forward = eye.Forward.WithZ( 0f ).Normal;
		var right = -eye.Left.WithZ( 0f ).Normal;
		if ( clip.Contains( "_right", StringComparison.Ordinal ) ) return SwingRightMovesRight ? right : -right;
		if ( clip.Contains( "_left", StringComparison.Ordinal ) ) return SwingRightMovesRight ? -right : right;
		return forward;
	}

	/// <summary>The blade landed on a body: the sound once per swing, the contact effect on every body it caught (blood on flesh, sparks on bone).</summary>
	[Rpc.Broadcast]
	private void StruckBody( Vector3 position, Vector3 swing, bool heavy, bool first, bool flesh )
	{
		if ( first ) PlayHit( flesh, position );
		if ( flesh ) VoidMage.Combat.HitVfx.FleshHit( Scene, position, swing, heavy, localAttacker: !IsProxy && first );
		else VoidMage.Combat.HitVfx.BoneHit( Scene, position, swing, heavy, localAttacker: !IsProxy && first );
	}

	/// <summary>The blade met a corpse lying about (a FleshProp): blood, no damage.</summary>
	[Rpc.Broadcast]
	private void StruckFlesh( Vector3 position, Vector3 swing, bool heavy )
	{
		PlayHit( true, position );
		VoidMage.Combat.HitVfx.FleshHit( Scene, position, swing, heavy, localAttacker: !IsProxy );
	}

	private void PlayHit( bool flesh, Vector3 position )
	{
		VoidMage.Audio.GameAudio.Play( flesh && !string.IsNullOrEmpty( HitFlesh ) ? HitFlesh : HitBody, position );
	}

	/// <summary>The blade met the dungeon: the clang (or the wood thunk on furniture) and sparks off the surface.</summary>
	[Rpc.Broadcast]
	private void StruckWorld( Vector3 position, Vector3 normal, Vector3 swing, bool heavy, bool wood )
	{
		VoidMage.Audio.GameAudio.Play( wood ? HitWood : HitStone, position );
		VoidMage.Combat.HitVfx.StoneHit( Scene, position, normal, swing, heavy, localAttacker: !IsProxy );
	}
}

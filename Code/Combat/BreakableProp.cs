using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Combat;

/// <summary>
/// A crate, barrel, vase or piece of wooden furniture the dungeon builder placed: it comes apart into
/// <see cref="Debris"/>. Small things go in one hit; the big furniture takes a couple of light hits (it shudders and
/// sheds chips in between) or one heavy. The props are built deterministically on every peer from the seed, so they
/// are not networked; each gets a stable index in spawn order and <see cref="BreakableNet"/> relays hits by index.
/// Whoever swung applies the hit at once (a round trip later would read as input lag) and reports to the host, the
/// host tells everyone; a hit carries the resulting hit count, so the echo of a predicted hit is a no-op. Whatever
/// stood on the prop tumbles off, a lid attached to it breaks with it, and a breakable stacked on it comes down a
/// beat later. Nothing but the intact model is left behind (a vase keeps its broken base).
/// </summary>
[Title( "Breakable Prop" ), Category( "Void Mage" ), Icon( "inventory_2" )]
public sealed class BreakableProp : Component, DamageRouter.IHittable
{
	public const string Tag = "breakable";

	/// <summary>The arms' freeze on hitting something breakable: half the enemy hitstop, wood gives way rather than stopping the blade.</summary>
	[ConVar( "break_hitstop" )] public static float HitstopSeconds { get; set; } = 0.045f;
	/// <summary>Seconds before a prop that lost its support breaks in turn.</summary>
	[ConVar( "break_chain_delay" )] public static float ChainDelay { get; set; } = 0.12f;
	/// <summary>How far (units) furniture shudders on a hit it survives.</summary>
	[ConVar( "break_shake" )] public static float ShakeUnits { get; set; } = 3f;

	/// <summary>Crates, barrels and furniture coming apart (WoodSmash01-03).</summary>
	public const string SoundWood = "sounds/woodsmash.sound";
	/// <summary>Vases (VaseShatter01-05).</summary>
	public const string SoundClay = "sounds/vaseshatter.sound";
	/// <summary>A hit the furniture survives: the blade meeting wood.</summary>
	public const string SoundHit = "sounds/swordhittable.sound";
	public const string SoundThud = "sounds/debristhud.sound";

	private const float ShakeSeconds = 0.25f;

	public int Index { get; internal set; } = -1;
	public BreakableKits.Kit Kit { get; set; }
	/// <summary>Only the containers hide shards (crates, barrels, vases: user 2026-09-14); the wooden furniture and the boards break for nothing.</summary>
	public bool DropsShards { get; set; }
	/// <summary>A crate or barrel: a 1% chance of a weapon skin inside (ChestLoot.CrateSkin). Not the vases.</summary>
	public bool DropsSkins { get; set; }
	/// <summary>A chest: breaks open into ChestLoot (shards, maybe a potion, and its scrolls).</summary>
	public bool IsChest { get; set; }
	public bool InSecretRoom { get; set; }
	/// <summary>Guaranteed scrolls inside (ChestLoot.Assign): 1 for the floor's and the secret room's chest, 0 for the rest.</summary>
	public int LootScrolls { get; set; }
	/// <summary>A new player's first floor: this chest's scrolls are a spell and an enchantment.</summary>
	public bool FirstRunGift { get; set; }
	/// <summary>A companion mesh that breaks with this one (a coffin's lid), parented under this object.</summary>
	public GameObject Attach { get; set; }
	public BreakableKits.Kit AttachKit { get; set; }
	/// <summary>Units the burst is moved along the blow's flat direction before the pieces are born: a prop whose
	/// mesh reaches into static geometry (the boards nailed over a doorway, their ends inside the jambs) would
	/// otherwise leave its end pieces asleep inside the wall. 0 for everything free-standing.</summary>
	public float ClearPush { get; set; }
	public bool Broken { get; private set; }
	/// <summary>Light hits taken so far; the prop breaks when this reaches its kit's Hits.</summary>
	public int HitsTaken { get; private set; }
	public int Hits => Kit?.Hits ?? 1;

	private ModelRenderer _renderer;
	private BBox _bounds;
	private bool _boundsValid;
	private bool _shaking;
	private float _shakeStart;
	private Vector3 _rest;
	private Vector3 _shakePhase;

	/// <summary>The intact model's world box (asset data, so it is right even before the first frame draws).</summary>
	public BBox WorldBounds
	{
		get
		{
			if ( !_boundsValid )
			{
				_renderer ??= Components.Get<ModelRenderer>();
				var model = _renderer?.Model;
				_bounds = model is null || model.IsError ? BBox.FromPositionAndSize( WorldPosition, 40f ) : model.Bounds.Transform( WorldTransform );
				_boundsValid = true;
			}
			return _bounds;
		}
	}

	public Vector3 ClosestPoint( Vector3 p )
	{
		var b = WorldBounds;
		return new Vector3( p.x.Clamp( b.Mins.x, b.Maxs.x ), p.y.Clamp( b.Mins.y, b.Maxs.y ), p.z.Clamp( b.Mins.z, b.Maxs.z ) );
	}

	public void ReceiveHit( in DamageRouter.Hit hit )
		=> Strike( hit.Position, hit.Push, hit.Kind == "sword_heavy" || DamageRouter.IsSpell( hit.Kind ), localAttacker: false );

	/// <summary>A blow lands: a light one counts a hit, a heavy finishes it. Applied here now, told to the host, the host tells everyone else.</summary>
	public void Strike( Vector3 position, Vector3 push, bool heavy, bool localAttacker )
	{
		if ( Broken ) return;
		var taken = heavy ? Hits : HitsTaken + 1;
		Apply( taken, position, push, heavy, localAttacker );
		BreakableNet.Report( Index, taken, position, push, heavy );
	}

	/// <summary>Idempotent: a peer only ever moves its hit count up, so a predicted hit and its echo are one hit.</summary>
	internal void Apply( int taken, Vector3 position, Vector3 push, bool heavy, bool localAttacker )
	{
		if ( Broken || taken <= HitsTaken ) return;
		HitsTaken = taken;
		if ( HitsTaken >= Hits )
		{
			BreakLocal( position, push, heavy, localAttacker );
			return;
		}
		// a hit it survives: the wood thunk, a few chips off the contact, a shudder
		VoidMage.Audio.GameAudio.Play( SoundHit, position );
		var flat = push.WithZ( 0f );
		if ( flat.IsNearZeroLength ) flat = (WorldBounds.Center - position).WithZ( 0f );
		BreakVfx.WoodHit( Scene, position, flat.IsNearZeroLength ? Vector3.Forward : flat.Normal, localAttacker );
		if ( !_shaking ) { _shaking = true; _rest = LocalPosition; }
		_shakeStart = Time.Now;
		_shakePhase = new Vector3( Game.Random.Float( 0f, 100f ), Game.Random.Float( 0f, 100f ), Game.Random.Float( 0f, 100f ) );
	}

	protected override void OnUpdate()
	{
		if ( !_shaking ) return;
		var elapsed = Time.Now - _shakeStart;
		if ( elapsed >= ShakeSeconds )
		{
			LocalPosition = _rest;
			_shaking = false;
			return;
		}
		// heden's boulder shudder: a fast three-axis wobble that dies off squared
		var t = 1f - elapsed / ShakeSeconds;
		var amp = ShakeUnits * t * t;
		const float freq = 46f;
		LocalPosition = _rest + new Vector3(
			MathF.Sin( (elapsed + _shakePhase.x) * freq ),
			MathF.Sin( (elapsed + _shakePhase.y) * freq * 1.27f ),
			MathF.Sin( (elapsed + _shakePhase.z) * freq * 0.83f ) * 0.4f ) * amp;
	}

	internal void BreakLocal( Vector3 position, Vector3 push, bool heavy, bool localAttacker )
	{
		if ( Broken ) return;
		Broken = true;
		if ( _shaking ) { LocalPosition = _rest; _shaking = false; }
		// where the break's own frame goes (debris_debug 1)
		var timer = Sandbox.Diagnostics.FastTimer.StartNew();

		var bounds = WorldBounds;
		var centre = bounds.Center;
		// the pieces travel the way the blow did, flat: an overhead chop must not drive them into the floor
		var flat = push.WithZ( 0f );
		if ( flat.IsNearZeroLength ) flat = (centre - position).WithZ( 0f );
		if ( flat.IsNearZeroLength ) flat = Vector3.Forward;
		flat = flat.Normal;

		var wood = Kit is null || Kit.Stuff == BreakableKits.Stuff.Wood;
		VoidMage.Audio.GameAudio.Play( wood ? SoundWood : SoundClay, centre );
		var size = bounds.Size.Length * 0.5f;
		if ( wood ) BreakVfx.Wood( Scene, centre, size, flat, heavy, localAttacker );
		else BreakVfx.Clay( Scene, centre, size, flat, heavy, localAttacker );
		var vfxMs = timer.ElapsedMilliSeconds;

		var parent = GameObject.Parent.IsValid() ? GameObject.Parent : GameObject;
		// the run's currency: shards hidden in the crate, most of the time (host only: this runs on every peer);
		// the Hoarder boon scales them like it scales a chest's
		if ( DropsShards && Networking.IsHost && Game.Random.Float() < VoidMage.Run.ShardPickup.BreakChance )
		{
			int shards = Game.Random.Int( Math.Min( VoidMage.Run.ShardPickup.BreakMin, VoidMage.Run.ShardPickup.BreakMax ), Math.Max( VoidMage.Run.ShardPickup.BreakMin, VoidMage.Run.ShardPickup.BreakMax ) );
			VoidMage.Run.ShardPickup.Drop( Scene, centre, (int)MathF.Round( shards * VoidMage.Boons.PlayerBoons.LocalScale( VoidMage.Boons.BoonStat.ShardGain ) ), parent );
		}
		// a weapon stowed in a crate or barrel, now and then (the skins no monster carries)
		if ( DropsSkins && Networking.IsHost ) VoidMage.Run.ChestLoot.CrateSkin( Scene, centre, flat, parent );
		if ( IsChest ) VoidMage.Run.ChestLoot.Open( this, centre, flat, parent );
		// a crate or barrel ChestLoot.Assign marked with one of the floor's scrolls
		else if ( LootScrolls > 0 ) VoidMage.Run.ChestLoot.DropScrolls( this, centre, flat, parent );
		var lootMs = timer.ElapsedMilliSeconds;
		// the pieces of a prop set into the masonry start clear of it, on the side the blow goes
		var shift = flat * ClearPush;
		if ( Kit is not null )
		{
			var at = WorldTransform;
			at.Position += shift;
			Debris.Burst( Scene, at, new BBox( bounds.Mins + shift, bounds.Maxs + shift ), Kit, flat, heavy, wood ? SoundThud : null, parent );
			if ( Kit.Remnant is not null ) SpawnRemnant( Kit.Remnant, bounds );
		}
		if ( Attach.IsValid() && AttachKit is not null )
		{
			var attachModel = Attach.Components.Get<ModelRenderer>()?.Model;
			var attachBounds = attachModel is null ? bounds : attachModel.Bounds.Transform( Attach.WorldTransform );
			var at = Attach.WorldTransform;
			at.Position += shift;
			Debris.Burst( Scene, at, new BBox( attachBounds.Mins + shift, attachBounds.Maxs + shift ), AttachKit, flat, heavy, SoundThud, parent );
		}
		var debrisMs = timer.ElapsedMilliSeconds;

		foreach ( var r in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) ) r.Enabled = false;
		foreach ( var c in Components.GetAll<Collider>( FindMode.EverythingInSelfAndDescendants ) ) c.Enabled = false;

		// what stood on it tumbles off, what is stacked on it follows a beat later
		BreakableNet.FlingRiders( this, flat );
		BreakableNet.Collapse( this );
		// a potion shut inside (a chest) comes out of the wreck
		VoidMage.Run.PotionPickup.OpenInside( bounds, flat );
		if ( Debris.DebugLogging )
		{
			var total = timer.ElapsedMilliSeconds;
			Log.Info( $"[Break] {GameObject.Name}: {total:0.00} ms (sound+vfx {vfxMs:0.00}, loot {lootMs - vfxMs:0.00}, debris {debrisMs - lootMs:0.00}, riders+chain {total - debrisMs:0.00})" );
		}
	}

	/// <summary>The base that stays (a vase's broken bottom), stood on the floor the prop stood on.</summary>
	private void SpawnRemnant( string model, BBox bounds )
	{
		var m = Debris.LoadModel( model );
		if ( m is null || m.IsError ) return;
		var parent = GameObject.Parent.IsValid() ? GameObject.Parent : GameObject;
		var go = new GameObject( parent, true, GameObject.Name + " remnant" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		// the prop's own scale too (a def may spawn at 1.5x), and the base's own underside scaled with it
		go.WorldRotation = WorldRotation;
		go.WorldScale = WorldScale;
		go.WorldPosition = WorldPosition.WithZ( bounds.Mins.z - m.Bounds.Mins.z * WorldScale.z );
		go.Components.Create<ModelRenderer>().Model = m;
	}
}

/// <summary>
/// The breakables of the current dungeon by index, the hit relay, the fireball blast, the riders and the chain
/// collapse. Static RPCs because the props themselves are not networked: every peer built the same list in the
/// same order.
/// </summary>
public static class BreakableNet
{
	/// <summary>A fireball breaks everything within this of its impact.</summary>
	[ConVar( "fireball_break_radius" )] public static float BlastRadius { get; set; } = 90f;

	private static readonly List<BreakableProp> _props = new();
	private static readonly List<(BreakableProp prop, float at, Vector3 push)> _chain = new();

	public static int Count => _props.Count;

	/// <summary>Forget the previous dungeon's props (a rebuild spawns a fresh list).</summary>
	public static void Clear()
	{
		_props.Clear();
		_chain.Clear();
		Debris.ClearPrewarm();
		VoidMage.Run.PotionPickup.ClearShut();
	}

	public static int Register( BreakableProp prop )
	{
		prop.Index = _props.Count;
		_props.Add( prop );
		// its pieces load a few a frame from now on, not on the frame it breaks
		Debris.Prewarm( prop.Kit );
		Debris.Prewarm( prop.AttachKit );
		return prop.Index;
	}

	/// <summary>Every intact breakable whose box comes within `radius` of `at`.</summary>
	public static IEnumerable<BreakableProp> Near( Vector3 at, float radius )
	{
		var r2 = radius * radius;
		foreach ( var p in _props )
		{
			if ( !p.IsValid() || p.Broken ) continue;
			if ( p.ClosestPoint( at ).DistanceSquared( at ) <= r2 ) yield return p;
		}
	}

	public static void Report( int index, int taken, Vector3 position, Vector3 push, bool heavy )
	{
		if ( index < 0 ) return;
		if ( Networking.IsHost ) HitRpc( index, taken, position, push, heavy );
		else RequestHit( index, taken, position, push, heavy );
	}

	[Rpc.Host]
	private static void RequestHit( int index, int taken, Vector3 position, Vector3 push, bool heavy )
	{
		HitRpc( index, taken, position, push, heavy );
	}

	[Rpc.Broadcast]
	private static void HitRpc( int index, int taken, Vector3 position, Vector3 push, bool heavy )
	{
		if ( index < 0 || index >= _props.Count ) return;
		var p = _props[index];
		if ( p.IsValid() ) p.Apply( taken, position, push, heavy, localAttacker: false );
	}

	/// <summary>A fireball landed: every breakable in the blast flies away from it. Returns how many broke.</summary>
	public static int Blast( Vector3 at, float radius, bool localAttacker )
	{
		var n = 0;
		foreach ( var p in Near( at, radius ) )
		{
			var dir = (p.WorldBounds.Center - at).WithZ( 0f );
			if ( dir.IsNearZeroLength ) dir = Vector3.Random.WithZ( 0f );
			p.Strike( p.ClosestPoint( at ), dir.Normal, heavy: true, localAttacker );
			n++;
		}
		return n;
	}

	/// <summary>
	/// The small static props standing on the prop that just broke (candles, books, jars, a candle's flame card)
	/// become debris and tumble off. Judged by bounds: small, its bottom at the prop's top, its centre over the
	/// prop's footprint. Breakables stacked on it are not riders: <see cref="Collapse"/> takes those.
	/// </summary>
	public static void FlingRiders( BreakableProp under, Vector3 push )
	{
		var parent = under.GameObject.Parent;
		if ( !parent.IsValid() ) return;
		var b = under.WorldBounds;
		var top = b.Maxs.z;
		var riders = new List<GameObject>();
		foreach ( var child in parent.Children )
		{
			if ( !child.IsValid() || child == under.GameObject ) continue;
			if ( child.Tags.Has( BreakableProp.Tag ) || child.Tags.Has( Debris.Tag ) ) continue;
			var renderer = child.Components.Get<ModelRenderer>();
			if ( renderer is null || !renderer.Enabled ) continue;
			var rb = renderer.Bounds;
			var size = rb.Size;
			if ( size.x > 70f || size.y > 70f || size.z > 90f ) continue;
			if ( rb.Mins.z < top - 10f || rb.Mins.z > top + 30f ) continue;
			var c = rb.Center;
			if ( c.x < b.Mins.x - 4f || c.x > b.Maxs.x + 4f || c.y < b.Mins.y - 4f || c.y > b.Maxs.y + 4f ) continue;
			// a potion on a table is loot, not debris: it drops to the floor and can still be picked up
			if ( child.Components.Get<VoidMage.Run.PotionPickup>() is { } potion )
			{
				potion.Fall( push.WithZ( 0f ) * 30f );
				continue;
			}
			riders.Add( child );
		}
		foreach ( var rider in riders )
			Debris.Fling( rider, b.Center, push, BreakableProp.SoundThud, parent );
	}

	/// <summary>Whatever breakable stood on the prop that just broke comes down a beat later, with a small shove of its own.</summary>
	public static void Collapse( BreakableProp under )
	{
		var b = under.WorldBounds;
		foreach ( var p in _props )
		{
			if ( !p.IsValid() || p.Broken || p == under ) continue;
			var pb = p.WorldBounds;
			if ( MathF.Abs( pb.Mins.z - b.Maxs.z ) > 8f ) continue;
			var c = pb.Center;
			if ( c.x < b.Mins.x - 12f || c.x > b.Maxs.x + 12f || c.y < b.Mins.y - 12f || c.y > b.Maxs.y + 12f ) continue;
			_chain.Add( (p, Time.Now + BreakableProp.ChainDelay + Game.Random.Float( 0f, 0.08f ), Vector3.Random.WithZ( 0f ).Normal * 0.5f) );
		}
	}

	internal static void Tick()
	{
		for ( var i = _chain.Count - 1; i >= 0; i-- )
		{
			var (prop, at, push) = _chain[i];
			if ( Time.Now < at ) continue;
			_chain.RemoveAt( i );
			if ( prop.IsValid() && !prop.Broken ) prop.BreakLocal( prop.WorldBounds.Center, push, false, localAttacker: false );
		}
	}
}

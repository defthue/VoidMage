using System;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

/// <summary>
/// A bolt of any spell in flight, owner-simulated (the caster's client owns it, its transform is networked, every
/// peer builds the look from the synced spell id). Its life is the FlightProfile's phases in order: an Orbit around
/// the caster (The Orbit), then Flight (straight, arcing or drifting; Ricochet reflects it off walls), then Halting
/// into a Mine (Amber) that the host fires when a hostile comes near. The Wake registers a growing line hazard per
/// straight leg and a ring while orbiting. Homing (the spell's own or The Hound's) bends the velocity toward the
/// nearest hostile ahead; a wobble (the swarm's bees) walks the heading randomly around that course. The Needle
/// lets it strike a body and fly on (walls still stop it); The Return turns it round at
/// the end of its flight and brings it back through everything; The Horizon grows what it hits for with the
/// distance flown; The Fracture throws pieces on from where it finally lands. Damage never happens here: an impact
/// is one SpellNet.Impact to the host.
/// </summary>
[Title( "Spell Projectile" ), Category( "Void Mage" ), Icon( "whatshot" )]
public sealed class SpellProjectile : Component, ILatticeNode
{
	/// <summary>The most bolts one caster keeps alive: past it the oldest FREE bolt (a mirror's, an echo's, a wisp's) fizzles first, then the oldest of all (a full staff on the swarm would otherwise be hundreds). 64 until 2026-09-22 (user: 128).</summary>
	[ConVar( "spell_bolt_cap" )] public static int BoltCap { get; set; } = 128;
	private static readonly System.Collections.Generic.List<SpellProjectile> _live = new();

	public enum FlightPhase { Flight = 0, Orbit = 1, Halting = 2, Mine = 3, Dead = 4 }

	[Sync] public string SpellId { get; set; } = "";
	[Sync] public Vector3 Direction { get; set; } = Vector3.Forward;
	[Sync] public float Scale { get; set; } = 1f;
	[Sync] public int Phase { get; set; }
	[Sync] public int RingIndex { get; set; }

	/// <summary>Owner only: the cast this bolt belongs to.</summary>
	public CastContext Context { get; set; }
	/// <summary>Owner only: where on its ring the bolt starts (degrees of yaw).</summary>
	public float OrbitStartDegrees { get; set; }
	/// <summary>Owner only: this bolt's own random phase (the wobble's), rolled by the emitter.</summary>
	public float Seed { get; set; }

	public SpellDefinition Spell => _spell ??= SpellDatabase.Get( SpellId );
	public FlightPhase CurrentPhase => (FlightPhase)Phase;

	private SpellDefinition _spell;
	private FlightProfile Flight => Context?.Flight;

	private Vector3 _velocity;
	private float _speed;
	private float _born;
	private float _travelled;
	private bool _done;
	private int _bouncesLeft;
	private float _orbitUntil;
	private int _castSerial;
	private float _nextPropCheck;
	private float _orbitAngle;
	private float _haltStart = -1f;
	private float _mineUntil;
	private Guid _mineKey;
	private Guid _wakeKey;
	private Vector3 _wakeStart;
	private float _legTravelled;
	private Vector3 _legDirection;

	// the needle and the return (owner)
	private int _pierceLeft, _wallsLeft, _turnsLeft;
	private float _ghostLeft;
	private bool _returning;
	private float _damageScale = 1f;
	private float _flown;
	private readonly System.Collections.Generic.HashSet<Guid> _struck = new();

	// homing and wobble (owner)
	private ICombatant _homingTarget;
	private float _nextHomingSearch;
	private float _wobbleYaw, _wobblePitch;

	/// <summary>How far a leg's heading may drift before the straight wake ray is closed and a new one opened.</summary>
	private const float WakeLegBendDegrees = 25f;
	private const float HomingSearchSeconds = 0.1f;

	private SpellVfx.ProjectileLook _look;

	protected override void OnStart()
	{
		_born = Time.Now;
		GameObject.Tags.Add( "spellfire" );
		_look = SpellVfx.BuildProjectile( GameObject, Spell, Scale );

		if ( IsProxy || Context is null ) return;
		var f = Flight;
		_speed = f.Speed;
		_velocity = Direction * _speed;
		_bouncesLeft = f.Bounces;
		_pierceLeft = f.PierceBodies;
		// always 0 since 2026-09-22 (The Needle is bodies only; it used to take orbs through walls from the first copy)
		_wallsLeft = f.PierceWalls;
		_turnsLeft = f.ReturnTurns;
		_live.Add( this );
		while ( _live.Count > Math.Max( 8, BoltCap ) )
		{
			// the aimed cast's own bolts go last: they are thrown first, and oldest-first was killing exactly those
			var victim = _live.Find( b => b != this && b.Context is { IsFree: true } ) ?? _live[0];
			_live.Remove( victim );
			if ( victim.IsValid() && victim != this ) victim.Fizzle();
			else if ( victim == this ) break;
		}
		if ( f.LatticeRange > 0f ) LatticeField.Add( this );
		// the emitter decides who rings (a miniature born away from the staff never does), the profile only how
		if ( CurrentPhase == FlightPhase.Orbit )
		{
			Phase = (int)FlightPhase.Orbit;
			_orbitUntil = Time.Now + f.OrbitSeconds;
			_castSerial = Context.Caster.IsValid() ? Context.Caster.CastSerial : 0;
			_orbitAngle = OrbitStartDegrees;
			WorldPosition = OrbitPoint( _orbitAngle );
			OpenRingWake();
		}
		else
		{
			if ( f.Halts && f.Bounces == 0 ) _haltStart = Time.Now;
			OpenLegWake();
		}
		if ( Spell.Delivery == SpellDelivery.Orb && Context.Impact.ZoneRadius > 0f )
		{
			// the orb's area rides the orb itself; the host reads its networked position every tick. Amber parks it for
			// at most another zone's worth (24 s of mine on a 7 s zone was a 31 s zone, audit 2026-09-22).
			_zoneUntil = Time.Now + MathF.Max( Context.Impact.ZoneSeconds, f.LifetimeSeconds ) + MathF.Min( f.MineSeconds, Context.Impact.ZoneSeconds );
			RegisterZone( 1f );
		}
	}

	protected override void OnUpdate()
	{
		SpellVfx.Drive( _look, CurrentPhase );
	}

	protected override void OnDestroy()
	{
		SpellVfx.Release( _look );
		_live.Remove( this );
		LatticeField.Remove( this );
	}

	// ------------------------------------------------------------------ the lattice

	public Vector3 LatticePoint => WorldPosition;
	public CastContext LatticeContext => Context;
	public bool LatticeLive => !_done && this.IsValid() && CurrentPhase != FlightPhase.Dead;

	/// <summary>What a hit is worth right now: the way back (The Return) and the distance flown (The Horizon).</summary>
	private float DamageScale => _damageScale * ModifierTables.HorizonScale( Flight?.HorizonStacks ?? 0, _flown );

	// ------------------------------------------------------------------ owner

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || _done || Context is null ) return;
		switch ( CurrentPhase )
		{
			case FlightPhase.Orbit: TickOrbit( Time.Delta ); break;
			case FlightPhase.Flight:
			case FlightPhase.Halting: TickFlight( Time.Delta ); break;
			case FlightPhase.Mine: TickMine(); break;
		}
	}

	private Guid _zoneKey;
	private float _zoneUntil;

	/// <summary>The orb's burning area, (re)registered at `scale` of the cast's damage: The Horizon re-registers it where the orb comes to rest, paid for the distance it drifted.</summary>
	private void RegisterZone( float scale )
	{
		if ( _zoneKey != Guid.Empty ) SpellNet.RemoveHazard( _zoneKey );
		var seconds = MathF.Max( 0.5f, _zoneUntil - Time.Now );
		_zoneKey = SpellNet.AddHazard( HazardKind.FollowSphere, WorldPosition, WorldPosition, Context.Impact.ZoneRadius, 0f, 0f, seconds,
			GameObject, Context.Damage * scale * SpellHostTuning.DotHz, Spell.SlowFraction, Context.CasterRoot, Context.ToHitInfo() );
	}

	private Vector3 OrbitCentre()
	{
		var root = Context.CasterRoot;
		if ( !root.IsValid() ) return WorldPosition;
		var controller = root.Components.Get<PlayerController>();
		return controller.IsValid() ? controller.EyePosition - Vector3.Up * 16f : root.WorldPosition + Vector3.Up * 50f;
	}

	private Vector3 OrbitPoint( float degrees ) => OrbitCentre() + Rotation.FromYaw( degrees ).Forward * Flight.OrbitRadius( RingIndex );

	private void TickOrbit( float dt )
	{
		var f = Flight;
		var sign = (RingIndex & 1) == 0 ? 1f : -1f;
		_orbitAngle += sign * f.OrbitDegreesPerSecond * dt;
		var next = OrbitPoint( _orbitAngle );
		var from = WorldPosition;
		Direction = (next - from).IsNearZeroLength ? Direction : (next - from).Normal;

		// bodies and breakables stop an orbiting bolt (the ring passes through walls: it is the caster's, not the room's)
		var orb = Spell.Delivery == SpellDelivery.Orb;
		if ( !orb )
		{
			var hit = Scene.Trace.Ray( from, next ).Radius( f.Radius ).WithAnyTags( "npc" ).WithoutTags( "ally", "ragdoll" ).Run();
			if ( hit.Hit && DamageRouter.IsBody( hit.GameObject ) )
			{
				Land( hit.HitPosition, hit.Normal, hit.GameObject, Direction );
				return;
			}
			// every prop on the floor is walked for this, so 20 times a second is plenty for a bolt doing 170 u/s
			if ( Time.Now >= _nextPropCheck )
			{
				_nextPropCheck = Time.Now + 0.05f;
				foreach ( var prop in BreakableNet.Near( next, MathF.Max( f.Radius, 8f ) ) )
				{
					Land( prop.ClosestPoint( next ), -Direction, prop.GameObject, Direction );
					return;
				}
			}
		}
		WorldPosition = next;

		// A bolt circles until it hits something or its caster casts again (user, 2026-09-19; it used to fly off after
		// one lap). An orb keeps its timed lap: bodies never stop it, so its ring would simply never end.
		var caster = Context.Caster;
		var release = orb
			? Time.Now >= _orbitUntil
			: !caster.IsValid() || caster.CastSerial != _castSerial || Time.Now - _born > ModifierTables.OrbitMaxSeconds;
		if ( !release ) return;
		// the flight's lifetime starts now: a bolt let go after a minute on the ring would otherwise fizzle on the spot
		if ( !orb ) _born = Time.Now;
		// the orbit is over: outward, along the room; a ring of mines when Amber is on
		var outward = (WorldPosition - OrbitCentre()).WithZ( 0f );
		Direction = outward.IsNearZeroLength ? Direction : outward.Normal;
		_velocity = Direction * _speed;
		if ( f.Halts ) { BecomeMine(); return; }
		Phase = (int)FlightPhase.Flight;
		OpenLegWake();
	}

	private void TickFlight( float dt )
	{
		var f = Flight;
		if ( _returning ) { TickReturn( dt ); return; }
		if ( _ghostLeft > 0f ) { TickGhost( dt ); return; }
		if ( _haltStart >= 0f )
		{
			Phase = (int)FlightPhase.Halting;
			var k = (Time.Now - _haltStart) / MathF.Max( 0.05f, f.AmberHaltSeconds );
			if ( k >= 1f ) { BecomeMine(); return; }
			_speed = f.Speed * (1f - k);
			_velocity = _velocity.Normal * _speed;
		}
		else
		{
			if ( f.Homes ) Home( f, dt );
			if ( f.Base == FlightBase.Arc && f.Gravity > 0f )
			{
				_velocity += Vector3.Down * f.Gravity * dt;
				_speed = _velocity.Length;
			}
		}

		var from = WorldPosition;
		var step = _velocity * dt;
		if ( f.Wobbles && _haltStart < 0f ) step = Wobble( f, step, dt );
		var to = from + step;
		if ( !step.IsNearZeroLength )
		{
			Direction = step.Normal;
			WorldRotation = Rotation.LookAt( Direction );
		}
		// a bent leg: the wake ray is straight, so a turned bolt closes it and starts the next one from here
		if ( _wakeKey != Guid.Empty && Vector3.GetAngle( _legDirection, Direction ) > WakeLegBendDegrees )
		{
			CloseLegWake();
			OpenLegWake();
		}

		var trace = Scene.Trace.Ray( from, to ).Radius( f.Radius ).WithoutTags( AimSolver.IgnoreTags );
		if ( Spell.Delivery == SpellDelivery.Orb )
		{
			trace = trace.WithoutTags( "npc", "enemy" );   // the orb's zone does the hurting; bodies do not stop it
			// The Needle on an orb: it already drifts through bodies, so instead each one it passes through is stung once, a second of its zone at a time
			if ( _pierceLeft > 0 ) StrikeBodiesAlong( from, to, SpellHostTuning.DotHz, fracture: false );
		}
		if ( Context.CasterRoot.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( Context.CasterRoot );
		var result = trace.Run();

		if ( result.Hit )
		{
			if ( DamageRouter.IsBody( result.GameObject ) )
			{
				// a body this bolt already went through (The Needle, the way back) is air to it
				if ( !_struck.Contains( result.GameObject.Id ) )
				{
					if ( _pierceLeft > 0 )
					{
						_pierceLeft--;
						Strike( result.HitPosition, result.Normal, result.GameObject, Direction );
					}
					else if ( _turnsLeft > 0 )
					{
						Strike( result.HitPosition, result.Normal, result.GameObject, Direction );
						BeginReturn();
						return;
					}
					else
					{
						Land( result.HitPosition, result.Normal, result.GameObject, Direction );
						return;
					}
				}
			}
			else
			{
				// Ricochet bounces off what cannot be destroyed; a crate, a barrel, a vase, a chest or the secret wall's
				// tell takes the blast instead (user, 2026-09-19)
				var destructible = IsDestructible( result.GameObject );
				if ( _bouncesLeft > 0 && !destructible )
				{
					Bounce( result.EndPosition, result.Normal );
					return;
				}
				if ( f.Base == FlightBase.Drift && (_wallsLeft <= 0 || destructible) )
				{
					// The Return: the orb drifts back the way it came instead of settling
					if ( _turnsLeft > 0 && !destructible ) { WorldPosition = result.EndPosition + result.Normal * 2f; BeginReturn(); return; }
					// the orb sticks where it lands and keeps its zone
					WorldPosition = result.EndPosition;
					Park( f.Halts ? Time.Now + f.MineSeconds : _born + f.LifetimeSeconds );
					return;
				}
				if ( _wallsLeft > 0 && !destructible )
				{
					// a wall pierce (nothing grants one since 2026-09-22): into the wall, blind, and out the other side if there is one
					_wallsLeft--;
					_ghostLeft = ModifierTables.WallPierceDepth;
					CloseLegWake();
					ShowBounce( result.HitPosition, result.Normal );
				}
				else if ( _turnsLeft > 0 && !destructible )
				{
					Strike( result.HitPosition, result.Normal, result.GameObject, Direction );
					WorldPosition = result.EndPosition + result.Normal * 2f;
					BeginReturn();
					return;
				}
				else
				{
					Land( result.HitPosition, result.Normal, result.GameObject, Direction );
					return;
				}
			}
		}

		// crates, barrels and vases take ANY bolt (user, 2026-09-20: "make swarm destroy pots and vases"): a splash
		// found them by their boxes (BreakableNet.Blast), a bolt without one only by the trace above, which the small
		// ones slip. The same box test along this step, so a bee breaks the vase it flies into.
		if ( Spell.Delivery != SpellDelivery.Orb )
		{
			foreach ( var prop in BreakableNet.Near( from + step * 0.5f, step.Length * 0.5f + MathF.Max( f.Radius, 6f ) ) )
			{
				Land( prop.ClosestPoint( to ), -Direction, prop.GameObject, Direction );
				return;
			}
		}

		WorldPosition = to;
		var len = step.Length;
		_travelled += len;
		_legTravelled += len;
		_flown += len;

		if ( Time.Now - _born > f.LifetimeSeconds || _travelled > f.RangeUnits )
		{
			if ( _turnsLeft > 0 ) BeginReturn();
			else Fizzle();
		}
	}

	// ------------------------------------------------------------------ the wall pierce, the return

	/// <summary>Inside a wall: only bodies are looked for. When the depth is spent the normal flight resumes, and a bolt still inside something bursts there.</summary>
	private void TickGhost( float dt )
	{
		var from = WorldPosition;
		var step = _velocity * dt;
		var to = from + step;
		StrikeBodiesAlong( from, to );
		WorldPosition = to;
		var len = step.Length;
		_travelled += len;
		_flown += len;
		_ghostLeft -= len;
		if ( _ghostLeft <= 0f ) { _ghostLeft = 0f; OpenLegWake(); }
		if ( Time.Now - _born > Flight.LifetimeSeconds ) Fizzle();
	}

	/// <summary>The Return: straight back to the caster through walls and bodies alike, striking each body once on the way.</summary>
	private void TickReturn( float dt )
	{
		var home = OrbitCentre();
		var to = home - WorldPosition;
		var drift = Flight.Base == FlightBase.Drift;
		if ( to.Length < 45f || Time.Now - _born > (drift ? 9f : 6f) )
		{
			if ( _turnsLeft > 0 && to.Length < 45f )
			{
				// three copies: out again, along where the caster is looking now
				_turnsLeft--;
				_returning = false;
				_struck.Clear();
				_born = Time.Now;
				_travelled = 0f;
				var root = Context.CasterRoot;
				var controller = root.IsValid() ? root.Components.Get<PlayerController>() : null;
				Direction = controller.IsValid() ? controller.EyeTransform.Rotation.Forward : Direction;
				_speed = MathF.Max( _speed, Flight.Speed );
				_velocity = Direction * _speed;
				_bouncesLeft = Flight.Bounces;
				OpenLegWake();
				return;
			}
			Fizzle();
			return;
		}
		var speed = drift ? Flight.Speed * 2f : MathF.Max( 700f, Flight.Speed );
		_velocity = to.Normal * speed;
		var from = WorldPosition;
		var next = from + _velocity * dt;
		Direction = _velocity.Normal;
		WorldRotation = Rotation.LookAt( Direction );
		if ( _wakeKey != Guid.Empty && Vector3.GetAngle( _legDirection, Direction ) > WakeLegBendDegrees )
		{
			CloseLegWake();
			OpenLegWake();
		}
		if ( !drift ) StrikeBodiesAlong( from, next );   // an orb's area does its hurting, on the way back as on the way out
		WorldPosition = next;
		var len = (next - from).Length;
		_legTravelled += len;
		_flown += len;
	}

	private void BeginReturn()
	{
		_turnsLeft--;
		_returning = true;
		_damageScale = Flight.ReturnDamage;
		_born = Time.Now;
		_haltStart = -1f;
		Phase = (int)FlightPhase.Flight;
		CloseLegWake();
		_speed = MathF.Max( 700f, Flight.Speed );
		OpenLegWake();
		ShowBounce( WorldPosition, -Direction );
	}

	/// <summary>Owner, before the spawn: a body this bolt flies through untouched (the enemy a shard of The Fracture broke off).</summary>
	public void PassThrough( GameObject body )
	{
		if ( body.IsValid() ) _struck.Add( body.Id );
	}

	/// <summary>Every hostile body between two points that this bolt has not struck yet takes its hit; the bolt does not stop.</summary>
	private void StrikeBodiesAlong( Vector3 from, Vector3 to, float scale = 1f, bool fracture = true )
	{
		foreach ( var hit in Scene.Trace.Ray( from, to ).Radius( Flight.Radius ).WithAnyTags( "npc" ).WithoutTags( "ally", "ragdoll" ).RunAll() )
		{
			if ( !hit.Hit || !DamageRouter.IsBody( hit.GameObject ) || _struck.Contains( hit.GameObject.Id ) ) continue;
			Strike( hit.HitPosition, hit.Normal, hit.GameObject, Direction, scale, fracture );
		}
	}

	/// <summary>The bolt's impact WITHOUT its end: the hit and the splash go to the host, a puff marks it, the bolt flies on.</summary>
	private void Strike( Vector3 position, Vector3 normal, GameObject hitObject, Vector3 pushDir, float extraScale = 1f, bool fracture = true )
	{
		if ( hitObject.IsValid() ) _struck.Add( hitObject.Id );
		var scale = DamageScale * extraScale;
		if ( Spell.Delivery == SpellDelivery.Instant )
			ChainLightningCast.Resolve( Context, position, Direction, DamageRouter.IsBody( hitObject ) ? hitObject : null, scale, carried: true );
		else
		{
			ImpactResolver.Resolve( Context, position, normal, hitObject, pushDir.WithZ( 0f ).Normal, scale );
			// a bolt that goes on (through an enemy with The Needle, turning home with The Return) breaks here as well,
			// on the way out only: the way home strikes everything it crosses
			if ( !_returning && fracture ) FractureShards.Spawn( Context, position, normal, hitObject );
		}
		ShowStrike( position, normal );
	}

	// ------------------------------------------------------------------ homing and wobble

	/// <summary>Bend the velocity toward the nearest hostile ahead, at most the profile's turn rate, keeping the speed.</summary>
	private void Home( FlightProfile f, float dt )
	{
		if ( Time.Now >= _nextHomingSearch || !_homingTarget.IsValid() || !_homingTarget.CanBeTargeted )
		{
			_nextHomingSearch = Time.Now + HomingSearchSeconds;
			_homingTarget = FindHomingTarget( f );
		}
		if ( _homingTarget is null || _velocity.IsNearZeroLength ) return;
		var chest = _homingTarget is Boss.BossPart part ? part.ClosestPoint( WorldPosition ) : _homingTarget.AimPoint;
		var want = (chest - WorldPosition);
		if ( want.IsNearZeroLength ) return;
		var heading = _velocity.Normal;
		var angle = Vector3.GetAngle( heading, want.Normal );
		if ( angle < 0.01f ) return;
		var maxTurn = f.HomingTurnDegreesPerSecond * dt;
		var k = MathF.Min( 1f, maxTurn / angle );
		var turned = Vector3.Slerp( heading, want.Normal, k ).Normal;
		_velocity = turned * _velocity.Length;
	}

	/// <summary>The nearest live hostile body (a skeleton, a boss part) inside the homing reach and cone with a clear line to its chest; null when there is none. Owner side: the synced state is enough to pick.</summary>
	private ICombatant FindHomingTarget( FlightProfile f )
	{
		var heading = _velocity.IsNearZeroLength ? Direction : _velocity.Normal;
		var from = WorldPosition;
		var reach2 = f.HomingRadius * f.HomingRadius;
		var cosCone = MathF.Cos( f.HomingConeDegrees.DegreeToRadian() );
		ICombatant best = null;
		var bestD2 = float.MaxValue;
		foreach ( var npc in CombatTargets.Bodies( Team.Hostile ) )
		{
			var chest = npc is Boss.BossPart part ? part.ClosestPoint( from ) : npc.AimPoint;
			var to = chest - from;
			var d2 = to.LengthSquared;
			if ( d2 > reach2 || d2 >= bestD2 || d2 < 1f ) continue;
			if ( Vector3.Dot( heading, to.Normal ) < cosCone ) continue;
			var los = Scene.Trace.Ray( from, chest ).WithoutTags( SpellArea.LosIgnore ).WithoutTags( "spellfire" ).Run();
			if ( los.Hit ) continue;
			best = npc;
			bestD2 = d2;
		}
		return best;
	}

	/// <summary>The bee's flight: the heading walks randomly around the course, never straying past WobbleDegrees. Returns the wobbled step.</summary>
	private Vector3 Wobble( FlightProfile f, Vector3 step, float dt )
	{
		var walk = f.WobbleRate * dt;
		_wobbleYaw = Math.Clamp( _wobbleYaw + Game.Random.Float( -walk, walk ) - _wobbleYaw * 2f * dt, -f.WobbleDegrees, f.WobbleDegrees );
		_wobblePitch = Math.Clamp( _wobblePitch + Game.Random.Float( -walk, walk ) - _wobblePitch * 2f * dt, -f.WobbleDegrees, f.WobbleDegrees );
		if ( step.IsNearZeroLength ) return step;
		var course = Rotation.LookAt( step.Normal );
		var wobbled = course * Rotation.From( new Angles( _wobblePitch, _wobbleYaw, 0f ) );
		return wobbled.Forward * step.Length;
	}

	private static bool IsDestructible( GameObject hit ) => hit.IsValid()
		&& (hit.Components.Get<BreakableProp>( FindMode.EverythingInSelfAndAncestors ) is not null
			|| hit.Components.Get<SecretWall>( FindMode.EverythingInSelfAndAncestors ) is not null);

	private void Bounce( Vector3 at, Vector3 normal )
	{
		var f = Flight;
		_bouncesLeft--;
		_velocity = Vector3.Reflect( _velocity, normal ) * f.BounceSpeedKeep;
		_speed = _velocity.Length;
		Direction = _velocity.IsNearZeroLength ? normal : _velocity.Normal;
		WorldPosition = at + normal * 1f;
		if ( _bouncesLeft == 0 && f.Halts ) _haltStart = Time.Now;
		CloseLegWake();
		OpenLegWake();
		ShowBounce( WorldPosition, normal );
	}

	private void TickMine()
	{
		if ( Time.Now < _mineUntil ) return;
		// The Return on a mine nobody walked into: it comes home instead of fizzling (an orb parked on a wall just ends)
		if ( _turnsLeft > 0 && Spell.Delivery != SpellDelivery.Orb )
		{
			if ( _mineKey != Guid.Empty ) { SpellNet.RemoveHazard( _mineKey ); _mineKey = Guid.Empty; }
			BeginReturn();
			return;
		}
		Fizzle();
	}

	/// <summary>Amber: stop here and wait for a hostile. The host owns the trigger.</summary>
	private void BecomeMine()
	{
		var f = Flight;
		CloseLegWake();
		_velocity = Vector3.Zero;
		_haltStart = -1f;
		Phase = (int)FlightPhase.Mine;
		_mineUntil = Time.Now + f.MineSeconds;
		if ( Spell.Delivery == SpellDelivery.Orb ) return;   // the orb's zone is already registered; parked is enough
		var impact = Context.Impact;
		_mineKey = SpellNet.AddHazard( HazardKind.Mine, WorldPosition, WorldPosition, f.MineTriggerRadius, 0f, 0f, f.MineSeconds,
			GameObject, 0f, 0f, Context.CasterRoot, Context.ToHitInfo(), Context.Damage * DamageScale, impact.SplashRadius, impact.SplashFraction );
	}

	/// <summary>The orb came to rest (a wall, or Amber): it sits and its zone keeps working until `until`.</summary>
	private void Park( float until )
	{
		// where it comes to rest: The Horizon pays the area for the drift, The Fracture breaks small orbs off it
		if ( _zoneKey != Guid.Empty && Flight.HorizonStacks > 0 ) RegisterZone( ModifierTables.HorizonScale( Flight.HorizonStacks, _flown ) );
		FractureShards.Spawn( Context, WorldPosition, Vector3.Up );
		CloseLegWake();
		_velocity = Vector3.Zero;
		_haltStart = -1f;
		Phase = (int)FlightPhase.Mine;
		_mineUntil = until;
	}

	/// <summary>The host fired this mine and already dealt its damage: the owner does the local extras and the burst (a parked chain zaps from here).</summary>
	public void OnMineTriggered()
	{
		if ( IsProxy || _done ) return;
		_done = true;
		_mineKey = Guid.Empty;
		ImpactResolver.LocalExtras( Context, WorldPosition, Vector3.Up );
		if ( Spell.Delivery == SpellDelivery.Instant ) ChainLightningCast.Resolve( Context, WorldPosition, Direction, null, carried: true );
		if ( Spell.Delivery != SpellDelivery.Instant ) FractureShards.Spawn( Context, WorldPosition, Vector3.Up );   // a chain forks from its own last link
		Explode( WorldPosition, Vector3.Up, (int)ImpactKind.Body );
		Retire();
	}

	/// <summary>The bolt is finished: destroyed, or parked for the director's tape (off the live list either way).</summary>
	private void Retire() => VoidMage.Dev.WorldTape.Retire( GameObject, () => { _live.Remove( this ); LatticeField.Remove( this ); } );

	private void Land( Vector3 position, Vector3 normal, GameObject hitObject, Vector3 pushDir )
	{
		_done = true;
		CloseLegWake();
		if ( _mineKey != Guid.Empty ) { SpellNet.RemoveHazard( _mineKey ); _mineKey = Guid.Empty; }
		// a chain riding a carrier resolves its jumps from where the carrier stopped
		if ( Spell.Delivery == SpellDelivery.Instant )
		{
			ChainLightningCast.Resolve( Context, position, Direction, DamageRouter.IsBody( hitObject ) ? hitObject : null, DamageScale, carried: true );
			// a chain only ever damages bodies and never runs ImpactResolver, so the one thing it can do to the
			// dungeon is opened here: the secret wall it stopped against
			VoidMage.Combat.SecretWallNet.Blast( position, 24f );
		}
		else ImpactResolver.Resolve( Context, position, normal, hitObject, pushDir.WithZ( 0f ).Normal, DamageScale );
		if ( Spell.Delivery != SpellDelivery.Instant ) FractureShards.Spawn( Context, position, normal, hitObject );   // a chain forks from its own last link
		Explode( position, normal, (int)(DamageRouter.IsBody( hitObject ) ? ImpactKind.Body : ImpactKind.World) );
		Retire();
	}

	private void Fizzle()
	{
		if ( _done ) return;
		_done = true;
		CloseLegWake();
		if ( _mineKey != Guid.Empty ) { SpellNet.RemoveHazard( _mineKey ); _mineKey = Guid.Empty; }
		Explode( WorldPosition, -Direction, (int)ImpactKind.Fizzle );
		Retire();
	}

	// ------------------------------------------------------------------ the wake

	private void OpenLegWake()
	{
		var f = Flight;
		if ( !f.Wakes || _wakeKey != Guid.Empty ) return;
		_wakeStart = WorldPosition;
		_legTravelled = 0f;
		_legDirection = Direction;
		var reach = MathF.Max( 50f, f.RangeUnits - _travelled );
		var travel = reach / MathF.Max( 1f, _speed );
		_wakeKey = SpellNet.AddHazard( HazardKind.Ray, _wakeStart, _wakeStart + Direction * reach, f.WakeWidth * 0.5f, f.WakeWidth, _speed, travel + f.WakeSeconds,
			null, SpellHostTuning.WakeDps( Spell ), SpellHostTuning.WakeSlow( Spell ), Context.CasterRoot, Context.ToHitInfo() );
	}

	private void CloseLegWake()
	{
		if ( _wakeKey == Guid.Empty ) return;
		SpellNet.EndHazard( _wakeKey, _legTravelled );
		_wakeKey = Guid.Empty;
	}

	private void OpenRingWake()
	{
		var f = Flight;
		if ( !f.Wakes || RingIndex != 0 ) return;   // one ring hazard per orbit, from the first bolt of the ring
		SpellNet.AddHazard( HazardKind.Ring, WorldPosition, WorldPosition, f.OrbitRadius( RingIndex ), f.WakeWidth, 0f, f.OrbitSeconds + f.WakeSeconds,
			Context.CasterRoot, SpellHostTuning.WakeDps( Spell ), SpellHostTuning.WakeSlow( Spell ), Context.CasterRoot, Context.ToHitInfo() );
	}

	// ------------------------------------------------------------------ every peer

	/// <summary>Every client: the trail is let go so it outlives the bolt, then the burst and its sounds.</summary>
	[Rpc.Broadcast]
	private void Explode( Vector3 position, Vector3 normal, int kind )
	{
		Phase = (int)FlightPhase.Dead;
		var impact = (ImpactKind)kind;
		SpellVfx.Release( _look );
		_look = null;
		SpellVfx.Burst( Scene, position, normal, Spell, Scale, impact );
	}

	[Rpc.Broadcast( NetFlags.Unreliable )]
	private void ShowBounce( Vector3 position, Vector3 normal )
	{
		SpellVfx.Bounce( Scene, position, normal, Spell, Scale );
	}

	/// <summary>The bolt struck something and flew on: a small burst and the spell's impact sound, the bolt's look untouched.</summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	private void ShowStrike( Vector3 position, Vector3 normal )
	{
		SpellVfx.Bounce( Scene, position, normal, Spell, Scale * 1.6f );
		if ( Spell?.SoundImpact is { } sound ) VoidMage.Audio.GameAudio.Play( sound, position );
	}
}

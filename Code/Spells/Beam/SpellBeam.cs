using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Spells;

/// <summary>
/// One ray of the Void Lance (the laser; it was the "Piercing Laser" and went through every body in its line until
/// 2026-09-20: "a bit too strong ... just hits the first enemy because we also have a piercing enchantment now"),
/// owner-simulated: from the staff's gem along the aim (or swept around the caster by The Orbit, fanned by
/// Multiplication), reflected off walls by Ricochet. It STOPS AT THE FIRST HOSTILE BODY, which is hit on every tick,
/// and everything that happens "where the ray ends" happens on that enemy; The Needle takes it through bodies (its
/// bolt numbers), never walls (user, 2026-09-22). Amber drops burning spots where it ends; The Wake leaves its last
/// line on the ground; The Hound leans it toward whoever stands nearest the aim; The Horizon makes the far end
/// hit harder; The Anvil bursts where it ends; The Fracture sprays short rays from there; The Return fires one ray
/// back down the path when the window closes; The Lattice joins the ends of a fan. Proxies re-trace the synced
/// origin and direction against the same static dungeon for the ribbon, so nothing per-tick crosses the wire.
/// </summary>
[Title( "Spell Beam" ), Category( "Void Mage" ), Icon( "flare" )]
public sealed class SpellBeam : Component, ILatticeNode
{
	[Sync] public string SpellId { get; set; } = "";
	[Sync] public Vector3 Origin { get; set; }
	[Sync] public Vector3 Direction { get; set; } = Vector3.Forward;
	[Sync] public float Scale { get; set; } = 1f;
	[Sync] public int Bounces { get; set; }
	/// <summary>Walls the ray burns through before it stops. Nothing grants any since 2026-09-22 (The Needle is bodies only).</summary>
	[Sync] public int WallPierces { get; set; }
	/// <summary>The Needle: hostile bodies the ray goes through before one stops it (0 = the first body ends it).</summary>
	[Sync] public int BodyPierces { get; set; }
	/// <summary>A body this ray does not see at all: the enemy a Fracture ray broke off (CastContext.IgnoreBody).</summary>
	[Sync] public Guid IgnoreBodyId { get; set; }
	/// <summary>Owner only: a free ray that does not leave from the staff keeps the origin it was born at.</summary>
	public bool FixedOrigin { get; set; }
	/// <summary>Owner only: this ray IS the return; it never fires another.</summary>
	public bool IsReturn { get; set; }
	[Sync] public bool Active { get; set; }

	/// <summary>Owner only.</summary>
	public CastContext Context { get; set; }
	public int ForkIndex { get; set; }
	/// <summary>How long this ray burns before it ends itself.</summary>
	[Sync] public float WindowSeconds { get; set; } = 1.2f;
	/// <summary>The real cast follows the crosshair while it burns; a free copy holds the line it was born on.</summary>
	public bool Steers { get; set; } = true;

	public SpellDefinition Spell => _spell ??= SpellDatabase.Get( SpellId );
	public IReadOnlyList<Vector3> Points => _points;

	private SpellDefinition _spell;
	private readonly List<Vector3> _points = new();
	private float _born;
	private float _nextTick;
	private float _nextAmber;
	private float _endedAt = -1f;
	private float _orbitAngle;
	private Vector3 _aimAtStart;
	private SpellVfx.BeamLook _look;
	private SoundHandle _loop;
	private readonly HashSet<Guid> _tickHit = new();
	private int _ticks;
	private float _nextImpactSound;
	/// <summary>The body that stopped the ray in the last TraceSegments, if one did.</summary>
	private GameObject _endBody;
	private static readonly List<Vector3> _latticeLines = new();
	private static readonly List<(float distance, GameObject body, Vector3 at)> _legBodies = new();
	private static readonly HashSet<Guid> _legSeen = new();

	/// <summary>Seconds between strike sounds while a ray burns: the length of the clip (laserforreal, 1.29 s), so a burst plays it once.</summary>
	[ConVar( "beam_impact_sound_every" )] public static float ImpactSoundEvery { get; set; } = 1.3f;

	public Vector3 LatticePoint => _points.Count > 0 ? _points[^1] : Origin;
	public CastContext LatticeContext => Context;
	public bool LatticeLive => this.IsValid() && Active && !IsProxy;

	protected override void OnStart()
	{
		_born = Time.Now;
		_aimAtStart = Direction;
		GameObject.Tags.Add( "spellfire" );
		_look = SpellVfx.BuildBeam( GameObject, Spell, Scale );
		// the burn hum runs on every peer for the ray's whole window, positioned at the muzzle
		if ( Spell?.SoundLoop is { } loop ) _loop = VoidMage.Audio.GameAudio.PlayLoop( loop, Origin );
		if ( !IsProxy ) _nextTick = Time.Now + 0.05f;
		if ( !IsProxy && Context?.Flight is { LatticeRange: > 0f } ) LatticeField.Add( this );
	}

	protected override void OnDestroy()
	{
		SpellVfx.Release( _look );
		_loop?.Stop( 0.1f );
		_loop = null;
		LatticeField.Remove( this );
	}

	protected override void OnUpdate()
	{
		if ( !IsProxy && Context is not null && Active ) TickOwner();
		if ( !IsProxy && _endedAt >= 0f && Time.Now > _endedAt + 0.25f ) { GameObject.Destroy(); return; }

		TraceSegments();
		// the spell's sound sits halfway along the ray (user, 2026-09-22: at the strike point it read as coming from
		// wherever the wall was, not from the beam), again and again while it burns; every peer plays its own
		var soundAt = MidPoint();
		if ( Active && _points.Count >= 2 && Time.Now >= _nextImpactSound && Spell?.SoundImpact is { } strikeSound )
		{
			_nextImpactSound = Time.Now + ImpactSoundEvery;
			VoidMage.Audio.GameAudio.Play( strikeSound, soundAt );
		}
		SpellVfx.Drive( _look, _points, Active, Time.Now - _born, WindowSeconds );
		if ( _loop is not null ) _loop.Position = soundAt;
	}

	/// <summary>The point half the ray's length along its path (through every bend), or the muzzle before it has one.</summary>
	private Vector3 MidPoint()
	{
		if ( _points.Count < 2 ) return _points.Count > 0 ? _points[0] : Origin;
		var total = 0f;
		for ( var i = 1; i < _points.Count; i++ ) total += _points[i - 1].Distance( _points[i] );
		var half = total * 0.5f;
		for ( var i = 1; i < _points.Count; i++ )
		{
			var seg = _points[i - 1].Distance( _points[i] );
			if ( half <= seg ) return seg <= 0f ? _points[i] : Vector3.Lerp( _points[i - 1], _points[i], half / seg );
			half -= seg;
		}
		return _points[^1];
	}

	// ------------------------------------------------------------------ owner

	private void TickOwner()
	{
		var ctx = Context;
		var spell = Spell;
		var f = ctx.Flight;

		// where the ray leaves and where it points this frame
		var caster = ctx.Caster;
		var root = ctx.CasterRoot;
		var controller = root.IsValid() ? root.Components.Get<PlayerController>() : null;
		var eye = controller.IsValid() ? controller.EyePosition : WorldPosition;
		Vector3 direction;
		if ( !Steers )
		{
			// a free copy holds its line (and, thrown from a wisp or the ceiling, its origin)
			if ( !FixedOrigin ) Origin = caster.IsValid() ? caster.StaffTip() ?? Origin : Origin;
			direction = _aimAtStart;
		}
		else
		{
			var aim = AimSolver.Solve( Scene, root, eye, caster?.StaffTip(), spell.BeamRadius, ctx.Caster?.AimDistance ?? 4000f );
			Origin = aim.Origin;
			direction = ProjectileEmitter.FanDirection( aim.Direction, ForkIndex, Math.Max( 1, ctx.Count ), ctx.FanDegrees );
		}
		if ( f.Orbits && !FixedOrigin && Time.Now - _born < f.OrbitSeconds )
		{
			// The Orbit: the ray sweeps around the caster (odd forks the other way), then snaps back to the crosshair
			var sign = (ForkIndex & 1) == 0 ? 1f : -1f;
			_orbitAngle += sign * f.OrbitDegreesPerSecond * Time.Delta;
			var angles = direction.EulerAngles;
			angles.yaw += _orbitAngle;
			angles.pitch = 0f;
			direction = angles.Forward;
			Origin = eye - Vector3.Up * 10f;
		}
		// The Hound: the ray leans toward the hostile nearest the line
		var hound = ctx.Stack( ModifierId.Hound );
		if ( hound > 0 ) direction = Lean( direction, ModifierTables.HoundSnapDegrees[hound] );
		Direction = direction;
		WorldPosition = Origin;

		if ( Time.Now - _born >= WindowSeconds ) { End(); return; }
		if ( Time.Now < _nextTick ) return;
		// off the last tick's time, not this frame's: late frames never add up and cost the window its fifth tick
		_nextTick = MathF.Max( _nextTick + spell.BeamTickSeconds, Time.Now );

		TraceSegments();
		// a ray never runs ImpactResolver, so the secret wall is opened here, where the beam stops. Owner side only:
		// the TraceSegments() in OnUpdate runs on every peer and would send one host request per client.
		if ( _points.Count >= 2 ) VoidMage.Combat.SecretWallNet.Blast( _points[^1], spell.BeamRadius + 12f );
		// Pulse: the ray's own strikes each pop their number (5 5 5 5 5), a burn or a zone is still summed
		var info = ctx.ToHitInfo().With( SpellHitFlags.Tick | SpellHitFlags.Pulse );
		_tickHit.Clear();
		_ticks++;
		var along = 0f;
		for ( var i = 1; i < _points.Count; i++ )
		{
			var a = _points[i - 1]; var b = _points[i];
			var dir = (b - a).IsNearZeroLength ? Direction : (b - a).Normal;
			foreach ( var hit in Scene.Trace.Ray( a, b ).Radius( spell.BeamRadius ).WithAnyTags( "npc" ).WithoutTags( "ally", "ragdoll" ).RunAll() )
			{
				if ( !hit.Hit || !DamageRouter.IsHostileBody( hit.GameObject, Team.Ally ) || hit.GameObject.Id == IgnoreBodyId || !_tickHit.Add( hit.GameObject.Id ) ) continue;
				// The Horizon: the far end of the ray hits harder
				var scale = ModifierTables.HorizonScale( f.HorizonStacks, along + a.Distance( hit.HitPosition ) );
				SpellNet.Impact( hit.HitPosition, -dir, hit.GameObject, ctx.Damage * scale, dir, root, info, 0f, 0f, 0 );
			}
			along += a.Distance( b );
		}

		// crates, barrels, vases: whatever breakable the ray crosses or stops against is broken by it (user, 2026-09-20).
		// Every other tick, walked in steps along the path: the props are a flat list, not something a trace finds.
		if ( (_ticks & 1) == 1 )
		{
			for ( var i = 1; i < _points.Count; i++ )
			{
				var a = _points[i - 1]; var b = _points[i];
				var length = a.Distance( b );
				var steps = Math.Max( 1, (int)(length / 44f) );
				for ( var k = 0; k <= steps; k++ )
					BreakableNet.Blast( Vector3.Lerp( a, b, k / (float)steps ) + (b - a).Normal * (k == steps ? 10f : 0f), spell.BeamRadius + 14f, localAttacker: true );
			}
		}

		// where the ray ends: The Anvil (and an overcharged ray) bursts there every other tick, for all to see; The
		// Fracture sprays short rays from it every fourth; The Lattice throws lightning from it at whoever stands near
		if ( _points.Count >= 2 )
		{
			var end = _points[^1];
			var back = (_points[^2] - end).IsNearZeroLength ? -Direction : (_points[^2] - end).Normal;
			var impact = ctx.Impact;
			if ( impact.SplashRadius > 0f && (_ticks & 1) == 0 )
			{
				SpellNet.Impact( end, back, null, ctx.Damage * 2f, Vector3.Zero, root, ctx.ToHitInfo().With( SpellHitFlags.Tick | SpellHitFlags.Area ), impact.SplashRadius * 0.6f, 1f, impact.SplashMaxTargets );
				SpellNet.ShowBurst( end + back * 6f, back, spell.Id, 0.4f + impact.SplashRadius / 300f, false );
				BreakableNet.Blast( end, impact.SplashRadius * 0.6f, localAttacker: true );
			}
			if ( impact.FractureShards > 0 && _ticks % 4 == 1 ) FractureShards.Spawn( ctx, end, back, _endBody );
			if ( f.LatticeRange > 0f && (_ticks & 1) == 0 ) LatticeFromEnd( ctx, end + back * 8f, root );
		}

		// Amber: the strike point keeps burning after the ray moved on
		if ( f.Halts && Time.Now >= _nextAmber && _points.Count >= 2 )
		{
			_nextAmber = Time.Now + 0.25f;
			var end = _points[^1];
			SpellNet.AddHazard( HazardKind.Sphere, end, end, 50f, 0f, 0f, f.MineSeconds, null, ctx.Damage * SpellHostTuning.DotHz * 0.5f, spell.SlowFraction, root, ctx.ToHitInfo() );
		}
	}

	/// <summary>The button let go, the pool ran dry, the window closed: the ray stops burning and The Wake keeps its last line.</summary>
	public void End()
	{
		if ( IsProxy || !Active ) return;
		Active = false;
		_endedAt = Time.Now;
		_loop?.Stop( 0.15f );
		_loop = null;
		LatticeField.Remove( this );
		var ctx = Context;
		FireReturn( ctx );
		if ( ctx?.Flight is { Wakes: true } && _points.Count >= 2 )
		{
			var info = ctx.ToHitInfo();
			for ( var i = 1; i < _points.Count; i++ )
				SpellNet.AddHazard( HazardKind.Capsule, _points[i - 1], _points[i], ctx.Flight.WakeWidth * 0.5f, ctx.Flight.WakeWidth, 0f, ctx.Flight.WakeSeconds, null,
					SpellHostTuning.WakeDps( Spell ), SpellHostTuning.WakeSlow( Spell ), ctx.CasterRoot, info );
		}
	}

	/// <summary>
	/// The Lattice on a ray: a lone beam has no second spell to be joined to, so its END throws lightning at the
	/// hostiles standing near it, one line per copy of the scroll, each a Tick hit.
	/// </summary>
	private void LatticeFromEnd( CastContext ctx, Vector3 from, GameObject root )
	{
		var reach = ctx.Flight.LatticeRange;
		var lines = Math.Max( 1, ctx.Stack( ModifierId.Lattice ) );
		var info = ctx.ToHitInfo().With( SpellHitFlags.Tick | SpellHitFlags.Area );
		_latticeLines.Clear();
		foreach ( var npc in CombatTargets.Bodies( Team.Hostile ) )
		{
			if ( _latticeLines.Count >= lines * 2 ) break;
			if ( !npc.CanBeTargeted || _tickHit.Contains( npc.GameObject.Id ) ) continue;
			var chest = npc.AimPoint;
			if ( chest.DistanceSquared( from ) > reach * reach ) continue;
			if ( Scene.Trace.Ray( from, chest ).WithoutTags( SpellArea.LosIgnore ).WithoutTags( "spellfire" ).Run().Hit ) continue;
			SpellNet.Impact( chest, Vector3.Up, npc.GameObject, ctx.Damage * 0.6f, Vector3.Zero, root, info, 0f, 0f, 0 );
			_latticeLines.Add( from ); _latticeLines.Add( chest );
		}
		if ( _latticeLines.Count >= 2 ) SpellNet.LatticeLines( Spell.Id, _latticeLines, Spell.VfxScale * 0.6f );
	}

	/// <summary>The Hound on a ray: turned toward the hostile nearest the line, when one stands within `degrees` of it and in plain sight.</summary>
	private Vector3 Lean( Vector3 direction, float degrees )
	{
		if ( degrees <= 0f ) return direction;
		var spell = Spell;
		var cos = MathF.Cos( degrees.DegreeToRadian() );
		var bestDot = cos;
		Vector3? best = null;
		foreach ( var npc in CombatTargets.Bodies( Team.Hostile ) )
		{
			if ( !npc.CanBeTargeted ) continue;
			var to = npc.AimPoint - Origin;
			var d = to.Length;
			if ( d < 1f || d > spell.BeamRange ) continue;
			var dot = Vector3.Dot( to / d, direction );
			if ( dot <= bestDot ) continue;
			if ( Scene.Trace.Ray( Origin, npc.AimPoint ).WithoutTags( SpellArea.LosIgnore ).WithoutTags( "spellfire" ).Run().Hit ) continue;
			bestDot = dot;
			best = to / d;
		}
		return best ?? direction;
	}

	/// <summary>The Return: the window closed, and one short ray burns back down the last leg of the path.</summary>
	private void FireReturn( CastContext ctx )
	{
		if ( ctx is null || IsReturn || _points.Count < 2 ) return;
		var ret = ctx.Stack( ModifierId.Return );
		if ( ret <= 0 ) return;
		var end = _points[^1]; var prev = _points[^2];
		if ( (prev - end).Length < 20f ) return;
		var back = ctx.Clone();
		back.Flags |= SpellHitFlags.Proc;
		back.Depth = 1;
		back.Origin = end + (prev - end).Normal * 2f;
		back.Direction = (prev - end).Normal;
		back.FixedOrigin = true;
		back.SourceScale = ctx.SourceScale * ModifierTables.ReturnDamage[ret];
		back.SizeScale = ctx.SizeScale;
		back.BeamWindowSeconds = 0.45f;
		back.Count = 1;
		back.FanDegrees = 0f;
		DamageStage.Run( back );
		FlightStage.Run( back );
		ImpactStage.Run( back );
		back.Flight.ReturnTurns = 0;
		back.Flight.OrbitRings = 0;
		BeamEmitter.Emit( back, isReturn: true );
	}

	// ------------------------------------------------------------------ every peer

	/// <summary>
	/// The ray's path: from the origin along the direction, reflected off the world up to Bounces times, ended by the
	/// first hostile body it meets once The Needle's pierces are spent. Every peer runs this for the ribbon (the
	/// bodies are networked, so they agree near enough).
	/// </summary>
	private void TraceSegments()
	{
		var spell = Spell;
		_points.Clear();
		_endBody = null;
		if ( spell is null ) return;
		var bodiesLeft = BodyPierces;
		var from = Origin;
		var dir = Direction;
		var remaining = spell.BeamRange;
		_points.Add( from );
		var walls = WallPierces;
		for ( var leg = 0; leg <= Bounces && remaining > 1f; leg++ )
		{
			var tr = Scene.Trace.Ray( from, from + dir * remaining ).WithoutTags( AimSolver.IgnoreTags ).WithoutTags( "npc", "enemy", "ragdoll" ).Run();
			var end = tr.Hit ? tr.HitPosition : tr.EndPosition;
			// The Needle: through the wall. The ray picks up again where the far side of it is open, a few steps on.
			while ( tr.Hit && walls > 0 && remaining > 1f )
			{
				walls--;
				var resumed = false;
				for ( var hop = 1; hop <= 5 && !resumed; hop++ )
				{
					var probe = end + dir * (40f * hop);
					var left = remaining - from.Distance( probe );
					if ( left <= 1f ) break;
					var next = Scene.Trace.Ray( probe, probe + dir * left ).WithoutTags( AimSolver.IgnoreTags ).WithoutTags( "npc", "enemy", "ragdoll" ).Run();
					if ( next.StartedSolid ) continue;
					tr = next;
					end = next.Hit ? next.HitPosition : next.EndPosition;
					resumed = true;
				}
				if ( !resumed ) break;
			}
			// the first hostile body on this leg that the ray cannot go through ends it there
			if ( bodiesLeft < 99 )
			{
				GameObject stopper = null;
				var stopAt = end;
				var nearest = float.MaxValue;
				_legBodies.Clear();
				foreach ( var hit in Scene.Trace.Ray( from, end ).Radius( spell.BeamRadius ).WithAnyTags( "npc" ).WithoutTags( "ally", "ragdoll" ).RunAll() )
				{
					if ( !hit.Hit || !DamageRouter.IsHostileBody( hit.GameObject, Team.Ally ) || hit.GameObject.Id == IgnoreBodyId ) continue;
					_legBodies.Add( (hit.Distance, hit.GameObject, hit.HitPosition) );
				}
				_legBodies.Sort( ( x, y ) => x.distance.CompareTo( y.distance ) );
				_legSeen.Clear();
				foreach ( var (distance, body, at) in _legBodies )
				{
					if ( !_legSeen.Add( body.Id ) ) continue;   // one body, several shapes
					if ( bodiesLeft > 0 ) { bodiesLeft--; continue; }
					if ( distance < nearest ) { nearest = distance; stopper = body; stopAt = at + dir * 2f; }
					break;
				}
				if ( stopper is not null )
				{
					_endBody = stopper;
					_points.Add( stopAt );
					return;
				}
			}
			_points.Add( end );
			if ( !tr.Hit ) break;
			remaining -= from.Distance( end );
			dir = Vector3.Reflect( dir, tr.Normal );
			from = end + tr.Normal * 0.5f;
		}
	}
}

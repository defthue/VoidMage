using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Npc;

namespace VoidMage.Spells;

/// <summary>
/// The relays between a caster's client and the host (the TrapNet pattern, static RPCs): an impact is ONE request
/// carrying the point, the direct victim and the cast's context, and the host resolves the direct hit, the splash
/// against its own skeleton positions; a hazard (a wake, a zone, a mine) is registered once and every
/// peer keeps the same list; a chain's arcs are broadcast once for the look. The owner keeps simulating its own
/// projectile and never enumerates targets itself.
/// </summary>
public static class SpellNet
{
	// ------------------------------------------------------------------ impact

	/// <summary>
	/// Something landed: the direct victim (may be null or a prop), a splash around `center` (radius 0 = none). Damage is the cast's resolved damage; the splash takes `splashFraction` of it.
	/// </summary>
	public static void Impact( Vector3 center, Vector3 normal, GameObject direct, float damage, Vector3 push, GameObject attacker, in SpellHitInfo info, float splashRadius, float splashFraction, int splashMax )
	{
		if ( Networking.IsHost ) ImpactHost( center, normal, direct, damage, push, attacker, info, splashRadius, splashFraction, splashMax );
		else RequestImpact( center, normal, direct, damage, push, attacker, info.SpellId ?? "", info.Mods, info.Mods2, info.Depth, info.CastId, (int)info.Flags, splashRadius, splashFraction, splashMax );
	}

	[Rpc.Host]
	private static void RequestImpact( Vector3 center, Vector3 normal, GameObject direct, float damage, Vector3 push, GameObject attacker, string spellId, long mods, long mods2, int depth, int castId, int flags, float splashRadius, float splashFraction, int splashMax )
	{
		var info = new SpellHitInfo { SpellId = spellId, Mods = mods, Mods2 = mods2, Depth = depth, CastId = castId, Flags = (SpellHitFlags)flags };
		ImpactHost( center, normal, direct, damage, push, attacker, info, splashRadius, splashFraction, splashMax );
	}

	/// <summary>Host only: the impact resolved against the host's bodies.</summary>
	public static void ImpactHost( Vector3 center, Vector3 normal, GameObject direct, float damage, Vector3 push, GameObject attacker, in SpellHitInfo info, float splashRadius, float splashFraction, int splashMax )
	{
		if ( !Networking.IsHost ) return;
		if ( info.Depth > SpellHostTuning.MaxProcDepth ) return;
		var kind = info.IsSpell ? "spell_" + info.SpellId : "spell";
		var team = CombatTargets.TeamOf( attacker );
		var hitTeam = team == Team.Ally ? Team.Hostile : Team.Ally;

		// the body the bolt touched: a skeleton or a piece of the boss (never a player: a player's own bolts are on its side)
		var directNpc = CombatTargets.CombatantOf( direct );
		if ( directNpc is not null )
		{
			if ( directNpc.IsValid() && directNpc.Team == hitTeam && !directNpc.IsDead )
			{
				directNpc.ApplyHitHost( new DamageRouter.Hit
				{
					Damage = damage,
					Position = center,
					Push = push,
					Attacker = attacker,
					Kind = kind,
					Spell = info,
				} );
			}
			else directNpc = null;
		}

		if ( splashRadius > 0f && splashFraction > 0f )
		{
			var template = new DamageRouter.Hit { Damage = damage * splashFraction, Position = center, Push = push, Attacker = attacker, Kind = kind, Spell = info };
			SpellArea.Damage( center, splashRadius, template, hitTeam, directNpc?.GameObject, falloff: true, needLos: true, maxTargets: splashMax );
		}
	}

	// ------------------------------------------------------------------ hazards

	/// <summary>Registers an element on the ground with every peer; the host ticks it. Returns the key for EndHazard/RemoveHazard.</summary>
	public static Guid AddHazard( HazardKind kind, Vector3 a, Vector3 b, float radius, float width, float speed, float seconds, GameObject follow, float dps, float slow, GameObject attacker, in SpellHitInfo info, float mineDamage = 0f, float mineSplashRadius = 0f, float mineSplashFraction = 0f )
	{
		var key = Guid.NewGuid();
		var now = Time.Now;
		if ( Networking.IsHost )
			HazardRpc( key, (int)kind, a, b, radius, width, speed, now, now + seconds, follow, dps, slow, attacker, info.SpellId ?? "", info.Mods, info.Mods2, info.Depth, info.CastId, (int)info.Flags, mineDamage, mineSplashRadius, mineSplashFraction );
		else
			RequestHazard( key, (int)kind, a, b, radius, width, speed, now, now + seconds, follow, dps, slow, attacker, info.SpellId ?? "", info.Mods, info.Mods2, info.Depth, info.CastId, (int)info.Flags, mineDamage, mineSplashRadius, mineSplashFraction );
		return key;
	}

	[Rpc.Host]
	private static void RequestHazard( Guid key, int kind, Vector3 a, Vector3 b, float radius, float width, float speed, float start, float until, GameObject follow, float dps, float slow, GameObject attacker, string spellId, long mods, long mods2, int depth, int castId, int flags, float mineDamage, float mineSplashRadius, float mineSplashFraction )
	{
		if ( depth > SpellHostTuning.MaxProcDepth ) return;
		HazardRpc( key, kind, a, b, radius, width, speed, start, until, follow, dps, slow, attacker, spellId, mods, mods2, depth, castId, flags, mineDamage, mineSplashRadius, mineSplashFraction );
	}

	[Rpc.Broadcast]
	private static void HazardRpc( Guid key, int kind, Vector3 a, Vector3 b, float radius, float width, float speed, float start, float until, GameObject follow, float dps, float slow, GameObject attacker, string spellId, long mods, long mods2, int depth, int castId, int flags, float mineDamage, float mineSplashRadius, float mineSplashFraction )
	{
		HazardField.Add( new Hazard
		{
			Key = key,
			Kind = (HazardKind)kind,
			A = a,
			B = b,
			Radius = radius,
			Width = width,
			Speed = speed,
			StartTime = start,
			Until = until,
			MaxLength = (b - a).Length,
			Follow = follow,
			Dps = dps,
			Slow = slow,
			Attacker = attacker,
			DamageKind = string.IsNullOrEmpty( spellId ) ? "spell" : "spell_" + spellId,
			Info = new SpellHitInfo { SpellId = spellId, Mods = mods, Mods2 = mods2, Depth = depth, CastId = castId, Flags = (SpellHitFlags)flags },
			MineDamage = mineDamage,
			MineSplashRadius = mineSplashRadius,
			MineSplashFraction = mineSplashFraction,
		} );
	}

	/// <summary>A Ray stops growing at `finalLength` and lives its life out from now.</summary>
	public static void EndHazard( Guid key, float finalLength )
	{
		if ( Networking.IsHost ) EndHazardRpc( key, finalLength );
		else RequestEndHazard( key, finalLength );
	}

	[Rpc.Host] private static void RequestEndHazard( Guid key, float finalLength ) => EndHazardRpc( key, finalLength );
	[Rpc.Broadcast] private static void EndHazardRpc( Guid key, float finalLength ) => HazardField.End( key, finalLength, Time.Now );

	public static void RemoveHazard( Guid key )
	{
		if ( Networking.IsHost ) RemoveHazardRpc( key );
		else RequestRemoveHazard( key );
	}

	[Rpc.Host] private static void RequestRemoveHazard( Guid key ) => RemoveHazardRpc( key );
	[Rpc.Broadcast] private static void RemoveHazardRpc( Guid key ) => HazardField.Remove( key );

	/// <summary>Host: a hostile walked into a mine. The host deals its impact; everyone removes it; the owner's bolt explodes.</summary>
	internal static void FireMine( Hazard h )
	{
		if ( !Networking.IsHost ) return;
		ImpactHost( h.A, Vector3.Up, null, h.MineDamage, Vector3.Zero, h.Attacker, h.Info, h.MineSplashRadius, h.MineSplashFraction, 8 );
		MineTriggeredRpc( h.Key );
	}

	[Rpc.Broadcast]
	private static void MineTriggeredRpc( Guid key )
	{
		var h = HazardField.Get( key );
		if ( h is null ) return;
		var follow = h.Follow;
		HazardField.Remove( key );
		if ( follow.IsValid() && !follow.IsProxy ) follow.Components.Get<SpellProjectile>()?.OnMineTriggered();
	}

	// ------------------------------------------------------------------ the 2026-09-20 scrolls (look only, and the harvest's mote)

	/// <summary>The Lattice's lines this tick, as point PAIRS, on every peer. One unreliable message five times a second.</summary>
	public static void LatticeLines( string spellId, IReadOnlyList<Vector3> pairs, float scale )
	{
		if ( pairs is null || pairs.Count < 2 ) return;
		LatticeLinesRpc( spellId, Encode( pairs ), scale );
	}

	[Rpc.Broadcast( NetFlags.Unreliable )]
	private static void LatticeLinesRpc( string spellId, string pairs, float scale )
	{
		var spell = SpellDatabase.Get( spellId );
		var list = Decode( pairs );
		if ( spell is null ) return;
		var line = new List<Vector3>( 2 ) { default, default };
		for ( var i = 0; i + 1 < list.Count; i += 2 )
		{
			line[0] = list[i]; line[1] = list[i + 1];
			StyleVfx.SpawnArc( Game.ActiveScene, line, spell.Palette, scale, ModifierTables.LatticeTickSeconds + 0.05f );
		}
	}

	/// <summary>Host: a spell's burst somewhere no bolt landed (a pyre, a fuse going off, a body thrown into a wall).</summary>
	[Rpc.Broadcast]
	public static void ShowBurst( Vector3 position, Vector3 normal, string spellId, float scale, bool body )
	{
		var spell = SpellDatabase.Get( spellId );
		if ( spell is null ) return;
		SpellVfx.Burst( Game.ActiveScene, position, normal, spell, spell.VfxScale * scale, body ? ImpactKind.Body : ImpactKind.World );
	}

	/// <summary>Host: The Fuse lodged in a body. A small core of the spell rides its chest until it goes off.</summary>
	[Rpc.Broadcast]
	public static void ShowFuse( GameObject body, string spellId, float seconds )
	{
		var spell = SpellDatabase.Get( spellId );
		if ( spell is null || !body.IsValid() ) return;
		var rig = FireballVfx.BuildCore( body, "fuse", false, spell.Palette );
		if ( rig?.Root is null ) return;
		rig.Root.LocalPosition = Vector3.Up * 44f;
		FireballVfx.SetScale( rig, 0.22f );
		FireballVfx.SetSpread( rig, 0.22f );
		rig.Root.Components.Create<VfxLifetime>().Seconds = seconds;
	}

	/// <summary>Host: The Harvest. A mote leaves the corpse for the caster on every peer; the caster's own peer banks the mana when it arrives.</summary>
	[Rpc.Broadcast]
	public static void ManaMote( Vector3 from, GameObject caster, float mana )
	{
		if ( !caster.IsValid() ) return;
		var go = new GameObject( true, "mana mote" ) { WorldPosition = from };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var mote = go.Components.Create<ManaMote>();
		mote.Target = caster;
		mote.Mana = mana;
	}

	// ------------------------------------------------------------------ chain arcs (look only)

	/// <summary>Every peer draws the arc through these points. Points travel as text: an RPC takes plain values.</summary>
	public static void ChainArc( string spellId, IReadOnlyList<Vector3> points, float scale )
	{
		if ( points is null || points.Count < 2 ) return;
		ChainArcRpc( spellId, Encode( points ), scale );
	}

	[Rpc.Broadcast]
	private static void ChainArcRpc( string spellId, string points, float scale )
	{
		var spell = SpellDatabase.Get( spellId );
		var list = Decode( points );
		if ( spell is null || list.Count < 2 ) return;
		SpellVfx.ChainArc( Game.ActiveScene, spell, list, scale );
	}

	private static string Encode( IReadOnlyList<Vector3> points )
	{
		var sb = new System.Text.StringBuilder();
		for ( var i = 0; i < points.Count; i++ )
		{
			if ( i > 0 ) sb.Append( ';' );
			var p = points[i];
			sb.Append( p.x.ToString( "0.#", CultureInfo.InvariantCulture ) ).Append( ',' )
			  .Append( p.y.ToString( "0.#", CultureInfo.InvariantCulture ) ).Append( ',' )
			  .Append( p.z.ToString( "0.#", CultureInfo.InvariantCulture ) );
		}
		return sb.ToString();
	}

	private static List<Vector3> Decode( string text )
	{
		var list = new List<Vector3>();
		foreach ( var part in (text ?? "").Split( ';', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var c = part.Split( ',' );
			if ( c.Length != 3 ) continue;
			if ( float.TryParse( c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x )
				&& float.TryParse( c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y )
				&& float.TryParse( c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z ) )
				list.Add( new Vector3( x, y, z ) );
		}
		return list;
	}
}

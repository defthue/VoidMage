using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Npc;

/// <summary>
/// What a spell left on a skeleton, host only: a slow (the strongest fraction wins, the duration refreshes) and
/// burns (one per kind, ticking damage at <see cref="Spells.SpellHostTuning.DotHz"/> through ApplyHitHost with the
/// Tick flag so they never stagger and their numbers are summed). A burn remembers who lit it, so a kill by the
/// burn still runs the death stage for the caster.
/// </summary>
public sealed class SkeletonStatus
{
	private sealed class BurnEntry
	{
		public float Dps, Until, NextTick;
		public string Kind;
		public GameObject Attacker;
		public SpellHitInfo Info;
	}

	private float _slowFraction, _slowUntil = float.NegativeInfinity;
	private readonly Dictionary<string, BurnEntry> _burns = new();

	public float SlowFraction => Time.Now < _slowUntil ? _slowFraction : 0f;
	public float SpeedScale => 1f - SlowFraction;
	public bool Burning => _burns.Count > 0;
	/// <summary>0 none, 1 fire, 2 void (the body's tint).</summary>
	public int BurnKind
	{
		get
		{
			foreach ( var b in _burns.Values )
				return b.Kind.Contains( "void", StringComparison.Ordinal ) ? 2 : 1;
			return 0;
		}
	}

	public void Slow( float fraction, float seconds )
	{
		if ( fraction <= 0f || seconds <= 0f ) return;
		if ( Time.Now >= _slowUntil || fraction >= _slowFraction ) _slowFraction = Math.Clamp( fraction, 0f, 0.9f );
		_slowUntil = MathF.Max( _slowUntil, Time.Now + seconds );
	}

	public void Burn( float dps, float seconds, string kind, GameObject attacker, in SpellHitInfo info )
	{
		if ( dps <= 0f || seconds <= 0f ) return;
		if ( !_burns.TryGetValue( kind, out var b ) )
		{
			b = new BurnEntry { Kind = kind, NextTick = Time.Now + 1f / Spells.SpellHostTuning.DotHz };
			_burns[kind] = b;
		}
		b.Dps = MathF.Max( b.Dps, dps );
		b.Until = MathF.Max( b.Until, Time.Now + seconds );
		b.Attacker = attacker;
		b.Info = info;
	}

	public void Clear()
	{
		_slowUntil = float.NegativeInfinity;
		_burns.Clear();
	}

	private static readonly List<string> _expired = new();
	private static readonly List<(string kind, BurnEntry b)> _ticking = new();

	/// <summary>Host, per fixed tick: every burn ticks on its own clock.</summary>
	public void Tick( SkeletonNpc npc, float now )
	{
		if ( _burns.Count == 0 ) return;
		_expired.Clear();
		var hz = MathF.Max( 1f, Spells.SpellHostTuning.DotHz );
		// a snapshot: a burn tick that kills clears the bag from under the loop
		_ticking.Clear();
		foreach ( var (kind, b) in _burns ) _ticking.Add( (kind, b) );
		foreach ( var (kind, b) in _ticking )
		{
			if ( now > b.Until ) { _expired.Add( kind ); continue; }
			if ( now < b.NextTick ) continue;
			b.NextTick = now + 1f / hz;
			if ( npc.IsDead ) continue;
			npc.ApplyHitHost( new DamageRouter.Hit
			{
				Damage = b.Dps / hz,
				Position = npc.WorldPosition + Vector3.Up * 50f,
				Push = Vector3.Zero,
				Attacker = b.Attacker,
				Kind = b.Kind,
				Spell = b.Info.With( SpellHitFlags.Tick ),
			} );
		}
		foreach ( var kind in _expired ) _burns.Remove( kind );
	}
}

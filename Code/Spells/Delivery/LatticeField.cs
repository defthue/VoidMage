using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Combat;

namespace VoidMage.Spells;

/// <summary>Something of the caster's that The Lattice may join to another: a bolt in flight, a mine, a ring bolt, the end of a beam.</summary>
public interface ILatticeNode
{
	Vector3 LatticePoint { get; }
	CastContext LatticeContext { get; }
	bool LatticeLive { get; }
}

/// <summary>
/// The Lattice, owner side: five times a second every node is joined to the nearest other node inside both their
/// reaches (each pair once, the shortest lines first, capped), every hostile body on a line takes a Tick hit through
/// the usual one-request impact, and the lines go out to every peer in ONE unreliable broadcast for the look. The
/// host never hears about nodes; nothing here is networked state.
/// </summary>
public static class LatticeField
{
	private static readonly List<ILatticeNode> _nodes = new();
	private static readonly List<(int a, int b, float d2)> _pairs = new();
	private static readonly List<Vector3> _points = new();
	private static readonly HashSet<Guid> _hitThisTick = new();
	private static float _nextTick;

	public static void Add( ILatticeNode node )
	{
		if ( node is not null && !_nodes.Contains( node ) ) _nodes.Add( node );
	}

	public static void Remove( ILatticeNode node ) => _nodes.Remove( node );

	public static void Tick( SpellCaster caster )
	{
		if ( _nodes.Count == 0 || Time.Now < _nextTick ) return;
		_nextTick = Time.Now + ModifierTables.LatticeTickSeconds;
		_nodes.RemoveAll( n => n is null || !n.LatticeLive || n.LatticeContext is null );
		if ( _nodes.Count < 2 || !caster.IsValid() ) return;

		_pairs.Clear();
		for ( var i = 0; i < _nodes.Count; i++ )
		{
			for ( var j = i + 1; j < _nodes.Count; j++ )
			{
				var reach = MathF.Min( _nodes[i].LatticeContext.Flight?.LatticeRange ?? 0f, _nodes[j].LatticeContext.Flight?.LatticeRange ?? 0f );
				if ( reach <= 0f ) continue;
				var d2 = _nodes[i].LatticePoint.DistanceSquared( _nodes[j].LatticePoint );
				if ( d2 > reach * reach || d2 < 16f * 16f ) continue;
				_pairs.Add( (i, j, d2) );
			}
		}
		if ( _pairs.Count == 0 ) return;
		_pairs.Sort( ( x, y ) => x.d2.CompareTo( y.d2 ) );

		var scene = caster.Scene;
		var root = caster.PlayerRoot;
		var links = Math.Min( _pairs.Count, ModifierTables.LatticeMaxLinks );
		_points.Clear();
		_hitThisTick.Clear();
		SpellDefinition look = null;
		for ( var k = 0; k < links; k++ )
		{
			var a = _nodes[_pairs[k].a]; var b = _nodes[_pairs[k].b];
			var from = a.LatticePoint; var to = b.LatticePoint;
			// a line through a wall is no line
			if ( scene.Trace.Ray( from, to ).WithoutTags( SpellArea.LosIgnore ).WithoutTags( "spellfire" ).Run().Hit ) continue;
			_points.Add( from ); _points.Add( to );
			look ??= a.LatticeContext.Spell;

			var damage = (a.LatticeContext.Damage + b.LatticeContext.Damage) * 0.5f * ModifierTables.LatticeDamageFraction;
			var info = a.LatticeContext.ToHitInfo().With( SpellHitFlags.Tick );
			var dir = (to - from).Normal;
			foreach ( var hit in scene.Trace.Ray( from, to ).Radius( 10f ).WithAnyTags( "npc" ).WithoutTags( "ally", "ragdoll" ).RunAll() )
			{
				if ( !hit.Hit || !DamageRouter.IsHostileBody( hit.GameObject, Team.Ally ) || !_hitThisTick.Add( hit.GameObject.Id ) ) continue;
				SpellNet.Impact( hit.HitPosition, -dir, hit.GameObject, damage, Vector3.Zero, root, info, 0f, 0f, 0 );
			}
		}
		if ( _points.Count >= 2 && look is not null ) SpellNet.LatticeLines( look.Id, _points, look.VfxScale * 0.6f );
	}
}

using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Character;

namespace VoidMage.Boss;

/// <summary>
/// The Colossus's decision table: which move at which range, how often, and what follows what. A Souls grammar
/// rather than a roulette: range bands (close, mid, far), a cooldown per move, never the same move twice running,
/// phase 1 is the hands' fight (plus repositions, the rain and the summon), phase 2 lets the skull itself join; authored CHAINS give the strings
/// the player learns (the one-two, the greed punish); the grab only ever comes as the hug punish or up close.
/// Repositions are moves too: the whole formation flies to a flank or behind the player, then attacks from there.
/// </summary>
public sealed class BossDecider
{
	public enum Band { Close, Mid, Far, Any }

	/// <summary>One line of the table. `Bearing`/`Distance` aim a reposition relative to the current orbit bearing.</summary>
	public readonly record struct Entry( string Move, Band Band, int MinPhase, float Cooldown, float Weight, float Bearing = 0f, float Distance = 0f );

	/// <summary>A chain: after `After` ends, `Then` follows `Gap` seconds later (`Overlap`: it may start while the first still rests), with this chance, from this phase; `CloseOnly` needs the player within the close band.</summary>
	public readonly record struct Link( string After, string Then, float Gap, int MinPhase, float Chance, bool Overlap = false, bool CloseOnly = false, bool OtherHand = false, float Speed = 1f );

	public const float CloseRange = 700f;
	public const float MidRange = 1400f;
	private const float ChainCooldown = 6f;

	private static readonly Entry[] Table =
	{
		new( "slam", Band.Close, 1, 4f, 3f ), new( "hammer", Band.Close, 1, 5f, 1.5f ),
		new( "sweep", Band.Mid, 1, 2.5f, 3f ), new( "palm", Band.Mid, 1, 2.5f, 3f ),
		new( "clap", Band.Close, 1, 9f, 2f ), new( "march", Band.Mid, 1, 9f, 2f ),
		new( "rake", Band.Far, 1, 7f, 2f ),
		new( "grab", Band.Close, 1, 10f, 0.6f ),
		new( "reposition", Band.Any, 1, 6f, 1.5f, Bearing: 90f, Distance: 900f ),
		new( "reposition", Band.Close, 1, 8f, 1f, Bearing: 180f, Distance: 1300f ),
		// the skull's ranged moves are in from the start: the rain from mid range or further, the summon anywhere
		// (weighted heavily: at 1.5 it went unrolled through a whole fight)
		new( "rain", Band.Far, 1, 12f, 3f ), new( "rain", Band.Mid, 1, 12f, 1.5f ),
		new( "summon", Band.Any, 1, 30f, 4f ),
		new( "bite", Band.Mid, 2, 8f, 3f ), new( "ram", Band.Far, 2, 14f, 3f ),
		new( "shake", Band.Close, 2, 12f, 2f ),
	};

	private static readonly Link[] Chains =
	{
		new( "sweep", "palm", 0.25f, 1, 0.6f, Overlap: true, Speed: 1.25f ),
		new( "sweep", "hammer", 0.3f, 1, 0.5f, Speed: 1.25f ),
		new( "palm", "slam", 0.3f, 1, 0.5f, Speed: 1.25f ),
		new( "slam", "slam", 0.2f, 1, 0.5f, Overlap: true, OtherHand: true, Speed: 1.1f ),
		new( "march", "clap", 0.4f, 1, 0.5f ),
		new( "palm", "clap", 0.4f, 1, 0.4f, CloseOnly: true ),
		new( "rake", "reposition", 0.3f, 1, 0.4f ),
		new( "reposition", "ram", 0.2f, 2, 0.7f ),
		new( "bite", "shake", 0.2f, 2, 0.6f, Speed: 1.2f ),
		new( "ram", "rain", 0.3f, 2, 0.5f ),
	};

	private readonly Dictionary<string, float> _readyAt = new();
	private readonly Dictionary<string, float> _chainReadyAt = new();
	private string _last = "";
	private BossPartKind _lastHand = BossPartKind.HandR;
	private int _lastReposition = -1;

	public void Reset()
	{
		_readyAt.Clear();
		_chainReadyAt.Clear();
		_last = "";
	}

	/// <summary>Is the move off cooldown?</summary>
	public bool Ready( string move, float now ) => !_readyAt.TryGetValue( move, out var at ) || now >= at;

	/// <summary>The brain began a move: its cooldown starts, it becomes the last move.</summary>
	public void Began( string move, float now )
	{
		var cd = 0f;
		foreach ( var e in Table ) if ( e.Move == move ) cd = MathF.Max( cd, e.Cooldown );
		_readyAt[move] = now + cd;
		_last = move;
	}

	public static Band BandOf( float dist ) => dist < CloseRange ? Band.Close : dist < MidRange ? Band.Mid : Band.Far;

	/// <summary>A fresh pick from neutral for the situation, or null when nothing fits (the brain waits a beat).</summary>
	public BossBrain.Queued? Pick( BossBrain brain, PlayerHealth player, float dist )
	{
		var now = Time.Now;
		var band = BandOf( dist );
		var options = new List<(Entry e, BossPartKind part, float w)>();
		for ( var i = 0; i < Table.Length; i++ )
		{
			var e = Table[i];
			if ( e.Band != Band.Any && e.Band != band ) continue;
			if ( brain.Fight < e.MinPhase || !Ready( e.Move, now ) ) continue;
			if ( e.Move == _last && !(e.Move == "reposition" && i != _lastReposition) ) continue;
			var attack = BossAttacks.Find( e.Move );
			if ( attack is null ) continue;
			foreach ( var part in PartsFor( brain, attack ) ) options.Add( (e, part, e.Weight * (attack.UsesHand && !attack.UsesBothHands && part == _lastHand ? 0.5f : 1f)) );
		}
		if ( options.Count == 0 )
		{
			// nothing fits: step to a flank (its cooldown ignored) so the fight never stalls
			var repo = BossAttacks.Find( "reposition" );
			return repo is null || _last == "reposition" ? null : Queue( brain, repo, BossPartKind.Skull, 1f, player, Table[8], 0f, false );
		}
		var total = 0f;
		foreach ( var o in options ) total += o.w;
		var roll = Game.Random.Float( 0f, total );
		var chosen = options[^1];
		foreach ( var o in options )
		{
			roll -= o.w;
			if ( roll <= 0f ) { chosen = o; break; }
		}
		if ( chosen.e.Move == "reposition" ) _lastReposition = Array.IndexOf( Table, chosen.e );
		return Queue( brain, BossAttacks.Find( chosen.e.Move ), chosen.part, 1f, player, chosen.e, 0f, false );
	}

	/// <summary>After `finished` ended: the chain's next link, if one rolls and fits, else null.</summary>
	public BossBrain.Queued? Chain( BossBrain brain, string finished, BossPartKind finishedPart, PlayerHealth player, float dist )
	{
		var now = Time.Now;
		foreach ( var c in Chains )
		{
			if ( c.After != finished || brain.Fight < c.MinPhase ) continue;
			var key = c.After + ">" + c.Then;
			if ( _chainReadyAt.TryGetValue( key, out var at ) && now < at ) continue;
			if ( c.CloseOnly && dist >= CloseRange ) continue;
			if ( Game.Random.Float() > c.Chance ) continue;
			var attack = BossAttacks.Find( c.Then );
			if ( attack is null ) continue;
			BossPartKind part;
			if ( attack.UsesBothHands ) part = BossPartKind.HandL;
			else if ( !attack.UsesHand ) part = BossPartKind.Skull;
			else if ( c.OtherHand ) part = finishedPart == BossPartKind.HandL ? BossPartKind.HandR : BossPartKind.HandL;
			else if ( attack.OnlyHand is { } only ) part = only;
			else part = finishedPart;
			if ( !Usable( brain, attack, part, c.Overlap ) ) continue;
			_chainReadyAt[key] = now + ChainCooldown;
			Entry entry = default;
			if ( attack is RepositionAttack ) entry = Table[8];
			return Queue( brain, attack, part, c.Speed, player, entry, c.Gap, c.Overlap );
		}
		return null;
	}

	private BossBrain.Queued Queue( BossBrain brain, BossAttack attack, BossPartKind part, float speed, PlayerHealth player, Entry entry, float gap, bool overlap )
	{
		Vector3? aux = null;
		if ( attack is RepositionAttack )
		{
			// a flank or the back, relative to where the skull stands now around the player; alternate the flank's side
			var side = Game.Random.Float() < 0.5f ? 1f : -1f;
			var rel = entry.Bearing == 0f ? 90f : entry.Bearing;
			var distance = entry.Distance == 0f ? 900f : entry.Distance;
			var feet = player.FeetPosition;
			aux = brain.ClampToArena( feet + Rotation.FromYaw( brain.Bearing + rel * side ).Forward * distance, 1300f );
		}
		if ( attack.UsesHand && !attack.UsesBothHands ) _lastHand = part;
		return new BossBrain.Queued { Attack = attack, Part = part, Speed = speed, Aux = aux, At = Time.Now + gap, Overlap = overlap };
	}

	/// <summary>The parts this move can use right now (alive, not staggered, not busy).</summary>
	private static IEnumerable<BossPartKind> PartsFor( BossBrain brain, BossAttack attack )
	{
		if ( attack.UsesBothHands )
		{
			if ( Usable( brain, attack, BossPartKind.HandL, false ) ) yield return BossPartKind.HandL;
			yield break;
		}
		if ( !attack.UsesHand )
		{
			if ( Usable( brain, attack, BossPartKind.Skull, false ) ) yield return BossPartKind.Skull;
			yield break;
		}
		foreach ( var kind in new[] { BossPartKind.HandL, BossPartKind.HandR } )
		{
			if ( attack.OnlyHand is { } only && only != kind ) continue;
			if ( Usable( brain, attack, kind, false ) ) yield return kind;
		}
	}

	private static bool Usable( BossBrain brain, BossAttack attack, BossPartKind part, bool overlap )
	{
		if ( attack.UsesBothHands )
		{
			var l = brain.HandL;
			var r = brain.HandR;
			return l is { Dead: false } && r is { Dead: false } && !brain.Busy( BossPartKind.HandL ) && !brain.Busy( BossPartKind.HandR );
		}
		var p = BossNet.Find( part );
		if ( !p.IsValid() || p.Dead ) return false;
		return !brain.Busy( part );
	}
}

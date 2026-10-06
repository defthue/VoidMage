using System;

namespace VoidMage.Spells;

/// <summary>
/// How many of each modifier a staff carries, packed two bits per <see cref="ModifierId"/> (0..3 copies) into two
/// longs (31 modifiers each: the pool outgrew one on 2026-09-20), so the whole loadout rides two [Sync] fields, two RPC
/// arguments or a hit struct. Three copies is the cap (the stacking tables stop there), which is exactly what two bits hold.
/// </summary>
public readonly struct ModifierStacks : IEquatable<ModifierStacks>
{
	/// <summary>Modifiers 1..31.</summary>
	public long Lo { get; }
	/// <summary>Modifiers 32..62.</summary>
	public long Hi { get; }

	public const int MaxCopies = 3;
	private const int PerLong = 31;

	public ModifierStacks( long lo, long hi ) { Lo = lo; Hi = hi; }

	public static readonly ModifierStacks Empty = new( 0, 0 );

	/// <summary>Copies of `id` on the staff, 0..3.</summary>
	public int this[ModifierId id]
	{
		get
		{
			if ( id == ModifierId.None ) return 0;
			var i = (int)id - 1;
			return i < PerLong ? (int)((Lo >> (i * 2)) & 3L) : (int)((Hi >> ((i - PerLong) * 2)) & 3L);
		}
	}

	public bool Has( ModifierId id ) => this[id] > 0;
	public bool IsEmpty => Lo == 0 && Hi == 0;

	/// <summary>Scrolls in a row (None skipped); duplicates count up, capped at three copies.</summary>
	public static ModifierStacks From( params ModifierId[] slots )
	{
		var s = Empty;
		foreach ( var id in slots ) s = s.Add( id );
		return s;
	}

	/// <summary>One more copy of `id` (capped at 3).</summary>
	public ModifierStacks Add( ModifierId id )
	{
		if ( id == ModifierId.None ) return this;
		var n = Math.Min( MaxCopies, this[id] + 1 );
		return With( id, n );
	}

	/// <summary>A copy with `id` set to exactly `count` copies.</summary>
	public ModifierStacks With( ModifierId id, int count )
	{
		if ( id == ModifierId.None ) return this;
		var i = (int)id - 1;
		var v = (long)Math.Clamp( count, 0, MaxCopies );
		if ( i < PerLong )
		{
			var shift = i * 2;
			return new ModifierStacks( (Lo & ~(3L << shift)) | (v << shift), Hi );
		}
		else
		{
			var shift = (i - PerLong) * 2;
			return new ModifierStacks( Lo, (Hi & ~(3L << shift)) | (v << shift) );
		}
	}

	/// <summary>Every copy on the staff, all scrolls together.</summary>
	public int Total
	{
		get
		{
			var n = 0;
			foreach ( ModifierId id in Enum.GetValues( typeof( ModifierId ) ) ) n += this[id];
			return n;
		}
	}

	public bool Equals( ModifierStacks other ) => Lo == other.Lo && Hi == other.Hi;
	public override bool Equals( object obj ) => obj is ModifierStacks o && Equals( o );
	public override int GetHashCode() => HashCode.Combine( Lo, Hi );
	public static bool operator ==( ModifierStacks a, ModifierStacks b ) => a.Equals( b );
	public static bool operator !=( ModifierStacks a, ModifierStacks b ) => !a.Equals( b );

	public override string ToString()
	{
		if ( IsEmpty ) return "none";
		var parts = new System.Collections.Generic.List<string>();
		foreach ( ModifierId id in Enum.GetValues( typeof( ModifierId ) ) )
		{
			var n = this[id];
			if ( n > 0 ) parts.Add( n > 1 ? $"{id} x{n}" : id.ToString() );
		}
		return string.Join( ", ", parts );
	}
}

using System;
using System.Collections.Generic;

namespace VoidMage.Dungeon;

/// <summary>
/// Seeded RNG with the handful of Python-random helpers the generator was written against.
/// Every consumer feeds it deterministic input (sorted lists, never hash-set order), so the
/// same seed produces the same dungeon on every machine — the host can send just the seed.
/// </summary>
public sealed class DungeonRng
{
	private readonly Random _r;

	public DungeonRng( int seed ) { _r = new Random( seed ); }

	/// <summary>
	/// Deterministic hash of two ints for deriving sub-stream seeds (decoration per tier, etc.).
	/// HashCode.Combine is randomised per process, so it is never used here.
	/// </summary>
	public static int Mix( int a, int b )
	{
		unchecked
		{
			uint h = 2166136261;
			h = (h ^ (uint)a) * 16777619;
			h = (h ^ (uint)b) * 16777619;
			h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
			return (int)h;
		}
	}

	/// <summary>[0, 1)</summary>
	public double NextDouble() => _r.NextDouble();

	/// <summary>Inclusive on both ends, like Python's randint.</summary>
	public int RandInt( int a, int b ) => _r.Next( a, b + 1 );

	/// <summary>[0, n)</summary>
	public int RandRange( int n ) => _r.Next( n );

	public T Choice<T>( IReadOnlyList<T> list ) => list[_r.Next( list.Count )];

	public void Shuffle<T>( IList<T> list )
	{
		for ( int i = list.Count - 1; i > 0; i-- )
		{
			int j = _r.Next( i + 1 );
			(list[i], list[j]) = (list[j], list[i]);
		}
	}

	/// <summary>Weighted pick, like Python's random.choices(k=1).</summary>
	public T Choices<T>( IReadOnlyList<T> items, IReadOnlyList<double> weights )
	{
		double total = 0;
		for ( int i = 0; i < weights.Count; i++ ) total += weights[i];
		double r = _r.NextDouble() * total;
		for ( int i = 0; i < items.Count; i++ )
		{
			r -= weights[i];
			if ( r < 0 ) return items[i];
		}
		return items[items.Count - 1];
	}
}

using System;

namespace VoidMage.Run;

/// <summary>Easing curves for the portal and boon timelines (t in 0..1, clamped).</summary>
public static class Ease
{
	public static float Clamp01( float t ) => t < 0f ? 0f : t > 1f ? 1f : t;

	/// <summary>0..1 progress of `now` through a window starting at `start` lasting `duration`.</summary>
	public static float Window( float now, float start, float duration ) => duration <= 0f ? (now >= start ? 1f : 0f) : Clamp01( (now - start) / duration );

	public static float InQuad( float t ) { t = Clamp01( t ); return t * t; }
	public static float OutQuad( float t ) { t = Clamp01( t ); return 1f - (1f - t) * (1f - t); }
	public static float InCubic( float t ) { t = Clamp01( t ); return t * t * t; }
	public static float OutCubic( float t ) { t = Clamp01( t ); var u = 1f - t; return 1f - u * u * u; }

	public static float InOutCubic( float t )
	{
		t = Clamp01( t );
		return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow( -2f * t + 2f, 3f ) / 2f;
	}

	/// <summary>Overshoots past 1 and settles back: a card landing.</summary>
	public static float OutBack( float t, float overshoot = 1.4f )
	{
		t = Clamp01( t );
		var u = t - 1f;
		return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
	}

	public static float Lerp( float a, float b, float t ) => a + (b - a) * t;
}

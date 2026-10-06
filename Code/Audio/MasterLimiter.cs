using System;
using Sandbox;
using Sandbox.Audio;

namespace VoidMage.Audio;

/// <summary>
/// A limiter on the master mixer. The engine sums every voice and then clamps the samples to [-1, 1]
/// (MixingThread: HardLimit), so a dozen explosions at once is a square wave. This rides the level
/// instead: a peak follower turns the whole mix down the moment it passes the threshold and lets it
/// back up slowly, and whatever the attack still lets through is rounded off rather than chopped.
/// Runs on the mixing thread; every knob is a float that is only read there.
/// </summary>
public sealed class MasterLimiter : AudioProcessor<MasterLimiter.State>
{
	/// <summary>The engine mixes at a fixed 44.1 kHz (AudioEngine.SamplingRate, internal).</summary>
	private const float SampleRate = 44100f;

	public class State : ListenerState
	{
		internal PerChannel<float> Envelope;
	}

	/// <summary>Level (0..1) the mix is held under. Below it nothing is touched.</summary>
	public float Threshold { get; set; } = 0.7f;
	/// <summary>Seconds to clamp down on a peak. Short, or the first crack of a burst still clips; not so short that one stray sample slams the whole mix.</summary>
	public float Attack { get; set; } = 0.003f;
	/// <summary>Seconds to let go again. Long enough that the gain does not wobble inside one explosion, short enough that the mix is back before anyone hears it gone.</summary>
	public float Release { get; set; } = 0.15f;
	/// <summary>Nothing leaves louder than this, whatever comes in.</summary>
	public float Ceiling { get; set; } = 0.95f;
	/// <summary>
	/// The deepest the whole mix is ever turned down (0.35 = -9 dB). Past that the shoulder rounds the peak off
	/// instead. Unbounded, a burst summing to 3 or 4 pulled everything to a fifth for the half second the release
	/// took to let go: heard as every sound dropping out and swelling back, the "stutter" players reported (2026-09-26).
	/// </summary>
	public float Floor { get; set; } = 0.35f;

	/// <summary>The deepest cut of the last buffer, 1 = untouched. For the debug readout only.</summary>
	public float LastGain { get; private set; } = 1f;

	private float _deepest = 1f;

	/// <summary>The deepest cut since the last call (1 = untouched); resets. Written on the mixing thread, read on the main thread: a float, torn at worst.</summary>
	public float ReadDeepest()
	{
		var d = _deepest;
		_deepest = 1f;
		return d;
	}

	protected override void ProcessSingleChannel( AudioChannel channel, Span<float> input )
	{
		var threshold = Math.Clamp( Threshold, 0.05f, 1f );
		var ceiling = Math.Clamp( Ceiling, threshold, 1f );
		var floor = Math.Clamp( Floor, 0.05f, 1f );
		var attack = 1f - MathF.Exp( -1f / (MathF.Max( Attack, 0.0001f ) * SampleRate) );
		var release = 1f - MathF.Exp( -1f / (MathF.Max( Release, 0.001f ) * SampleRate) );
		// the soft shoulder: straight up to the knee, then a tanh that lands on the ceiling
		var knee = threshold + (ceiling - threshold) * 0.5f;
		var room = MathF.Max( ceiling - knee, 0.0001f );

		var env = CurrentState.Envelope.Get( channel );
		if ( !float.IsFinite( env ) ) env = 0f;
		var deepest = 1f;

		for ( var i = 0; i < input.Length; i++ )
		{
			var x = input[i];
			if ( !float.IsFinite( x ) ) x = 0f;
			var level = MathF.Abs( x );
			env += (level - env) * (level > env ? attack : release);

			var gain = env > threshold ? MathF.Max( threshold / env, floor ) : 1f;
			if ( gain < deepest ) deepest = gain;

			var y = x * gain;
			var mag = MathF.Abs( y );
			if ( mag > knee ) y = MathF.Sign( y ) * (knee + room * MathF.Tanh( (mag - knee) / room ));
			input[i] = y;
		}

		CurrentState.Envelope.Set( channel, env );
		LastGain = deepest;
		if ( deepest < _deepest ) _deepest = deepest;
	}
}

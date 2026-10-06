using System;
using System.Collections.Generic;
using Sandbox;

namespace VoidMage.Dungeon;

/// <summary>
/// Marks a PointLight as a fire and describes its flame. FireLightSystem drives every one of these
/// each frame and parks a sprite flame rig (FireVfxRig) on the nearest ones. One flicker signal
/// feeds everything: the light's colour and falloff, and the flame's brightness, lean, height and
/// licks, so light and fire breathe and gust together.
///
/// The light itself never moves and never changes radius. The engine's shadow mapper caches the
/// static geometry of a shadowed light and only re-renders all six cube faces when the light's
/// transform version changes; the cube far plane is the radius, so a radius change would stale the
/// cache too. Flicker therefore goes through colour (brightness) and quadratic attenuation (the
/// falloff shape, which is what makes the toon bands crawl), both free for the shadow cache. The
/// wander the flame leans toward is computed but only ever applied to the flame.
/// Base values are set by the builder and live in properties so a code hotload keeps them.
/// </summary>
public enum FireShadowMode
{
	/// <summary>Competes for the shadow budget by apparent size.</summary>
	Auto,
	/// <summary>Never casts shadows.</summary>
	Off,
	/// <summary>Always casts shadows while it is awake and within shadow range.</summary>
	On,
}

[Title( "Fire Light" ), Category( "Void Mage" ), Icon( "local_fire_department" )]
public sealed class FireLight : Component
{
	/// <summary>0 = steady, 1 = raging.</summary>
	[Property, Range( 0f, 1f )] public float Amount { get; set; } = 0.35f;
	/// <summary>
	/// Whether this light casts shadows. FireLightSystem owns the PointLight's Shadows checkbox and rewrites it
	/// every frame, so set it here (in the editor, ticking the light's own checkbox sets this to On or Off for you). Auto = competes for the shadow budget (firelight_shadow_max), Off = never,
	/// On = always first in line for a slot.
	/// </summary>
	[Property, Group( "Light Flicker" )] public FireShadowMode CastShadows { get; set; } = FireShadowMode.Auto;
	/// <summary>How hard the LIGHT flickers, independent of the flame. 1 = as before, 0 = a steady light, 2 = twice as strong.</summary>
	[Property, Group( "Light Flicker" ), Range( 0f, 2f )] public float LightFlicker { get; set; } = 1f;
	/// <summary>Smooths out the fast jitter of the light's flicker, leaving slow breathing. 0 = raw, 1 = very calm.</summary>
	[Property, Group( "Light Flicker" ), Range( 0f, 1f )] public float LightSmoothing { get; set; }
	[Property] public float Phase { get; set; }
	/// <summary>The light's authored colour; the flicker multiplies this, never the current colour.</summary>
	[Property] public Color BaseColor { get; set; } = Color.Transparent;
	[Property] public float BaseRadius { get; set; }
	/// <summary>The light's authored quadratic attenuation; the flicker breathes the falloff around it.</summary>
	[Property] public float BaseAttenuation { get; set; }
	/// <summary>Where the light sits. It stays put (see the class note); the flame's lean wanders instead.</summary>
	[Property] public Vector3 BasePosition { get; set; }
	[Property] public bool BaseSet { get; set; }

	[Property, Group( "Flame" )] public LightKind Kind { get; set; } = LightKind.Torch;
	/// <summary>1 = wall torch, candles about 0.45, braziers about 2.</summary>
	[Property, Group( "Flame" )] public float FlameScale { get; set; } = 1f;
	/// <summary>Flame base relative to the light, in the prop's frame (+X into the room, +Z up).</summary>
	[Property, Group( "Flame" )] public Vector3 FlameOffset { get; set; }
	/// <summary>Wall torches: a warm patch on the wall behind, and the flame leans off the wall.</summary>
	[Property, Group( "Flame" )] public bool WallGlow { get; set; }
	/// <summary>
	/// Wall torches: the room-side face of the wall (or pillar) the prop hangs on, as X from the prop's pivot in
	/// the prop's frame (+X into the room). The builder sets it from the model bounds and the embed depth; the
	/// wall glow sprite is parked just in front of it. 0 = the face is at the pivot.
	/// </summary>
	[Property, Group( "Flame" )] public float WallFaceX { get; set; }
	[Property, Group( "Flame" )] public bool Smoke { get; set; }
	[Property, Group( "Flame" )] public bool Sparks { get; set; }
	/// <summary>Taller or shorter flame: multiplies how fast the flame rises off the wick.</summary>
	[Property, Group( "Flame" ), Range( 0.2f, 3f )] public float FlameHeight { get; set; } = 1f;
	/// <summary>Wider or narrower flame: multiplies how far the flame spreads and wanders sideways.</summary>
	[Property, Group( "Flame" ), Range( 0.2f, 3f )] public float FlameWidth { get; set; } = 1f;

	/// <summary>More or fewer wisps in the column (Smoke must be on).</summary>
	[Property, Group( "Smoke" ), Range( 0.2f, 3f )] public float SmokeDensity { get; set; } = 1f;
	/// <summary>How tall the column gets before it has faded: multiplies how long each wisp lives.</summary>
	[Property, Group( "Smoke" ), Range( 0.2f, 3f )] public float SmokeHeight { get; set; } = 1f;
	/// <summary>How fast the smoke rises.</summary>
	[Property, Group( "Smoke" ), Range( 0.2f, 3f )] public float SmokeSpeed { get; set; } = 1f;
	/// <summary>Wisp size: small at the fire, swelling as it rises.</summary>
	[Property, Group( "Smoke" ), Range( 0.2f, 3f )] public float SmokeSize { get; set; } = 1f;
	/// <summary>How thick the smoke reads. 1 is a thin campfire haze.</summary>
	[Property, Group( "Smoke" ), Range( 0f, 4f )] public float SmokeOpacity { get; set; } = 1f;
	/// <summary>How far the column wanders and widens on the way up. 0 = a straight pillar.</summary>
	[Property, Group( "Smoke" ), Range( 0f, 3f )] public float SmokeSpread { get; set; } = 1f;
	/// <summary>How high above the flame base the smoke starts, in flame scales (it comes off the top of the fire, not the logs).</summary>
	[Property, Group( "Smoke" ), Range( 0f, 40f )] public float SmokeStart { get; set; } = 8f;
	/// <summary>Clear = dark warm grey off the fire cooling to a lighter grey. Set it to colour the smoke yourself.</summary>
	[Property, Group( "Smoke" )] public Color SmokeColor { get; set; } = Color.Transparent;

	/// <summary>How hard a nearby portal (RunPortal.SuctionRange) drags this fire's flame, smoke and sparks into itself. 0 = ignore portals.</summary>
	[Property, Group( "Portal" ), Range( 0f, 3f )] public float PortalPull { get; set; } = 1f;

	/// <summary>How many flame tongues, spread on a ring of TongueRadius around the flame base. Any kind.</summary>
	[Property, Group( "Tongues" ), Range( 1, 12 )] public int Tongues { get; set; } = 1;
	/// <summary>Ring radius of the tongues, inches. 0 with one tongue = a single flame on the base.</summary>
	[Property, Group( "Tongues" )] public float TongueRadius { get; set; }
	/// <summary>Explicit tongue positions relative to the flame base (one per wick). When set, Tongues and TongueRadius are ignored.</summary>
	[Property, Group( "Tongues" )] public List<Vector3> TongueOffsets { get; set; }

	/// <summary>Per-fire randomness so neighbouring fires never flicker in step. 0 = derived from the object's id (unique per placed copy).</summary>
	[Property, Group( "Variation" )] public int Seed { get; set; }
	/// <summary>How much each fire's flicker speed differs from the others (0.25 = up to 25 % faster or slower).</summary>
	[Property, Group( "Variation" ), Range( 0f, 0.6f )] public float TempoVariation { get; set; } = 0.25f;
	/// <summary>Turns each fire's wander in a random direction, so the flames don't all lean the same way.</summary>
	[Property, Group( "Variation" )] public bool RandomLeanDirection { get; set; } = true;
	/// <summary>Clear takes the flame colour from the light; set it to break the two apart.</summary>
	[Property, Group( "Flame" )] public Color Tint { get; set; } = Color.Transparent;
	/// <summary>The Synty flame card of a candle, driven overbright in time with the flame.</summary>
	[Property, Group( "Flame" )] public ModelRenderer FlameCard { get; set; }

	/// <summary>This frame's flicker, roughly -0.19..0.19.</summary>
	public float Flicker { get; private set; }
	/// <summary>This frame's lean of the flame in the prop's frame (x forward, y left), inches of force.</summary>
	public Vector2 Lean { get; private set; }
	/// <summary>This frame's rise force.</summary>
	public float Rise { get; private set; }
	/// <summary>True on the frames a flicker spike counts as a gust: licks and sparks fire.</summary>
	public bool Gust => Flicker > 0.06f;
	/// <summary>
	/// This frame's wander of the fire in the prop's frame: where the light WOULD have drifted with the
	/// lean. The light stays put (shadow cache); the flame's tip follows half of this.
	/// </summary>
	public Vector3 Displacement { get; private set; }

	/// <summary>The rig FireLightSystem parked here, if any (runtime only).</summary>
	public FireVfxRig.Rig Rig;

	/// <summary>The Shadows value FireLightSystem last wrote to the light in the editor; a different value since is the user's tick.</summary>
	internal bool? ShadowsWritten;

	private PointLight _light;

	// per-instance variation, derived from Seed (or the object id) and cached until those inputs change
	private int _varKey = int.MinValue;
	private float _lastApply, _lightFlick;
	private float _varPhase, _varTempo = 1f, _varLeanCos = 1f, _varLeanSin;

	/// <summary>This fire's own clock offset (seconds), so the flame particles of two fires never share a turbulence pattern.</summary>
	public float TimeOffset { get; private set; }
	/// <summary>This frame's time on this fire's own clock (offset and tempo applied); the flame rig runs on it.</summary>
	public float LocalTime { get; private set; }

	private void UpdateVariation()
	{
		int seed = Seed != 0 ? Seed : GameObject.Id.GetHashCode();
		int key = HashCode.Combine( seed, TempoVariation, RandomLeanDirection );
		if ( key == _varKey ) return;
		_varKey = key;
		var rng = new System.Random( seed );
		_varPhase = rng.NextSingle() * 10f;
		_varTempo = 1f + (rng.NextSingle() * 2f - 1f) * TempoVariation;
		float angle = RandomLeanDirection ? rng.NextSingle() * MathF.PI * 2f : 0f;
		_varLeanCos = MathF.Cos( angle );
		_varLeanSin = MathF.Sin( angle );
		TimeOffset = rng.NextSingle() * 500f;
	}

	/// <summary>
	/// Everything the flame rig is built from. FireLightSystem rebuilds the rig whenever this changes, so
	/// editing a flame setting in the inspector shows up at once.
	/// </summary>
	public int BuildKey
	{
		get
		{
			var h = new HashCode();
			h.Add( Kind ); h.Add( FlameScale ); h.Add( Amount ); h.Add( WallGlow ); h.Add( WallFaceX );
			h.Add( Smoke ); h.Add( Sparks ); h.Add( Tongues ); h.Add( TongueRadius ); h.Add( Tint );
			h.Add( BaseColor ); h.Add( FlameWidth );
			h.Add( SmokeDensity ); h.Add( SmokeHeight ); h.Add( SmokeSpeed ); h.Add( SmokeSize );
			h.Add( SmokeOpacity ); h.Add( SmokeSpread ); h.Add( SmokeStart ); h.Add( SmokeColor );
			if ( TongueOffsets != null ) foreach ( var o in TongueOffsets ) h.Add( o );
			return h.ToHashCode();
		}
	}

	/// <summary>The driven point light, or null when the object has none.</summary>
	public PointLight Light
	{
		get
		{
			if ( !_light.IsValid() ) _light = Components.Get<PointLight>( true );
			return _light;
		}
	}

	/// <summary>The flame colour: the light's colour normalised out of HDR, or the override.</summary>
	public Color FlameTint
	{
		get
		{
			if ( Tint.a > 0.01f ) return Tint;
			var c = BaseColor;
			float max = MathF.Max( c.r, MathF.Max( c.g, c.b ) );
			if ( max < 0.01f ) return new Color( 1f, 0.55f, 0.2f );
			return new Color( c.r / max, c.g / max, c.b / max );
		}
	}

	/// <summary>One frame: compute the flicker and breathe the light with it. Returns false when there is no light.</summary>
	public bool Apply( float t )
	{
		var light = Light;
		if ( light == null ) return false;
		if ( BaseColor.a <= 0f ) BaseColor = light.LightColor;
		if ( BaseRadius <= 0f ) BaseRadius = light.Radius;
		if ( BaseAttenuation <= 0f ) BaseAttenuation = light.Attenuation > 0f ? light.Attenuation : 1f;
		if ( !BaseSet ) { BasePosition = LocalPosition; BaseSet = true; }

		// each fire runs on its own phase and tempo, so a row of identical prefabs never flickers in step
		UpdateVariation();
		float p = Phase + _varPhase;
		t = t * _varTempo + TimeOffset;
		LocalTime = t;
		float f = MathF.Sin( t * 23f + p * 9f ) * 0.08f + MathF.Sin( t * 7.3f + p * 5f ) * 0.06f + MathF.Sin( t * 1.7f + p ) * 0.05f;
		Flicker = f;

		// a lazy breeze at rest, a whip on every spike, turned to this fire's own direction; wall torches lean off the wall
		float s = FlameScale;
		float lat0 = (MathF.Sin( t * 1.3f + p ) * 0.6f + f * 4f) * 9f * s;
		float fwd0 = MathF.Sin( t * 0.9f + p * 2f ) * 6f * s;
		float lateral = fwd0 * _varLeanSin + lat0 * _varLeanCos;
		float forward = fwd0 * _varLeanCos - lat0 * _varLeanSin + (WallGlow ? 6f * s : 0f);
		Lean = new Vector2( forward, lateral );
		Rise = (40f + 130f * Amount) * s * (1f + f * Amount * 3f);
		Displacement = new Vector3( forward, lateral, 0f ) * 0.3f;

		// A disabled light (gated out by FireLightSystem) only needs the flame-side values above.
		if ( !light.Enabled ) return true;

		// the light's own flicker: smoothed (up to a 0.35 s low-pass) and scaled, the flame keeps the raw signal
		float dt = _lastApply > 0f ? Math.Clamp( t - _lastApply, 0f, 0.1f ) : 0f;
		_lastApply = t;
		float tau = LightSmoothing * 0.35f;
		_lightFlick = tau <= 0.001f || dt <= 0f ? f : MathX.Lerp( _lightFlick, f, 1f - MathF.Exp( -dt / tau ) );
		float lf = _lightFlick * LightFlicker;
		float k = 1f + lf * Amount * 3f;
		light.LightColor = new Color( BaseColor.r * k, BaseColor.g * k, BaseColor.b * k, 1f );
		// Radius and position stay fixed (shadow cache). The falloff breathes instead: a lower
		// quadratic term reaches further, so the toon bands still crawl in time with the flame.
		light.Radius = BaseRadius;
		light.Attenuation = BaseAttenuation / (1f + lf * Amount * 1.2f);
		return true;
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = new Color( 1f, 0.5f, 0.1f, 0.8f );
		Gizmo.Draw.LineSphere( new Sphere( FlameOffset, 6f * FlameScale ) );
	}
}

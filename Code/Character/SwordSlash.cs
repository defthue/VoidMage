using System;
using System.Collections.Generic;
using System.Text;
using Sandbox;

namespace VoidMage.Character;

/// <summary>
/// The sword's slash effect, the Fell &amp; Sell touch: an authored crescent that plays in view space at
/// the moment of each attack clip's cut, like most games do, rather than a trail glued to the blade
/// (the pack's hand keys alternate fast and slow frames, so a faithful trail is a jittery scribble).
///
/// Each clip's crescent is three eye-relative points (start, middle, end of the cut, in inches: x forward,
/// y left, z up) measured from the clip by tools/measure_sword_tip.py: only the frames where the tip is
/// fast and in front of the eye, the windup behind the head left out. The circle through the three points
/// is the swing's pivot and reach; the crescent is a thin camera-facing stroke along that circle (a line
/// renderer's construction, so it is the same thickness on screen for every swing) with a thinner second
/// line just inside it for depth, both flat and crisp, thin at the ends and full in the middle. It reveals
/// from start to end over the cut (eased, fast first) and is erased start-to-end right after: a flash, not a
/// lingering mark. It rides the eye transform every frame, so it turns with the view like a view model;
/// peers see it on the body's eye.
///
/// Timing runs on the synced clip start time (ClipPoseSource.StartTime), never on the hidden renderer's
/// playback clock: that renderer switches clips in its own update, so on the frame a swing starts the
/// playback time can still be the previous clip's and a heavy would fire during its windup.
///
/// Drawn straight from a vertex list by a SceneCustomObject with Graphics.Draw and
/// materials/slash_ribbon.vmat, which paints the band's cross profile. One log line per swing names the
/// clip, its combo slot and its crescent frames, so a swing can be pointed at and its row tuned. With
/// DebugArc on, the real blade tip's eye-space path is recorded during the swing, drawn next to the arc
/// and logged per frame, which is how "does it line up" is measured.
/// </summary>
[Title( "Sword Slash" ), Category( "Void Mage" ), Icon( "flare" )]
public sealed class SwordSlash : Component
{
	/// <summary>
	/// One clip's crescent. Frames are clip frames at 30 fps (the effect fires at StartFrame; the reveal
	/// takes (EndFrame - StartFrame) frames, never less than MinSweep). Points are eye-relative inches
	/// (x forward, y left, z up). Width is the band's radial thickness (0 = DefaultWidth); Sweep/Hold/Fade
	/// override the component's timing when above 0.
	/// </summary>
	public sealed record SlashProfile( int StartFrame, int EndFrame, Vector3 Start, Vector3 Mid, Vector3 End,
		float Width = 0f, float Sweep = 0f, float Hold = 0f, float Fade = 0f, float Alpha = 1f, Color? Tint = null );

	/// <summary>
	/// Per-clip crescents from tools/measure_sword_tip.py (2026-09-06, fixed model axes; the comments list
	/// every in-front frame the tool saw). Tune by clip name. Two rows are hand-shaped: swing_down measured
	/// as a short straight drop, so it starts higher; heavy_swing_left loops up-left before the cut, so it
	/// starts at the top of that loop.
	/// </summary>
	public static readonly Dictionary<string, SlashProfile> Profiles = new()
	{
		// Widths vary a little per attack for detail: 3.5 is the ceiling for lights (the two big level
		// sweeps), the chops and short diagonals run thinner; heavies top out at 5 and follow their speed.
		// light 1: right to left, level, just under the eye.  f4(3,-29,-9) f5(30,-15,-6) f6(35,-9,-6) f7(42,16,-8)
		["anim_attack_swing_right_01"] = new( 4, 7, new( 3f, -29f, -9f ), new( 30f, -15f, -6f ), new( 42f, 16f, -8f ), Width: 2.8f ),
		// light 2: left to right, level.  f4(35,32,-4) f5(44,10,-2) f6(46,2,-3) f7(39,-27,-2)
		["anim_attack_swing_left_01"] = new( 4, 7, new( 35f, 32f, -4f ), new( 46f, 2f, -4f ), new( 39f, -27f, -2f ), Width: 2.6f ),
		// light 3: overhead chop, top to bottom.  f5(40,4,11) f6(41,5,6) f7(49,4,-16) (start raised by hand)
		["anim_attack_swing_down"] = new( 4, 7, new( 28f, 6f, 26f ), new( 46f, 4f, 2f ), new( 49f, 4f, -18f ), Width: 2.2f ),
		// light 4: short diagonal, right-high to left-low.  f5(24,-13,3) f6(38,-2,-3) f7(33,13,-12) (start pushed right)
		["anim_attack_swing_right_02"] = new( 5, 7, new( 18f, -22f, 6f ), new( 38f, -2f, -3f ), new( 33f, 15f, -13f ), Width: 2.1f ),
		// light 5: diagonal, left-high to right-low.  f4(20,35,13) f5(43,12,4) f6(47,4,-3) f7(40,-23,-13)
		["anim_attack_swing_left_02"] = new( 4, 7, new( 20f, 35f, 13f ), new( 43f, 12f, 4f ), new( 40f, -23f, -14f ), Width: 2.4f ),
		// light 6: rising diagonal, right-low to left-high.  f4(4,-20,-26) f5(32,-9,-20) f6(38,-1,-14) f7(40,23,0)
		["anim_attack_swing_right_03"] = new( 4, 7, new( 4f, -20f, -26f ), new( 32f, -9f, -20f ), new( 40f, 23f, 0f ), Width: 2.6f ),
		// heavy 1: diagonal, right-high to left-low.  f18(19,-22,9) f19(33,-12,6) f20(39,1,-2) f21(36,16,-12) f22(32,26,-21)
		["anim_attack_heavy_swing_right"] = new( 18, 22, new( 19f, -22f, 9f ), new( 39f, 1f, -2f ), new( 32f, 26f, -21f ), Width: 3.4f ),
		// heavy 2: loops up-left, then cuts left-high to right-low.  f16(4,4,21) f17(7,29,18) f18(25,24,14) f19(46,11,1) f20(49,-3,-7) f21(44,-20,-17) f22(26,-34,-21)
		["anim_attack_heavy_swing_left"] = new( 18, 22, new( 25f, 24f, 14f ), new( 49f, -3f, -7f ), new( 26f, -34f, -21f ), Width: 3.7f ),
		// heavy 3: overhead chop, top to bottom, bulging forward.  f18(31,7,19) f19(41,4,8) f20(49,3,-14) f21(45,4,-21) f22(32,-1,-44)
		["anim_attack_heavy_swing_down"] = new( 18, 22, new( 31f, 7f, 19f ), new( 49f, 3f, -14f ), new( 32f, -1f, -44f ), Width: 4f ),
	};

	// The stroke is camera-facing along the arc (a line renderer's construction), so its thickness is the
	// same on screen for every swing: a band lying in the swing's own plane looked 34 units thick on a
	// cut whose plane happened to face the eye and paper-thin on one seen edge-on.

	/// <summary>On-screen thickness of the main stroke (units at the arc) when the profile does not say.</summary>
	[Property, Group( "Look" )] public float DefaultWidth { get; set; } = 2.8f;
	/// <summary>Stroke thickness at the two ends as a fraction of the middle (the crescent's points).</summary>
	[Property, Group( "Look" )] public float EndTaper { get; set; } = 0.15f;
	/// <summary>How much thinner the stroke is at its end than at its start (0 = symmetric): the cut trails off.</summary>
	[Property, Group( "Look" )] public float EndThinning { get; set; } = 0.3f;
	/// <summary>Pushes the stroke out from the measured tip circle (units; the blade path itself is 0).</summary>
	[Property, Group( "Look" )] public float RadialOffset { get; set; } = 3f;
	/// <summary>Extend the arc past each measured end by this fraction of the sweep.</summary>
	[Property, Group( "Look" )] public float Overshoot { get; set; } = 0.12f;
	/// <summary>Rows along the arc.</summary>
	[Property, Group( "Look" )] public int Segments { get; set; } = 40;
	[Property, Group( "Look" )] public Color DefaultTint { get; set; } = Color.White;
	/// <summary>Opacity multiplier on top of the profile's.</summary>
	[Property, Group( "Look" )] public float Alpha { get; set; } = 1f;
	/// <summary>Draw the owner's crescent in the view model layer with the arms (never clipped by walls).</summary>
	[Property, Group( "Look" )] public bool ViewModelLayer { get; set; } = true;

	/// <summary>A second, thinner line running just inside the main stroke: the depth the reference slashes have.</summary>
	[Property, Group( "Second line" )] public bool SecondLine { get; set; } = true;
	/// <summary>How far inside the main stroke (toward the swing's pivot) the second line runs, in units.</summary>
	[Property, Group( "Second line" )] public float SecondOffset { get; set; } = 3.6f;
	/// <summary>Second line thickness as a fraction of the main stroke's.</summary>
	[Property, Group( "Second line" )] public float SecondWidth { get; set; } = 0.45f;
	[Property, Group( "Second line" )] public float SecondAlpha { get; set; } = 0.7f;
	/// <summary>The second line covers this fraction of the arc, centred (shorter than the main stroke).</summary>
	[Property, Group( "Second line" )] public float SecondSpan { get; set; } = 0.8f;
	/// <summary>Seconds the second line trails the main stroke's reveal.</summary>
	[Property, Group( "Second line" )] public float SecondDelay { get; set; } = 0.02f;

	/// <summary>Seconds for the reveal to cross the arc, at least (a three-frame cut would be 0.1 s).</summary>
	[Property, Group( "Timing" )] public float MinSweep { get; set; } = 0.08f;
	/// <summary>Seconds for the reveal to cross the arc, at most (the slow heavies still flash).</summary>
	[Property, Group( "Timing" )] public float MaxSweep { get; set; } = 0.11f;
	/// <summary>Seconds the full crescent holds before dissolving.</summary>
	[Property, Group( "Timing" )] public float Hold { get; set; } = 0f;
	/// <summary>Seconds of the dissolve (erases start-to-end, fading only at the very end).</summary>
	[Property, Group( "Timing" )] public float Fade { get; set; } = 0.08f;
	/// <summary>Softness of the erasing tail edge, as a fraction of the arc.</summary>
	[Property, Group( "Timing" )] public float TailSoftness { get; set; } = 0.06f;
	/// <summary>Softness of the revealing head edge, as a fraction of the arc.</summary>
	[Property, Group( "Timing" )] public float HeadSoftness { get; set; } = 0.03f;
	/// <summary>Added to the clip frame before the start test (the export/playback one-frame ambiguity).</summary>
	[Property, Group( "Timing" )] public int FrameOffset { get; set; }

	/// <summary>Draws the fitted arc, records the real blade tip's path during the swing and logs it per frame.</summary>
	[Property, Group( "Debug" )] public bool DebugArc { get; set; }
	/// <summary>One line per swing (owner only): clip, combo slot, crescent frames.</summary>
	[Property, Group( "Debug" )] public bool LogSwings { get; set; } = false;

	public const string MaterialPath = "materials/slash_ribbon.vmat";
	public const float ClipFps = 30f;

	/// <summary>
	/// The direction the blade travels during a clip's cut, from its measured crescent (start to end of the cut):
	/// in eye space (x forward, y left, z up) and in the world for the given eye rotation. The one truth about
	/// which way a swing goes, shared by the sparks, the shove and the camera sway. False for clips without a row.
	/// </summary>
	public static bool TryBladeDirection( string clip, Rotation eye, out Vector3 world, out Vector3 eyeSpace )
	{
		world = default; eyeSpace = default;
		if ( string.IsNullOrEmpty( clip ) || !Profiles.TryGetValue( clip, out var p ) ) return false;
		var d = p.End - p.Start;
		if ( d.LengthSquared < 1e-4f ) return false;
		eyeSpace = d.Normal;
		world = (eye.Forward * eyeSpace.x + eye.Left * eyeSpace.y + eye.Up * eyeSpace.z).Normal;
		return true;
	}
	private const float BladeLength = 28f;

	/// <summary>Owns nothing: hands rendering back to the component so one vertex list serves every pass.</summary>
	private sealed class SlashSceneObject : SceneCustomObject
	{
		private readonly SwordSlash _owner;

		public SlashSceneObject( SceneWorld world, SwordSlash owner ) : base( world )
		{
			_owner = owner;
			// Opaque on purpose: a translucent object's depth never reaches the buffer the retro filter's
			// depth fog samples (the stroke was fogged as the far wall behind it, while the opaque arms and
			// blade stay clear). As an opaque object in the view model layer it draws with the arms, over the
			// finished world, still alpha-blended by its shader, and writes real depth like the blade does.
			Flags.IsOpaque = true;
			Flags.IsTranslucent = false;
			Flags.WantsPrePass = true;
			Flags.CastShadows = false;
		}

		public override void RenderSceneObject() => _owner.Render( this );
	}

	private ClipPoseSource _source;
	private FirstPersonArms _arms;
	private PlayerController _controller;
	private SlashSceneObject _so;
	private Material _material;
	// Built in OnPreRender, read in RenderSceneObject. The list is the only source of truth for the
	// vertex count: Graphics.Draw passes the count straight to native code with no bounds check, so a
	// separate count field that could ever disagree with it is an access violation waiting to happen.
	private readonly List<Vertex> _verts = new();

	// the swing
	private int _seenVersion = -1;
	private string _clip;
	private SlashProfile _profile;
	private bool _armed;      // a profiled attack clip is playing and has not fired yet

	// the crescent in flight (view space)
	private bool _active;
	/// <summary>The profile the crescent in flight was fired from: it outlives `_profile`, which a clip change (a jump replacing the attack clip mid-swing) clears while the stroke still holds and fades.</summary>
	private SlashProfile _fired;
	private float _t0;
	private float _sweepSeconds, _holdSeconds, _fadeSeconds;
	private Vector3 _center, _u, _v;
	private float _radius, _sweep;   // sweep: signed radians from Start (angle 0) to End through Mid
	private bool _firstPerson;

	// debug: the real tip's eye-space path over the swing
	private readonly List<(int Frame, Vector3 Local)> _tipPath = new();
	private bool _tipLogged;
	private float _tipShowUntil;

	protected override void OnEnabled()
	{
		_material = Material.Load( MaterialPath );
		EnsureSceneObject();
	}

	protected override void OnDisabled()
	{
		_so?.Delete();
		_so = null;
		_verts.Clear();
		_active = false;
		_armed = false;
	}

	private void EnsureSceneObject()
	{
		if ( _so.IsValid() ) return;
		if ( !Scene.IsValid() || Scene.SceneWorld is null ) return;
		_so = new SlashSceneObject( Scene.SceneWorld, this );
		_so.Transform = global::Transform.Zero;
		// No depth feather: the scene depth texture is not the finished one during the opaque pass.
		_so.Attributes.Set( "g_DepthFeather", 0f );
	}

	protected override void OnUpdate()
	{
		if ( !_source.IsValid() ) _source = ClipPoseSource.Find( this );
		if ( !_arms.IsValid() ) _arms = Components.Get<FirstPersonArms>( FindMode.EverythingInSelfAndAncestors );
		if ( !_controller.IsValid() ) _controller = Components.Get<PlayerController>( FindMode.EverythingInSelfAndAncestors );
		EnsureSceneObject();

		if ( _source.IsValid() && _source.Version != _seenVersion )
		{
			_seenVersion = _source.Version;
			OnClipChanged( _source.Clip ?? "" );
		}

		var frame = _profile is not null ? (int)MathF.Floor( ClipTime() * ClipFps ) + FrameOffset : -1;

		if ( _armed && _profile is not null && frame >= _profile.StartFrame ) Fire();

		if ( _active && Time.Now - _t0 >= _sweepSeconds + _holdSeconds + _fadeSeconds )
		{
			_active = false;
			_verts.Clear();
		}

		if ( DebugArc ) DebugUpdate( frame );
	}

	/// <summary>
	/// The crescent is built here rather than in RenderSceneObject. OnPreRender runs on the main
	/// thread after the camera is set up and the body has posed, so the stroke still hangs on this
	/// frame's eye, but the render callback now only ever reads a finished list. Building inside the
	/// callback meant clearing and refilling the shared vertex list while the object was being drawn,
	/// and WantsPrePass has it visited by more than one pass per frame.
	/// </summary>
	protected override void OnPreRender() => Build();

	// ------------------------------------------------------------------ the swing

	private void OnClipChanged( string clip )
	{
		_armed = false;
		if ( !clip.StartsWith( "anim_attack_", StringComparison.Ordinal ) )
		{
			_clip = null;
			_profile = null;
			return;
		}

		_clip = clip;
		Profiles.TryGetValue( clip, out _profile );
		_armed = _profile is not null;
		_tipPath.Clear();
		_tipLogged = false;

		if ( IsProxy || !LogSwings ) return;
		var slot = FirstPersonArms.TryDescribeAttack( clip, out var set, out var index, out var count ) ? $"{set} {index + 1}/{count}" : "not in a combo";
		if ( _profile is null )
			Log.Info( $"[SwordSlash] {clip} ({slot}) no crescent profile" );
		else
			Log.Info( $"[SwordSlash] {clip} ({slot}) crescent f{_profile.StartFrame}-{_profile.EndFrame} at {_profile.StartFrame / ClipFps:0.00}s sweep {SweepSeconds( _profile ):0.00}s width {WidthOf( _profile ):0}" );
	}

	/// <summary>Seconds into the clip on the synced clock, the one the whoosh uses too.</summary>
	private float ClipTime() => _source.IsValid() ? _source.Elapsed : 0f;

	private float SweepSeconds( SlashProfile p ) => p.Sweep > 0f ? p.Sweep : Math.Clamp( (p.EndFrame - p.StartFrame) / ClipFps, MinSweep, MathF.Max( MinSweep, MaxSweep ) );
	private float WidthOf( SlashProfile p ) => p.Width > 0f ? p.Width : DefaultWidth;

	/// <summary>The cut begins: fit the circle through the three points and start the reveal.</summary>
	private void Fire()
	{
		_armed = false;
		var p = _profile;
		if ( p is null ) return;

		var a = p.Start;
		var b = p.Mid;
		var c = p.End;
		var ab = b - a;
		var ac = c - a;
		var n = Vector3.Cross( ab, ac );
		if ( n.LengthSquared < 1e-4f )
		{
			// three points on a line: bow the middle a hair so a circle exists (a huge, flat one)
			var side = Vector3.Cross( ac, Vector3.Forward );
			if ( side.LengthSquared < 1e-4f ) side = Vector3.Cross( ac, Vector3.Up );
			b += side.Normal * 0.5f;
			ab = b - a;
			n = Vector3.Cross( ab, ac );
		}

		var n2 = n.LengthSquared;
		_center = a + (Vector3.Cross( n, ab ) * ac.LengthSquared + Vector3.Cross( ac, n ) * ab.LengthSquared) / (2f * n2);
		_radius = (a - _center).Length;
		_u = (a - _center) / _radius;
		var normal = n.Normal;
		_v = Vector3.Cross( normal, _u );

		var thetaB = AngleOf( b );
		var thetaC = AngleOf( c );
		if ( thetaB >= 0f )
			_sweep = thetaC >= thetaB ? thetaC : thetaC + MathF.PI * 2f;
		else
			_sweep = thetaC <= thetaB ? thetaC : thetaC - MathF.PI * 2f;

		// the crescent traces the cut in the clip's own time: a faster swing (attack speed) sweeps faster
		_sweepSeconds = SweepSeconds( p ) / MathF.Max( 0.05f, _source.IsValid() ? _source.Rate : 1f );
		_holdSeconds = p.Hold > 0f ? p.Hold : Hold;
		_fadeSeconds = p.Fade > 0f ? p.Fade : Fade;
		_t0 = Time.Now;
		_fired = p;
		_active = true;
		_firstPerson = !IsProxy && _arms.IsValid() && _arms.RigVisible;
		if ( _so.IsValid() ) _so.Flags.ViewModelLayer = ViewModelLayer && _firstPerson;
	}

	private float AngleOf( Vector3 p )
	{
		var d = p - _center;
		return MathF.Atan2( Vector3.Dot( d, _v ), Vector3.Dot( d, _u ) );
	}

	/// <summary>A point on the arc, in view space: f along the sweep (0 start .. 1 end), radial offset from the measured tip circle.</summary>
	private Vector3 ArcPoint( float f, float radialOffset )
	{
		var angle = _sweep * f;
		var dir = _u * MathF.Cos( angle ) + _v * MathF.Sin( angle );
		return _center + dir * (_radius + radialOffset);
	}

	/// <summary>Where the crescent hangs: the final camera view for the owner in first person, the body's eye otherwise.</summary>
	private bool Eye( out global::Transform eye )
	{
		eye = default;
		if ( _firstPerson )
		{
			var camera = Scene.Camera;
			if ( !camera.IsValid() ) return false;
			var view = camera.View;
			eye = new global::Transform( view.Position, view.Rotation );
			return true;
		}
		if ( !_controller.IsValid() ) return false;
		eye = _controller.EyeTransform;
		return true;
	}

	// ------------------------------------------------------------------ the crescent

	private void Render( SceneCustomObject so )
	{
		if ( _verts.Count == 0 || _material is null ) return;
		Graphics.Draw( _verts, _verts.Count, _material, so.Attributes );
	}

	private static float EaseOut( float x ) => 1f - (1f - x) * (1f - x);
	private static float EaseIn( float x ) => x * x;

	private void Build()
	{
		_verts.Clear();
		if ( !_active || !Eye( out var eye ) ) return;

		var p = _fired;
		if ( p is null ) { _active = false; return; }
		var t = Time.Now - _t0;
		var width = WidthOf( p );
		var tint = p.Tint ?? DefaultTint;

		// main stroke on the tip path, then the thinner line just inside it, a hair behind in time
		Stroke( eye, t, width, RadialOffset, 0f, 1f, Alpha * p.Alpha, tint );
		if ( SecondLine )
		{
			var margin = Math.Clamp( (1f - SecondSpan) * 0.5f, 0f, 0.45f );
			Stroke( eye, t - SecondDelay, width * SecondWidth, RadialOffset - SecondOffset, margin, 1f - margin, Alpha * p.Alpha * SecondAlpha, tint );
		}
	}

	/// <summary>
	/// One camera-facing stroke along the arc between fractions f0..f1 of the sweep: reveal (eased), hold,
	/// then an erase from the start with the fade only at the very end. Flat along its length except a
	/// short soft head and tail; thin at both ends.
	/// </summary>
	private void Stroke( global::Transform eye, float t, float width, float radialOffset, float f0, float f1, float alphaScale, Color tint )
	{
		if ( t < 0f ) return;
		var head = EaseOut( Math.Clamp( t / _sweepSeconds, 0f, 1f ) );
		var dissolve = Math.Clamp( (t - _sweepSeconds - _holdSeconds) / MathF.Max( 0.01f, _fadeSeconds ), 0f, 1f );
		var tail = EaseIn( dissolve );
		var global = 1f - Math.Clamp( (dissolve - 0.7f) / 0.3f, 0f, 1f );
		if ( head <= tail + 1e-3f || global <= 0.001f ) return;

		var alpha = alphaScale * global;
		var segments = Math.Max( 4, Segments );
		var over = MathF.Max( 0f, Overshoot );
		var headSoft = MathF.Max( 0.005f, HeadSoftness );
		var tailSoft = MathF.Max( 0.005f, TailSoftness );
		var span = f1 - f0;

		Vector3 prevInner = default, prevOuter = default;
		Color32 prevColor = default;
		float prevV = 0f;
		var havePrev = false;

		for ( var i = 0; i <= segments; i++ )
		{
			var f = i / (float)segments;                                   // 0..1 along this stroke
			var fArc = f0 + (-over + f * (1f + 2f * over)) * span;        // fraction of the measured sweep
			var fVis = Math.Clamp( fArc, 0f, 1f );

			var a = Math.Clamp( (head - fVis) / headSoft, 0f, 1f ) * Math.Clamp( (fVis - tail) / tailSoft, 0f, 1f );
			var taper = MathF.Max( EndTaper, MathF.Pow( MathF.Sin( MathF.PI * f ), 0.5f ) );
			taper *= 1f - Math.Clamp( EndThinning, 0f, 0.9f ) * f;   // trails off toward the end of the cut
			var w = width * taper;

			// camera-facing: across = perpendicular to the arc's tangent and to the line of sight (the eye is
			// the view-space origin), so the stroke's thickness is what it is on screen for every swing
			var centre = ArcPoint( fArc, radialOffset );
			var tangent = ArcPoint( fArc + 0.01f, radialOffset ) - ArcPoint( fArc - 0.01f, radialOffset );
			var across = Vector3.Cross( tangent, centre );
			across = across.LengthSquared > 1e-6f ? across.Normal : Vector3.Up;

			var inner = eye.PointToWorld( centre - across * (w * 0.5f) );
			var outer = eye.PointToWorld( centre + across * (w * 0.5f) );
			var color = (Color32)tint.WithAlpha( Math.Clamp( a * alpha, 0f, 1f ) );

			if ( havePrev && (a > 0f || prevColor.a > 0) )
			{
				var along = (outer - prevOuter) + (inner - prevInner);
				var acrossW = outer - inner;
				var normal = Vector3.Cross( along, acrossW );
				normal = normal.LengthSquared > 1e-6f ? normal.Normal : Vector3.Up;
				var tangentW = acrossW.LengthSquared > 1e-6f ? acrossW.Normal : Vector3.Forward;
				AddCell( prevInner, prevOuter, prevColor, prevV, inner, outer, color, f, normal, tangentW );
			}

			prevInner = inner;
			prevOuter = outer;
			prevColor = color;
			prevV = f;
			havePrev = true;
		}
	}

	/// <summary>Two triangles between two rows; U runs inner (0) to outer (1), V along the arc.</summary>
	private void AddCell( Vector3 innerA, Vector3 outerA, Color32 colorA, float vA, Vector3 innerB, Vector3 outerB, Color32 colorB, float vB, Vector3 normal, Vector3 tangent )
	{
		var ia = Vert( innerA, colorA, 0f, vA, normal, tangent );
		var oa = Vert( outerA, colorA, 1f, vA, normal, tangent );
		var ib = Vert( innerB, colorB, 0f, vB, normal, tangent );
		var ob = Vert( outerB, colorB, 1f, vB, normal, tangent );
		_verts.Add( ia ); _verts.Add( oa ); _verts.Add( ob );
		_verts.Add( ia ); _verts.Add( ob ); _verts.Add( ib );
	}

	private static Vertex Vert( Vector3 position, Color32 color, float u, float v, Vector3 normal, Vector3 tangent )
	{
		var vert = new Vertex( position );
		vert.Color = color;
		vert.Normal = normal;
		vert.Tangent = new Vector4( tangent, 1f );
		vert.TexCoord0 = new Vector4( u, v, 0f, 0f );
		return vert;
	}

	// ------------------------------------------------------------------ debug

	/// <summary>
	/// Records the real tip (FirstPersonArms.SwordWorld, guard + 28 up) in eye space while an attack plays,
	/// draws the recorded path with the fitted arc, and logs the tips at the crescent's frames once per swing.
	/// </summary>
	private void DebugUpdate( int frame )
	{
		if ( _profile is not null && !IsProxy && _arms.IsValid() && _arms.SwordWorld is { } sword && Scene.Camera.IsValid() )
		{
			var view = Scene.Camera.View;
			var eyeNow = new global::Transform( view.Position, view.Rotation );
			var tip = sword.Position + sword.Rotation.Up * BladeLength;
			if ( frame >= 0 && frame <= _profile.EndFrame + 2 && (_tipPath.Count == 0 || _tipPath[^1].Frame != frame) )
			{
				_tipPath.Add( (frame, eyeNow.PointToLocal( tip )) );
				_tipShowUntil = Time.Now + 1.5f;
			}

			if ( !_tipLogged && frame > _profile.EndFrame + 2 && _tipPath.Count > 0 )
			{
				_tipLogged = true;
				var sb = new StringBuilder( $"[SwordSlash] tip path {_clip}:" );
				foreach ( var (f, local) in _tipPath )
					if ( f >= _profile.StartFrame - 1 && f <= _profile.EndFrame + 1 )
						sb.Append( $" f{f}({local.x:0},{local.y:0},{local.z:0})" );
				Log.Info( sb.ToString() );
			}
		}

		if ( !Eye( out var eye ) ) return;

		if ( Time.Now < _tipShowUntil )
			for ( var i = 1; i < _tipPath.Count; i++ )
				DebugOverlay.Line( eye.PointToWorld( _tipPath[i - 1].Local ), eye.PointToWorld( _tipPath[i].Local ), Color.Magenta );

		if ( !_active ) return;
		DebugOverlay.Line( eye.PointToWorld( _profile.Start ), eye.PointToWorld( _profile.Mid ), Color.Red );
		DebugOverlay.Line( eye.PointToWorld( _profile.Mid ), eye.PointToWorld( _profile.End ), Color.Yellow );
		var prev = ArcPoint( 0f, 0f );
		for ( var i = 1; i <= 24; i++ )
		{
			var p = ArcPoint( i / 24f, 0f );
			DebugOverlay.Line( eye.PointToWorld( prev ), eye.PointToWorld( p ), Color.Cyan );
			prev = p;
		}
	}
}

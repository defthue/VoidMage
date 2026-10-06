using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using VoidMage.Combat;
using VoidMage.Spells;

namespace VoidMage.Character;

/// <summary>
/// The potion going down: while the drink clip holds the bottle at the mouth, a glossy stream in the potion's colour
/// (red healing, blue mana) runs from the bottle's lip into the mouth, with bulges flowing along it, and the odd drop
/// that misses runs off the chin or falls from the lip. It grows from the lip when the pour starts and breaks off
/// and slides into the mouth when it stops. Runs on every peer off synced state (the drinker's DrinkKind and
/// BottleInHand, the clip clock), so everyone sees it.
///
/// The stream is a runtime strip drawn like SwordSlash's crescent (SceneCustomObject + Graphics.Draw on the slash
/// ribbon shader, potion_stream.vmat); CharacterBoneRig builds it right after the hand props are placed, when both
/// the first-person bottle (PostCameraSetup) and the third-person one (the follower) are this frame's.
/// No lights, no bloom, no collision, no trails.
/// </summary>
[Title( "Potion Pour" ), Category( "Void Mage" ), Icon( "water_drop" )]
public sealed class PotionPour : Component
{
	public const string MaterialPath = "materials/potion_stream.vmat";

	[ConVar( "potion_pour" )] public static bool On { get; set; } = true;
	/// <summary>Clip frame (30 fps, 1 = first key) the pour starts on: the bottle has reached the mouth.</summary>
	[ConVar( "potion_pour_start" )] public static int StartFrame { get; set; } = 5;
	/// <summary>Clip frame the pour stops on (the heal lands on potion_heal_frame, 56).</summary>
	[ConVar( "potion_pour_end" )] public static int EndFrame { get; set; } = 54;
	/// <summary>Multiplier on the stream's width.</summary>
	[ConVar( "potion_pour_width" )] public static float WidthScale { get; set; } = 1f;
	/// <summary>First person: the mouth in camera space, "forward,left,up" (units). Just ahead of and below the camera, so the stream runs out of the bottom of the view.</summary>
	[ConVar( "potion_pour_mouth_fp" )] public static string MouthFp { get; set; } = "3,0,-3";
	/// <summary>Third person: the mouth from the head bone, "forward,left,up" in the body's facing.</summary>
	[ConVar( "potion_pour_mouth_tp" )] public static string MouthTp { get; set; } = "9,0,0";
	/// <summary>Multiplier on the drip rates (0 = no drips).</summary>
	[ConVar( "potion_pour_drips" )] public static float DripScale { get; set; } = 1f;
	/// <summary>Draws the lip (green), the mouth (yellow) and the stream's curve.</summary>
	[ConVar( "potion_pour_debug" )] public static bool DebugDraw { get; set; }

	private const float ClipFps = 30f;
	private const int Segments = 16;
	/// <summary>
	/// The rim's diameter as a share of the uncorked bottle's height, measured on the FBX meshes (potion 06: rim radius
	/// 0.047 on 0.385 tall; potion 07: 0.040 on 0.485), so the stream is as wide as the mouth of the bottle it leaves.
	/// </summary>
	private const float ManaRimShare = 0.244f;
	private const float HealRimShare = 0.165f;
	/// <summary>The stream at the lip as a share of the rim, and at the mouth as a share of the lip.</summary>
	private const float LipFill = 0.85f;
	private const float MouthNarrow = 0.72f;
	private const float GrowSeconds = 0.1f;
	private const float BreakSeconds = 0.12f;
	private const float MouthDripRate = 4f;
	private const float LipDripRate = 1.5f;

	private sealed class PourSceneObject : SceneCustomObject
	{
		private readonly PotionPour _owner;

		public PourSceneObject( SceneWorld world, PotionPour owner ) : base( world )
		{
			_owner = owner;
			// opaque on purpose, like the slash: a translucent object's depth never reaches the retro filter's depth fog
			Flags.IsOpaque = true;
			Flags.IsTranslucent = false;
			Flags.WantsPrePass = true;
			Flags.CastShadows = false;
		}

		public override void RenderSceneObject() => _owner.Render();
	}

	private PourSceneObject _so;
	private Material _material;
	// the only record of the vertex count (Graphics.Draw trusts the count it is given)
	private readonly List<Vertex> _verts = new();

	private PotionDrinker _drinker;
	private FirstPersonArms _arms;
	private ClipPoseSource _source;
	private HandPropFollower _follower;
	private float _nextFollowerScan;
	private readonly BoneCache _bones = new();

	// the visible part of the stream along its curve (0 lip .. 1 mouth)
	private bool _active;
	private float _head, _tail;

	private GameObject _dripRoot;
	private ParticleEffect _mouthFx, _lipFx;
	private ParticleSphereEmitter _mouthEmitter, _lipEmitter;
	private int _dripKind;

	private string _fpParsed, _tpParsed;
	private Vector3 _fpMouth = new( 3f, 0f, -3f ), _tpMouth = new( 9f, 0f, 0f );

	protected override void OnEnabled()
	{
		_material = Material.Load( MaterialPath );
	}

	protected override void OnDisabled()
	{
		_so?.Delete();
		_so = null;
		_verts.Clear();
		_active = false;
		if ( _dripRoot.IsValid() ) _dripRoot.Destroy();
		_dripRoot = null;
		_mouthFx = _lipFx = null;
		_mouthEmitter = _lipEmitter = null;
	}

	private void Render()
	{
		if ( _verts.Count == 0 || _material is null || _so is null ) return;
		Graphics.Draw( _verts, _verts.Count, _material, _so.Attributes );
	}

	/// <summary>Called by CharacterBoneRig every OnPreRender, after the hand props are placed.</summary>
	public void Build( CharacterBoneRig rig )
	{
		_verts.Clear();

		if ( !_drinker.IsValid() ) _drinker = Components.Get<PotionDrinker>( FindMode.EverythingInSelfAndAncestors );
		if ( !_arms.IsValid() ) _arms = Components.Get<FirstPersonArms>( FindMode.EverythingInSelfAndDescendants );
		if ( !_source.IsValid() ) _source = ClipPoseSource.Find( this );
		if ( !_follower.IsValid() && Time.Now >= _nextFollowerScan )
		{
			_nextFollowerScan = Time.Now + 1f;
			_follower = Components.GetAll<HandPropFollower>( FindMode.EverythingInSelfAndDescendants ).FirstOrDefault( f => f.Role == HandPropRole.Potion );
		}

		var camera = Scene.Camera;
		var firstPerson = !IsProxy && _arms.IsValid() && _arms.RigVisible;
		var bottle = Bottle( firstPerson );
		var model = _drinker.IsValid() ? _drinker.BottleModel : null;
		if ( !On || bottle is not { } at || model is null || !camera.IsValid() || !_drinker.BottleInHand )
		{
			Stop();
			return;
		}

		// the lip: the top centre of the uncorked bottle, and the way the neck points
		var bounds = model.Bounds;
		var lip = at.PointToWorld( new Vector3( bounds.Center.x, bounds.Center.y, bounds.Maxs.z ) );
		var neck = at.Rotation.Up;
		if ( !Mouth( rig, firstPerson, camera, out var mouth ) )
		{
			Stop();
			return;
		}

		// the pour's own clock: grow in, break off
		var dt = Time.Delta;
		if ( Pouring() )
		{
			if ( !_active || _tail > 0f ) { _active = true; _head = 0f; _tail = 0f; }
			_head = MathF.Min( 1f, _head + dt / GrowSeconds );
		}
		else if ( _active )
		{
			_tail = MathF.Min( 1f, _tail + dt / BreakSeconds );
			if ( _tail >= 1f ) _active = false;
		}

		var heal = _drinker.DrinkKind == 1;
		var bright = heal ? ThrownBottle.HealBright : ThrownBottle.ManaBright;
		var dark = heal ? ThrownBottle.HealDark : ThrownBottle.ManaDark;

		if ( _active && _head > _tail + 1e-3f )
		{
			EnsureSceneObject();
			if ( _so is not null )
			{
				_so.Flags.ViewModelLayer = firstPerson && _arms.ViewModelLayer;
				var rim = bounds.Size.z * MathF.Abs( at.Scale.z ) * (FirstPersonArms.IsManaBottle( model ) ? ManaRimShare : HealRimShare);
				Strip( lip, neck, mouth, camera.View.Position, bright, rim * LipFill );
			}
		}

		Drips( lip, mouth, firstPerson, bright, dark, streaming: _active && _tail <= 0f && _head >= 1f );

		if ( DebugDraw ) Debug( lip, neck, mouth );
	}

	/// <summary>The bottle's transform this frame: the first-person prop for the owner looking through the arms, the hand follower's otherwise.</summary>
	private Transform? Bottle( bool firstPerson )
	{
		if ( firstPerson ) return _arms.PotionWorld;
		return _follower.IsValid() ? _follower.PropWorld : null;
	}

	/// <summary>The drink clip is inside the pour window (tuning holds its frozen frame).</summary>
	private bool Pouring()
	{
		if ( !_source.IsValid() || _source.Clip != FirstPersonArms.ClipDrink ) return false;
		var frame = !IsProxy && _drinker.Tuning
			? PotionDrinker.TuneFrame
			: (int)MathF.Floor( _source.Elapsed * ClipFps ) + 1;
		return frame >= StartFrame && frame <= EndFrame;
	}

	private bool Mouth( CharacterBoneRig rig, bool firstPerson, CameraComponent camera, out Vector3 mouth )
	{
		mouth = default;
		if ( firstPerson )
		{
			var o = Parsed( MouthFp, ref _fpParsed, ref _fpMouth );
			var view = camera.View;
			var rot = view.Rotation;
			mouth = view.Position + rot.Forward * o.x + rot.Left * o.y + rot.Up * o.z;
			return true;
		}

		var body = rig.Body;
		if ( !body.IsValid() || body.Model is null ) return false;
		var bone = _bones.Get( body.Model, "head" );
		if ( bone is null || !rig.TryGetPose( bone, out var head ) ) return false;
		var t = Parsed( MouthTp, ref _tpParsed, ref _tpMouth );
		var facing = Rotation.FromYaw( body.WorldRotation.Angles().yaw );
		mouth = head.Position + facing.Forward * t.x + facing.Left * t.y + facing.Up * t.z;
		return true;
	}

	/// <summary>"x,y,z" from a convar, re-parsed only when the string changes (a bad string keeps the last good value).</summary>
	private static Vector3 Parsed( string text, ref string parsed, ref Vector3 value )
	{
		if ( text == parsed ) return value;
		parsed = text;
		var parts = (text ?? "").Split( ',' );
		if ( parts.Length == 3
			&& float.TryParse( parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x )
			&& float.TryParse( parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y )
			&& float.TryParse( parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z ) )
			value = new Vector3( x, y, z );
		return value;
	}

	private void Stop()
	{
		_active = false;
		_head = _tail = 0f;
		SetDripRates( 0f, 0f );
	}

	private void EnsureSceneObject()
	{
		if ( _so.IsValid() ) return;
		if ( !Scene.IsValid() || Scene.SceneWorld is null ) return;
		_so = new PourSceneObject( Scene.SceneWorld, this );
		_so.Transform = global::Transform.Zero;
		_so.Attributes.Set( "g_DepthFeather", 0f );
	}

	// ------------------------------------------------------------------ the stream

	/// <summary>A point on the stream's curve: it leaves the lip along the neck and bends into the mouth.</summary>
	private static Vector3 Curve( Vector3 p0, Vector3 p1, Vector3 p2, float s )
	{
		var u = 1f - s;
		return p0 * (u * u) + p1 * (2f * u * s) + p2 * (s * s);
	}

	/// <summary>
	/// The strip: shape and fades only; the look (rim, highlight streaks, flow, rippled edge) is the shader's. It starts a
	/// little inside the neck so the bottle's own rim hides where it begins, is full rim width there and draws in over
	/// the first stretch the way a pouring column narrows, then holds. V is the distance along it in world units.
	/// </summary>
	private void Strip( Vector3 lip, Vector3 neck, Vector3 mouth, Vector3 eye, Color bright, float lipWidth )
	{
		var start = lip - neck * (lipWidth * 0.35f);
		var control = start + neck * (0.4f * (mouth - start).Length);

		Vector3 prevA = default, prevB = default, prevCentre = default;
		Color32 prevColor = default;
		float prevV = 0f, distance = 0f;

		for ( var i = 0; i <= Segments; i++ )
		{
			var s = _tail + (_head - _tail) * (i / (float)Segments);
			var centre = Curve( start, control, mouth, s );
			var tangent = 2f * (1f - s) * (control - start) + 2f * s * (mouth - control);
			var across = Vector3.Cross( tangent, centre - eye );
			across = across.LengthSquared > 1e-6f ? across.Normal : Vector3.Up;
			if ( i > 0 ) distance += (centre - prevCentre).Length;

			var narrow = MathX.Lerp( 1f, MouthNarrow, SmoothStep( s / 0.4f ) );
			var width = lipWidth * narrow * MathF.Max( 0f, WidthScale );

			// soft into the mouth (in first person: before it reaches the camera), and a short soft front while it
			// grows in and a soft back while it breaks off
			var alpha = Math.Clamp( (1f - s) / 0.12f, 0f, 1f );
			if ( _head < 1f ) alpha *= Math.Clamp( (_head - s) / 0.08f, 0f, 1f );
			if ( _tail > 0f ) alpha *= Math.Clamp( (s - _tail) / 0.08f, 0f, 1f );
			var color = (Color32)bright.WithAlpha( alpha );

			var a = centre - across * (width * 0.5f);
			var b = centre + across * (width * 0.5f);
			if ( i > 0 )
			{
				var along = (a - prevA) + (b - prevB);
				var normal = Vector3.Cross( along, b - a );
				normal = normal.LengthSquared > 1e-6f ? normal.Normal : Vector3.Up;
				var tangentW = (b - a).LengthSquared > 1e-6f ? (b - a).Normal : Vector3.Forward;
				AddCell( prevA, prevB, prevColor, prevV, a, b, color, distance, normal, tangentW );
			}
			prevA = a; prevB = b; prevCentre = centre; prevColor = color; prevV = distance;
		}
	}

	private static float SmoothStep( float x )
	{
		x = Math.Clamp( x, 0f, 1f );
		return x * x * (3f - 2f * x);
	}

	/// <summary>Two triangles between two rows; U runs across (0..1), V along the stream.</summary>
	private void AddCell( Vector3 aA, Vector3 bA, Color32 colorA, float vA, Vector3 aB, Vector3 bB, Color32 colorB, float vB, Vector3 normal, Vector3 tangent )
	{
		var ia = Vert( aA, colorA, 0f, vA, normal, tangent );
		var oa = Vert( bA, colorA, 1f, vA, normal, tangent );
		var ib = Vert( aB, colorB, 0f, vB, normal, tangent );
		var ob = Vert( bB, colorB, 1f, vB, normal, tangent );
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

	// ------------------------------------------------------------------ drips

	/// <summary>The drops that miss: off the chin while the stream runs (not in first person: that end is behind the camera), and now and then off the lip.</summary>
	private void Drips( Vector3 lip, Vector3 mouth, bool firstPerson, Color bright, Color dark, bool streaming )
	{
		var scale = MathF.Max( 0f, DripScale );
		if ( !streaming || scale <= 0f )
		{
			SetDripRates( 0f, 0f );
			return;
		}

		EnsureDrips();
		if ( !_dripRoot.IsValid() ) return;

		var kind = _drinker.DrinkKind;
		if ( kind != _dripKind )
		{
			_dripKind = kind;
			var gradient = FireballVfx.GradientOverLife( new Gradient.ColorFrame( 0f, bright ), new Gradient.ColorFrame( 0.5f, bright ), new Gradient.ColorFrame( 1f, dark ) );
			_mouthFx.Gradient = gradient;
			_lipFx.Gradient = gradient;
		}

		_mouthFx.GameObject.WorldPosition = mouth;
		_lipFx.GameObject.WorldPosition = lip;
		SetDripRates( firstPerson ? 0f : MouthDripRate * scale, LipDripRate * scale );
	}

	private float _mouthRate = -1f, _lipRate = -1f;

	private void SetDripRates( float mouth, float lip )
	{
		if ( _mouthEmitter.IsValid() && _mouthRate != mouth ) { _mouthEmitter.Rate = mouth; _mouthRate = mouth; }
		if ( _lipEmitter.IsValid() && _lipRate != lip ) { _lipEmitter.Rate = lip; _lipRate = lip; }
	}

	private void EnsureDrips()
	{
		if ( _dripRoot.IsValid() ) return;
		_dripRoot = new GameObject( true, "potion_pour_drips" );
		_dripRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_dripRoot.Tags.Add( "spellfire" );
		(_mouthFx, _mouthEmitter) = DripLayer( "mouth" );
		(_lipFx, _lipEmitter) = DripLayer( "lip" );
		_dripKind = 0;
		_mouthRate = _lipRate = -1f;
	}

	private (ParticleEffect, ParticleSphereEmitter) DripLayer( string name )
	{
		var (go, fx, renderer) = FireballVfx.AddSpriteLayer( _dripRoot, name, BloodVfx.TexDrop, additive: false, bloom: false );
		renderer.SortMode = ParticleSpriteRenderer.ParticleSortMode.Unsorted;
		renderer.FaceVelocity = true;
		renderer.DepthFeather = 0.5f;
		fx.MaxParticles = 12;
		fx.InitialVelocity = Vector3.Down * 10f;
		fx.StartVelocity = new ParticleFloat( 4f, 14f );
		fx.Force = true; fx.ForceDirection = Vector3.Down * 900f;
		fx.Damping = 0.5f;
		fx.Lifetime = new ParticleFloat( 0.3f, 0.5f );
		fx.Scale = FireballVfx.OverLife( 1.6f, 1.1f );
		fx.Stretch = 1.2f;
		fx.Alpha = FireballVfx.CurveOverLife( new Curve.Frame( 0f, 1f ), new Curve.Frame( 0.8f, 1f ), new Curve.Frame( 1f, 0f ) );
		var em = go.Components.Create<ParticleSphereEmitter>();
		em.Radius = 0.3f; em.Velocity = 0f; em.Rate = 0f; em.Burst = 0; em.Loop = true;
		return (fx, em);
	}

	// ------------------------------------------------------------------ debug

	private void Debug( Vector3 lip, Vector3 neck, Vector3 mouth )
	{
		Cross( lip, Color.Green );
		Cross( mouth, Color.Yellow );
		var control = lip + neck * (0.4f * (mouth - lip).Length);
		var prev = lip;
		for ( var i = 1; i <= Segments; i++ )
		{
			var p = Curve( lip, control, mouth, i / (float)Segments );
			DebugOverlay.Line( prev, p, Color.Cyan );
			prev = p;
		}
	}

	private void Cross( Vector3 p, Color color )
	{
		const float r = 0.6f;
		DebugOverlay.Line( p - Vector3.Forward * r, p + Vector3.Forward * r, color );
		DebugOverlay.Line( p - Vector3.Left * r, p + Vector3.Left * r, color );
		DebugOverlay.Line( p - Vector3.Up * r, p + Vector3.Up * r, color );
	}
}

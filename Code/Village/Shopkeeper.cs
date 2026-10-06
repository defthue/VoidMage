using System;
using System.Collections.Generic;
using Sandbox;
using VoidMage.Inventory;

namespace VoidMage.Village;

/// <summary>
/// John the shopkeeper: the elkdemon the user stood behind the shop's table (user, 2026-09-18). Like the table he is
/// found by his model when the village starts, so nothing in the scene has to name him.
///
/// His body: nobody near, he leans back on the boxes behind him with a leg up (the user stands him against them).
/// Someone walks up and he pushes himself off them; while they stay he stands in his base idle and every few seconds
/// does one of a handful of bored little things; they walk off and he settles back onto the boxes. The model has no
/// animgraph: its clips are played by name as sequences, one after another off a queue, with the engine's own blend
/// between them. A change of posture is never cut short; anything else gives way to a reaction from the chat.
///
/// He is an E thing like the table's stock (white outline, the same prompt): E opens <see cref="UI.ShopkeeperChat"/>.
/// The chat's picture of his face is NOT him (user: the reactions play on the body in the scene, the picture only ever
/// idles): a second elkdemon far under the map that plays nothing but the base idle, its own lights and a camera that
/// never moves, the way the wardrobe photographs its mannequin.
/// </summary>
[Title( "Shopkeeper" ), Category( "Void Mage" ), Icon( "record_voice_over" )]
public sealed class Shopkeeper : Component
{
	public const string ModelName = "elkdemon";
	public const string PortraitTag = "keeperportrait";
	/// <summary>On the whole portrait stage: the portrait camera draws it, the player's camera never does.</summary>
	public const string PortraitLightTag = "keeperlight";
	/// <summary>A thinner white than the pickups wear: he fills far more of the screen than a potion does (user).</summary>
	public const string FocusOutlineMaterial = "shaders/keeper_outline_focus.vmat";

	private const string BaseClip = "base";
	private const string LeanLoop = "lean_b_armsfoldedlegup_loop";
	private const string LeanEnter = "lean_b_armsfoldedlegup_enter";
	private const string LeanExit = "lean_b_armsfoldedlegup_exit";
	private const string HeadBone = "Head";

	/// <summary>The player closer than this: he comes off the boxes (units, from the camera to his feet).</summary>
	[ConVar( "keeper_near_range" )] public static float NearRange { get; set; } = 260f;
	/// <summary>The player further than this: he leans back. Wider than the near range so the edge never flickers.</summary>
	[ConVar( "keeper_far_range" )] public static float FarRange { get; set; } = 340f;
	/// <summary>Seconds of plain idle between two bored little things.</summary>
	[ConVar( "keeper_idle_min" )] public static float IdleMin { get; set; } = 4f;
	[ConVar( "keeper_idle_max" )] public static float IdleMax { get; set; } = 9f;
	/// <summary>How close the player has to be to talk (eye to chest), and how closely they have to look at him (degrees).</summary>
	[ConVar( "keeper_talk_range" )] public static float TalkRange { get; set; } = 200f;
	[ConVar( "keeper_talk_cone" )] public static float TalkCone { get; set; } = 30f;

	/// <summary>The picture: how far round his front the camera stands (degrees; negative = his right, so he looks to the RIGHT of the picture) and how far above his eye line.</summary>
	[ConVar( "keeper_portrait_yaw" )] public static float PortraitYaw { get; set; } = -28f;
	[ConVar( "keeper_portrait_pitch" )] public static float PortraitPitch { get; set; } = 6f;
	[ConVar( "keeper_portrait_fov" )] public static float PortraitFov { get; set; } = 24f;
	/// <summary>How much of him the picture covers top to bottom (units) and how far over the head bone its middle sits.</summary>
	[ConVar( "keeper_portrait_span" )] public static float PortraitSpan { get; set; } = 34f;
	[ConVar( "keeper_portrait_lift" )] public static float PortraitLift { get; set; } = 4f;
	/// <summary>Slides him sideways in the picture (units, positive = to the picture's right).</summary>
	[ConVar( "keeper_portrait_shift" )] public static float PortraitShift { get; set; } = 0f;
	[ConVar( "keeper_portrait_key" )] public static float PortraitKey { get; set; } = 1.4f;
	[ConVar( "keeper_portrait_fill" )] public static float PortraitFill { get; set; } = 0.5f;

	public static Shopkeeper Instance { get; private set; }
	/// <summary>Set while he is the thing the local player's E would talk to.</summary>
	public static Shopkeeper Focused { get; private set; }

	private static readonly string[] PassThrough = { "player", "viewmodel", "posesource", "debris", "trigger", "npc", "enemy", "ally", "ragdoll", "spellfire", "potion", "critter", VillageShop.ItemTag };
	private static float _nextSearchAt;

	// the bored little things (user's list); hands on hips is the only one with a held loop
	private static readonly string[] Idles =
	{
		"bored_swingarms", "handsonhips", "look_down", "look_r",
		"pointhand_index_f_small", "stretch_arms", "stretch_legs", "weightshift_l", "weightshift_r",
	};

	private SkinnedModelRenderer _renderer;
	private readonly Queue<KeeperStep> _queue = new();
	private KeeperStep? _current;
	private float _stepAt;
	/// <summary>How long the current held loop is held (rolled between the step's Hold and HoldMax when it begins).</summary>
	private float _holdFor;
	/// <summary>Off the boxes (or on his way off them): what his resting clip is.</summary>
	private bool _standing;
	private bool _playerNear;
	private float _nextIdleAt;
	private string _lastIdle;

	public string Label => $"Talk to {ShopkeeperDialogue.Name}";

	/// <summary>The scene's shopkeeper, made from the elkdemon standing in it. Null where there is none (looked for again now and then).</summary>
	public static Shopkeeper EnsureOn( Scene scene )
	{
		if ( Instance.IsValid() ) return Instance;
		if ( !scene.IsValid() || Time.Now < _nextSearchAt ) return null;
		_nextSearchAt = Time.Now + 2f;
		foreach ( var r in scene.GetAllComponents<SkinnedModelRenderer>() )
		{
			if ( !r.IsValid() || r.GameObject.Flags.HasFlag( GameObjectFlags.NotSaved ) ) continue;
			var path = (r.Model?.ResourcePath ?? "").Replace( '\\', '/' ).ToLowerInvariant();
			if ( !path.EndsWith( $"/{ModelName}.vmdl", StringComparison.Ordinal ) ) continue;
			Log.Info( $"[Keeper] found {r.GameObject.Name} at {r.WorldPosition}" );
			return Instance = r.GameObject.Components.GetOrCreate<Shopkeeper>();
		}
		return null;
	}

	protected override void OnStart()
	{
		Instance = this;
		_renderer = Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelf );
		if ( !_renderer.IsValid() ) return;
		_renderer.UseAnimGraph = false;
		_standing = false;
		Play( LeanLoop, true );
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		if ( Focused == this ) Focused = null;
		if ( _portraitRig.IsValid() ) _portraitRig.Destroy();
		_portraitRt?.Dispose();
		_portraitRt = null;
	}

	protected override void OnUpdate()
	{
		if ( !Game.IsPlaying ) return;
		TickPointed();
		if ( !_renderer.IsValid() || !_renderer.SceneModel.IsValid() ) return;
		TickBody();
		TickPortrait();
	}

	// ------------------------------------------------------------------ the body

	private bool Chatting => UI.ShopkeeperChat.IsOpen;

	private void TickBody()
	{
		var camera = Scene.Camera;
		if ( camera.IsValid() )
		{
			float dist = camera.WorldPosition.WithZ( 0f ).Distance( WorldPosition.WithZ( 0f ) );
			if ( MathF.Abs( camera.WorldPosition.z - WorldPosition.z ) > 400f ) dist = float.MaxValue;
			_playerNear = _playerNear ? dist < MathF.Max( FarRange, NearRange ) : dist < NearRange;
		}
		// nobody walks out on a conversation: the chat closes itself first
		bool wantStanding = _playerNear || Chatting;

		if ( _current is { } step )
		{
			// a held loop ends early only when he has somewhere else to be (a reaction never waits behind one: React
			// drops whatever is playing). It used to end for a "waiting reaction" flag as well, which stayed up for the
			// whole reaction and so cut the reaction's OWN held loop at once (hands on hips grumpy never held).
			bool hurry = step.Loop && !step.Locked && wantStanding != _standing;
			if ( !hurry && !StepDone( step ) ) return;
			_current = null;
		}

		// getting up comes before anything queued (a reaction asked for while he still leans); settling back onto the
		// boxes waits until the queue has run out, so hands on hips still gets its exit
		if ( wantStanding && !_standing )
		{
			_standing = true;
			Begin( new KeeperStep( LeanExit, Locked: true ) );
			ScheduleIdle();
			return;
		}
		if ( _queue.Count > 0 )
		{
			Begin( _queue.Dequeue() );
			return;
		}
		if ( !wantStanding && _standing )
		{
			_standing = false;
			Begin( new KeeperStep( LeanEnter, Locked: true ) );
			return;
		}

		// at rest
		var rest = _standing ? BaseClip : LeanLoop;
		var seq = _renderer.Sequence;
		if ( seq.Name != rest || !seq.Looping ) Play( rest, true );
		if ( _standing && !Chatting && Time.Now >= _nextIdleAt ) QueueIdle();
	}

	private bool StepDone( KeeperStep step )
	{
		float t = Time.Now - _stepAt;
		if ( step.Loop ) return t >= _holdFor;
		// the sequence is swapped natively a moment after it is named: never trust "finished" on the first frames
		if ( t < 0.15f ) return false;
		var seq = _renderer.Sequence;
		float duration = seq.Duration / MathF.Max( 0.01f, _renderer.PlaybackRate );
		return seq.IsFinished || (duration > 0f && t >= duration);
	}

	// ------------------------------------------------------------------ the point at the table (VillageShop)

	private readonly List<(Action Then, float Deadline)> _pointed = new();
	private string _pointClip;
	private float _pointPeak;
	private float _pointPeakAt = float.PositiveInfinity;

	/// <summary>
	/// A scroll was bought: he points at the table, and `then` runs `peak` seconds into the clip (the new scroll
	/// appears). Whatever keeps him from pointing (still getting off the boxes, a chat reaction), `then` runs anyway
	/// a few seconds on: the table never waits on his body.
	/// </summary>
	public void Point( string clip, float peak, Action then )
	{
		_pointClip = clip;
		_pointPeak = MathF.Max( 0f, peak );
		_pointed.Add( (then, Time.Now + 4f) );
		React( new[] { new KeeperStep( clip ) } );
	}

	private void TickPointed()
	{
		if ( _pointed.Count == 0 ) return;
		bool peaked = Time.Now >= _pointPeakAt;
		for ( int i = _pointed.Count - 1; i >= 0; i-- )
		{
			if ( !peaked && Time.Now < _pointed[i].Deadline ) continue;
			var then = _pointed[i].Then;
			_pointed.RemoveAt( i );
			then?.Invoke();
		}
		if ( peaked ) _pointPeakAt = float.PositiveInfinity;
	}

	private void Begin( KeeperStep step )
	{
		_current = step;
		if ( _pointed.Count > 0 && step.Clip == _pointClip ) _pointPeakAt = Time.Now + _pointPeak;
		_holdFor = step.HoldMax > step.Hold ? Game.Random.Float( step.Hold, step.HoldMax ) : step.Hold;
		Play( step.Clip, step.Loop );
	}

	private void Play( string clip, bool loop )
	{
		var seq = _renderer.Sequence;
		seq.Blending = true;
		seq.Looping = loop;
		if ( seq.Name == clip ) { if ( _renderer.SceneModel.IsValid() ) seq.Time = 0f; }
		else seq.Name = clip;
		_stepAt = Time.Now;
	}

	private void ScheduleIdle()
	{
		_nextIdleAt = Time.Now + Game.Random.Float( MathF.Min( IdleMin, IdleMax ), MathF.Max( IdleMin, IdleMax ) );
	}

	private void QueueIdle()
	{
		string pick;
		do pick = Idles[Game.Random.Int( 0, Idles.Length - 1 )];
		while ( pick == _lastIdle );
		_lastIdle = pick;
		if ( pick == "handsonhips" )
		{
			_queue.Enqueue( new KeeperStep( "handsonhips_base_enter" ) );
			_queue.Enqueue( new KeeperStep( "handsonhips_base_loop", Loop: true, Hold: 3f, HoldMax: 8f ) );
			_queue.Enqueue( new KeeperStep( "handsonhips_base_exit" ) );
		}
		else _queue.Enqueue( new KeeperStep( pick ) );
		ScheduleIdle();
	}

	/// <summary>
	/// The chat: what his body does with the line he is saying. Whatever bored thing he was in the middle of gives way
	/// at once (the engine blends out of it); coming off the boxes is finished first. Nothing to play means he idles on.
	/// </summary>
	public void React( IReadOnlyList<KeeperStep> steps )
	{
		if ( steps is null || steps.Count == 0 ) return;
		// what was queued behind the current clip is dropped, except a posture change
		var kept = new List<KeeperStep>();
		foreach ( var q in _queue ) if ( q.Locked ) kept.Add( q );
		_queue.Clear();
		foreach ( var q in kept ) _queue.Enqueue( q );
		foreach ( var s in steps ) _queue.Enqueue( s );
		if ( _current is { Locked: false } ) _current = null;
		ScheduleIdle();
	}

	// ------------------------------------------------------------------ E (PotionDrinker.TickPickup)

	private Vector3 Chest => _renderer.IsValid() ? _renderer.Bounds.Center : WorldPosition + Vector3.Up * 40f;

	private Vector3 Head => _renderer.IsValid() && _renderer.TryGetBoneTransform( HeadBone, out var tx ) ? tx.Position : Chest + Vector3.Up * 20f;

	/// <summary>Owner, every frame at home: the shopkeeper when he is in reach, looked at and not behind a wall.</summary>
	public static Shopkeeper UpdateFocus( Scene scene, Vector3 eye, Vector3 forward, GameObject playerRoot )
	{
		Shopkeeper best = null;
		var keeper = PlayerInventory.AtHome ? EnsureOn( scene ) : null;
		if ( keeper.IsValid() && keeper._renderer.IsValid() )
		{
			// looked at anywhere on his body: the nearer of chest and head to the crosshair counts
			float cosCone = MathF.Cos( TalkCone.Clamp( 1f, 89f ) * MathF.PI / 180f );
			var head = keeper.Head;
			var toChest = keeper.Chest - eye;
			var toHead = head - eye;
			float dist = toChest.Length;
			if ( dist <= MathF.Max( 10f, TalkRange ) && dist >= 1f
				&& MathF.Max( Vector3.Dot( toChest.Normal, forward ), Vector3.Dot( toHead.Normal, forward ) ) >= cosCone )
			{
				// he stands behind his table: the line is drawn to his head, over it
				var tr = scene.Trace.Ray( eye, head ).WithoutTags( PassThrough ).IgnoreGameObjectHierarchy( playerRoot ).Run();
				if ( !tr.Hit || tr.Distance >= toHead.Length - 8f || tr.GameObject == keeper.GameObject ) best = keeper;
			}
		}
		SetFocused( best );
		// re-applied every frame while in focus: an override set on a scene object that is not built yet is lost
		if ( best.IsValid() ) best.Highlight( true );
		return best;
	}

	public static void ClearFocus() => SetFocused( null );

	private static void SetFocused( Shopkeeper k )
	{
		if ( Focused == k ) return;
		if ( Focused.IsValid() ) Focused.Highlight( false );
		Focused = k;
		if ( k.IsValid() ) k.Highlight( true );
	}

	private int _outlineSlot = -2;
	private static Material _focusOutline;

	/// <summary>The white outline of the thing E would use: the outline slot swapped, as the pickups do.</summary>
	private void Highlight( bool on )
	{
		var r = _renderer;
		if ( !r.IsValid() || r.Model is null ) return;
		var materials = r.Materials;
		if ( _outlineSlot == -2 )
		{
			_outlineSlot = -1;
			for ( int i = 0; i < materials.Count; i++ )
			{
				var m = materials.GetOriginal( i );
				var name = m?.ResourcePath ?? m?.Name ?? "";
				if ( name.Contains( "outline", StringComparison.OrdinalIgnoreCase ) ) { _outlineSlot = i; break; }
			}
		}
		if ( _outlineSlot < 0 ) return;
		_focusOutline ??= Material.Load( FocusOutlineMaterial );
		materials.SetOverride( _outlineSlot, on ? _focusOutline : null );
	}

	public void Talk() => UI.ShopkeeperChat.Open( this );

	/// <summary>How far the local player's eye is from him: the chat ends when they wander off.</summary>
	public float DistanceTo( Vector3 eye ) => eye.Distance( Chest );

	// ------------------------------------------------------------------ the picture in the chat

	// wide enough for the antlers: the chat shows it at the same height as before, only with more room either side
	private static readonly Vector2 PortraitSize = new( 896f, 640f );
	private static readonly Vector3 StageOrigin = new( 4000f, 0f, -30000f );
	/// <summary>The picture is framed on where his head is once the idle has settled, then never again: it must not drift.</summary>
	private const float AnchorSeconds = 0.3f;

	private GameObject _portraitRig;
	private SkinnedModelRenderer _portraitBody;
	private CameraComponent _portraitCamera;
	private PointLight _keyLight, _fillLight;
	private Texture _portraitRt;
	private bool _portraitOn;
	private Vector3 _anchor;
	private bool _anchored;
	private float _portraitOnAt;

	/// <summary>His face, idling, transparent around him. Null until the chat has asked for it.</summary>
	public Texture PortraitTexture => _portraitOn ? _portraitRt : null;

	/// <summary>The chat turns the picture on while it is on screen: the camera costs a pass, so it only runs then.</summary>
	public void SetPortrait( bool on )
	{
		_portraitOn = on;
		if ( on && !_portraitRig.IsValid() ) { BuildPortrait(); _portraitOnAt = Time.Now; }
		if ( _portraitRig.IsValid() ) _portraitRig.Enabled = on;
	}

	private void BuildPortrait()
	{
		_portraitRig = new GameObject( true, "keeper portrait" )
		{
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked,
			WorldPosition = StageOrigin,
		};
		_portraitRig.Tags.Add( PortraitTag );
		_portraitRig.Tags.Add( PortraitLightTag );

		// the sitter: the same model at the same size, only ever idling
		var sitter = new GameObject( true, "sitter" ) { Parent = _portraitRig, Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };   // NotSaved: EnsureOn never mistakes it for him
		sitter.WorldScale = WorldScale;
		_portraitBody = sitter.Components.Create<SkinnedModelRenderer>();
		_portraitBody.Model = _renderer.Model;
		_portraitBody.RenderType = ModelRenderer.ShadowRenderType.Off;
		_portraitBody.UseAnimGraph = false;
		_portraitBody.Sequence.Looping = true;
		_portraitBody.Sequence.Name = BaseClip;

		var cameraObject = new GameObject( true, "camera" ) { Parent = _portraitRig };
		_portraitCamera = cameraObject.Components.Create<CameraComponent>();
		_portraitCamera.IsMainCamera = false;
		_portraitCamera.Priority = -102;
		_portraitCamera.ClearFlags = ClearFlags.All;
		_portraitCamera.BackgroundColor = Color.Transparent;
		_portraitCamera.RenderTags.Add( PortraitTag );
		_portraitCamera.EnablePostProcessing = false;
		_portraitCamera.FovAxis = CameraComponent.Axis.Vertical;
		// a camera with a RenderTarget must keep a non-zero viewport or the engine skips it (see DungeonMinimap)
		_portraitCamera.Viewport = new Vector4( 0f, 0f, 1f, 1f );
		_portraitCamera.ZNear = 2f;
		_portraitCamera.ZFar = 1000f;

		// a camera's tag whitelist filters lights too (WardrobeStage): the stage brings its own
		_keyLight = BuildLight( "key" );
		_fillLight = BuildLight( "fill" );
	}

	private PointLight BuildLight( string name )
	{
		var go = new GameObject( true, name ) { Parent = _portraitRig };
		var light = go.Components.Create<PointLight>();
		light.Shadows = false;
		light.Radius = 220f;
		return light;
	}

	private void TickPortrait()
	{
		if ( !_portraitOn || !_portraitCamera.IsValid() || !_portraitBody.IsValid() ) return;
		var main = Scene.Camera;
		if ( main.IsValid() && !main.RenderExcludeTags.Has( PortraitLightTag ) ) main.RenderExcludeTags.Add( PortraitLightTag );

		// framed on the sitter's head while the first frames of the idle settle (the very first time only), then
		// fixed for good: he breathes in the picture, the picture itself stays put and is the same every time
		if ( !_anchored || Time.Now - _portraitOnAt <= AnchorSeconds )
		{
			_anchor = _portraitBody.TryGetBoneTransform( HeadBone, out var tx ) ? tx.Position : StageOrigin + Vector3.Up * 60f;
			_anchored = true;
		}

		float fov = Math.Clamp( PortraitFov, 5f, 90f );
		float distance = MathF.Max( 4f, PortraitSpan ) * 0.5f / MathF.Tan( fov * 0.5f * MathF.PI / 180f );
		float pitch = PortraitPitch * MathF.PI / 180f;
		// from his head out to the camera: round his front by the yaw (the sitter faces +x), up by the pitch
		var side = Rotation.FromYaw( PortraitYaw );
		var toCamera = (side.Forward * MathF.Cos( pitch ) + Vector3.Up * MathF.Sin( pitch )).Normal;
		var rotation = Rotation.LookAt( -toCamera, Vector3.Up );
		// the camera steps the other way to slide him right in the picture
		var focus = _anchor + Vector3.Up * PortraitLift - rotation.Right * PortraitShift;
		_portraitCamera.WorldPosition = focus + toCamera * distance;
		_portraitCamera.WorldRotation = rotation;
		_portraitCamera.FieldOfView = fov;

		// key from the camera's side and above, fill from the other side, both in front of him
		float sign = PortraitYaw < 0f ? -1f : 1f;
		_keyLight.WorldPosition = focus + toCamera * 70f + side.Left * (40f * sign) + Vector3.Up * 35f;
		_keyLight.LightColor = new Color( 1f, 0.92f, 0.8f ) * PortraitKey;
		_fillLight.WorldPosition = focus + toCamera * 60f + side.Left * (-70f * sign) + Vector3.Up * -5f;
		_fillLight.LightColor = new Color( 0.7f, 0.8f, 1f ) * PortraitFill;

		_portraitRt = Texture.CreateRenderTarget( "keeper_portrait", ImageFormat.RGBA8888, PortraitSize, _portraitRt );
		if ( _portraitCamera.RenderTarget != _portraitRt ) _portraitCamera.RenderTarget = _portraitRt;
		_portraitCamera.CustomSize = PortraitSize;
	}

	/// <summary>Dev: writes the picture as the chat shows it to data/voidmage/keeper/portrait.png (open the chat first).</summary>
	[ConCmd( "keeper_shot" )]
	public static void ShotCommand()
	{
		var keeper = Instance;
		if ( !keeper.IsValid() || keeper.PortraitTexture is not { } tex ) { Log.Warning( "[Keeper] talk to the shopkeeper first (E)" ); return; }
		var px = tex.GetPixels();
		var bmp = new Bitmap( tex.Width, tex.Height );
		for ( var y = 0; y < tex.Height; y++ )
			for ( var x = 0; x < tex.Width; x++ )
				bmp.SetPixel( x, y, px[y * tex.Width + x] );
		FileSystem.Data.CreateDirectory( "voidmage/keeper" );
		FileSystem.Data.WriteAllBytes( "voidmage/keeper/portrait.png", bmp.ToPng() );
		Log.Info( $"[Keeper] wrote voidmage/keeper/portrait.png ({bmp.Width}x{bmp.Height})" );
	}

	/// <summary>Dev: play one of his clips by name, as a reaction would.</summary>
	[ConCmd( "keeper_play" )]
	public static void PlayCommand( string clip )
	{
		if ( !Instance.IsValid() ) { Log.Warning( "[Keeper] no shopkeeper in this scene" ); return; }
		Instance.React( new[] { new KeeperStep( clip ) } );
	}
}

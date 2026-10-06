using System;
using Sandbox;
using Sandbox.Rendering;

namespace VoidMage.Rendering;

public enum RetroResolution
{
	/// <summary>Use the real viewport resolution: no snapping at all.</summary>
	Native = 0,
	P720 = 720,
	P600 = 600,
	P540 = 540,
	P480 = 480,
	P432 = 432,
	P360 = 360,
	P288 = 288,
	P240 = 240,
	P180 = 180,
	/// <summary>Any height you like, set in Custom Height.</summary>
	Custom = -1,
}

public enum RetroSampleQuality
{
	/// <summary>One sample per virtual pixel. Crunchy, aliased, most authentic.</summary>
	Point,
	/// <summary>Average of 4 samples per virtual pixel. Slightly softer edges.</summary>
	Quad,
	/// <summary>Average of 16 samples per virtual pixel. Like a supersampled low-res render.</summary>
	Supersampled,
}

public enum RetroUpscale
{
	/// <summary>Hard square pixels, like an emulator with integer scaling.</summary>
	Nearest,
	/// <summary>Bilinear between virtual pixels, like a low-res signal on a CRT or PS2 on a TV.</summary>
	Smooth,
}

public enum RetroQuantizeMode
{
	/// <summary>Per-channel bit depth (5-6-5 is 16-bit PS1/PS2 style).</summary>
	Rgb,
	/// <summary>Only brightness is stepped, hue and saturation stay smooth. Very toon-friendly.</summary>
	Luminance,
	/// <summary>Snap every colour to the nearest entry of a palette texture.</summary>
	Palette,
}

public enum RetroDither
{
	None,
	Bayer2x2,
	Bayer4x4,
	Bayer8x8,
	/// <summary>Interleaved gradient noise: evenly spread, no visible tiling.</summary>
	GradientNoise,
	/// <summary>Random per-cell noise. Grainy.</summary>
	WhiteNoise,
}

public enum RetroPaletteDistance
{
	/// <summary>Plain RGB distance.</summary>
	Rgb,
	/// <summary>Luminance-weighted distance: better matches how the eye picks colours.</summary>
	Perceptual,
}

public enum RetroScanlinePitch
{
	/// <summary>One scanline per virtual low-res row: lines scale with the resolution setting.</summary>
	LowResRow = 0,
	Every2Px = 2,
	Every3Px = 3,
	Every4Px = 4,
}

public enum RetroMask
{
	None,
	/// <summary>Vertical RGB stripes (Trinitron-style).</summary>
	ApertureGrille,
	/// <summary>RGB stripes staggered every few rows (consumer CRT style).</summary>
	SlotMask,
}

public enum RetroHudCoverage
{
	/// <summary>The HUD is drawn clean on top of the filtered world.</summary>
	None,
	/// <summary>Only the CRT tube curvature bends the HUD along with the world.</summary>
	Curvature,
	/// <summary>The HUD goes through the whole screen-space filter.</summary>
	Everything,
}

public enum RetroStage
{
	/// <summary>After bloom and tonemapping: quantisation crushes the bloom too.</summary>
	AfterPostProcess,
	/// <summary>Before bloom and tonemapping: bloom softens the bands afterwards.</summary>
	BeforePostProcess,
	/// <summary>Right after transparents, before any built-in post effect.</summary>
	AfterTransparent,
}

/// <summary>
/// A style laboratory on the scene camera: low internal resolution, colour depth and dither,
/// palettes, Doom-style banded fog, depth ink lines, grading and CRT overlays. Every technique is
/// its own feature tab that can be toggled and tuned on its own, so looks can be mixed and matched.
/// One fullscreen grab and blit per frame (voidmage_retro.shader); costs nothing when disabled.
/// Built on the engine's BasePostProcess like Bloom, so it renders for every camera that draws
/// the scene: the editor viewport, play mode, and any PostProcessVolume-driven camera.
/// </summary>
[Title( "Retro Filter" ), Category( "Void Mage" ), Icon( "tv" )]
public sealed class RetroFilter : BasePostProcess<RetroFilter>
{
	// ------------------------------------------------------------------ Master

	/// <summary>Wet/dry mix with the untouched frame. 1 = full effect.</summary>
	[Property, Range( 0f, 1f )] public float Blend { get; set; } = 1f;

	/// <summary>A/B compare: everything left of this screen fraction shows the untouched frame.</summary>
	[Property, Range( 0f, 1f )] public float DebugSplit { get; set; } = 0f;

	/// <summary>
	/// How much of the filter the HUD and menus get. Curvature: the world gets the whole filter at Apply Stage and
	/// the tube warp alone runs after the UI, so the interface bends with the screen but stays crisp. Everything:
	/// the whole screen-space look (resolution, colour depth, grading, CRT, aberration, ripple) runs after the UI;
	/// depth fog and edges only know the world's depth so they stay on the world. None: the HUD is drawn clean on top.
	/// </summary>
	[Property] public RetroHudCoverage OverHud { get; set; } = RetroHudCoverage.Curvature;

	/// <summary>Where in the frame the filter runs (with Over HUD = Everything: only the depth fog and depth edges). Changes how bloom and tonemapping interact with it. If fog or edges show nothing, try Before Post Process.</summary>
	[Property] public RetroStage ApplyStage { get; set; } = RetroStage.AfterPostProcess;

	// -------------------------------------------------------------- Resolution

	/// <summary>Snap the frame onto a virtual low-resolution grid.</summary>
	[Property, Title( "Enabled" ), Feature( "Resolution", Icon = "grid_on" )] public bool LowResolution { get; set; } = false;

	/// <summary>Virtual vertical resolution. Width follows the screen aspect.</summary>
	[Property, Feature( "Resolution" )] public RetroResolution Resolution { get; set; } = RetroResolution.P480;

	[Property, Feature( "Resolution" ), Range( 60f, 1440f ), ShowIf( nameof( Resolution ), RetroResolution.Custom )]
	public int CustomHeight { get; set; } = 400;

	/// <summary>Width of a virtual pixel relative to its height. 1 = square. PS1 320x240 stretched to 4:3 is about 1.33.</summary>
	[Property, Feature( "Resolution" ), Range( 0.5f, 2f )] public float PixelAspect { get; set; } = 1f;

	/// <summary>How each virtual pixel samples the underlying frame.</summary>
	[Property, Feature( "Resolution" )] public RetroSampleQuality SampleQuality { get; set; } = RetroSampleQuality.Point;

	/// <summary>How the virtual grid is scaled back up to the screen.</summary>
	[Property, Feature( "Resolution" )] public RetroUpscale Upscale { get; set; } = RetroUpscale.Nearest;

	/// <summary>Lateral colour fringing in source pixels: red shifts left, blue right.</summary>
	[Property, Feature( "Resolution" ), Range( 0f, 8f )] public float ChromaticAberration { get; set; } = 0f;

	/// <summary>Aberration (source pixels) added the instant the player takes a hit, falling off with the screen shake. Works at native resolution too.</summary>
	[Property, Feature( "Resolution" ), Range( 0f, 16f )] public float HitAberration { get; set; } = 8f;

	// --------------------------------------------------------------- Hit ripple

	/// <summary>A ring of radial displacement racing out from the screen position of the player's own sword hit: weight, one per swing.</summary>
	[Property, Title( "Enabled" ), Feature( "Hit ripple", Icon = "waves" )] public bool HitRipple { get; set; } = true;
	/// <summary>How long the ring lives.</summary>
	[Property, Feature( "Hit ripple" ), Range( 0.05f, 1f )] public float RippleSeconds { get; set; } = 0.25f;
	/// <summary>How fast the ring travels, in screen heights per second.</summary>
	[Property, Feature( "Hit ripple" ), Range( 0.2f, 5f )] public float RippleSpeed { get; set; } = 1.6f;
	/// <summary>Thickness of the ring in source pixels.</summary>
	[Property, Feature( "Hit ripple" ), Range( 2f, 120f )] public float RippleWidth { get; set; } = 26f;
	/// <summary>Peak displacement in source pixels, decaying to nothing over RippleSeconds.</summary>
	[Property, Feature( "Hit ripple" ), Range( 0f, 40f )] public float RippleStrength { get; set; } = 8f;

	private static Vector3 _rippleWorld;
	private static float _rippleStart = float.NegativeInfinity;
	private static float _rippleScale = 1f;

	/// <summary>Start a ripple at a world point (the local player's hit); a new one replaces the one in flight.</summary>
	public static void TriggerRipple( Vector3 world, float scale )
	{
		if ( Dev.DevDirector.CleanFx ) return;   // a clean plate for the camera
		_rippleWorld = world;
		_rippleStart = Time.Now;
		_rippleScale = scale;
	}

	// --------------------------------------------------------------- Depth fog

	/// <summary>Fade towards a colour with distance. Stepped, it becomes Doom's colormap darkening.</summary>
	[Property, Title( "Enabled" ), Feature( "Depth Fog", Icon = "foggy" )] public bool DepthFog { get; set; } = false;

	[Property, Feature( "Depth Fog" )] public Color FogColor { get; set; } = Color.Black;

	/// <summary>Distance (world units) where the fog starts.</summary>
	[Property, Feature( "Depth Fog" ), Range( 0f, 4000f )] public float FogStart { get; set; } = 200f;

	/// <summary>Distance (world units) where the fog is at full strength.</summary>
	[Property, Feature( "Depth Fog" ), Range( 0f, 8000f )] public float FogEnd { get; set; } = 1500f;

	/// <summary>0 = smooth. 1 to 32 = number of discrete fog bands, like Doom's light diminishing.</summary>
	[Property, Feature( "Depth Fog" ), Range( 0f, 32f )] public int FogSteps { get; set; } = 0;

	/// <summary>Falloff exponent. 1 = linear, above 1 keeps the near range clear longer.</summary>
	[Property, Feature( "Depth Fog" ), Range( 0.1f, 4f )] public float FogCurve { get; set; } = 1f;

	/// <summary>Maximum fog opacity at the far end.</summary>
	[Property, Feature( "Depth Fog" ), Range( 0f, 1f )] public float FogMax { get; set; } = 1f;

	/// <summary>Fill the sky with the fog colour too, so it merges with the far distance.</summary>
	[Property, Feature( "Depth Fog" )] public bool FogAffectsSky { get; set; } = true;

	/// <summary>
	/// Apply the fog right after the opaque world and sky, before transparents. Particles, the slash and
	/// spells are drawn after it, so a fireball flying down a dark corridor is never fogged by the far wall
	/// behind it (a translucent's own depth never reaches the fog). The cost is that far-off translucents
	/// (a torch flame at the fog end) are not fogged either. Off = fog in the late pass, everything by the
	/// depth behind it.
	/// </summary>
	[Property, Feature( "Depth Fog" )] public bool FogBeforeTransparents { get; set; } = true;

	/// <summary>
	/// Fog every pixel by the farthest depth among itself and its four neighbours. The frame's colour at a
	/// silhouette is an anti-aliased blend of the model and whatever is behind it, but the depth there is the
	/// model's alone, so without this that one-pixel band keeps its unfogged share of the sky while the sky
	/// beside it fogs out: a thin bright edge around every model that has fog or sky behind it.
	/// </summary>
	[Property, Feature( "Depth Fog" ), Title( "Seal Edges" )] public bool FogSealEdges { get; set; } = true;

	// ------------------------------------------------------------- Depth edges

	/// <summary>Ink lines on the virtual grid wherever neighbouring depth jumps. Cheap silhouette outlines.</summary>
	[Property, Title( "Enabled" ), Feature( "Depth Edges", Icon = "border_style" )] public bool DepthEdges { get; set; } = false;

	[Property, Feature( "Depth Edges" )] public Color EdgeColor { get; set; } = Color.Black;

	/// <summary>Relative depth jump (fraction of the distance) that counts as an edge. Lower finds more edges.</summary>
	[Property, Feature( "Depth Edges" ), Range( 0.005f, 0.5f )] public float EdgeThreshold { get; set; } = 0.08f;

	[Property, Feature( "Depth Edges" ), Range( 0f, 1f )] public float EdgeStrength { get; set; } = 1f;

	// ----------------------------------------------------------------- Grading

	/// <summary>Colour grading applied before colour depth reduction, so the bands land where you want them.</summary>
	[Property, Title( "Enabled" ), Feature( "Grading", Icon = "palette" )] public bool Grading { get; set; } = false;

	[Property, Feature( "Grading" ), Range( 0f, 2f )] public float Brightness { get; set; } = 1f;
	[Property, Feature( "Grading" ), Range( 0f, 2f )] public float Contrast { get; set; } = 1f;
	[Property, Feature( "Grading" ), Range( 0f, 2f )] public float Saturation { get; set; } = 1f;

	/// <summary>1 = neutral. Above 1 darkens midtones, below 1 lifts them.</summary>
	[Property, Feature( "Grading" ), Range( 0.25f, 3f )] public float Gamma { get; set; } = 1f;

	/// <summary>Multiplied over the frame. White = no tint.</summary>
	[Property, Feature( "Grading" )] public Color Tint { get; set; } = Color.White;

	/// <summary>Raises pure black toward the Lift Color without touching bright areas. The right tool for "too dark": unlit and fogged-out areas keep a hint of shape.</summary>
	[Property, Feature( "Grading" ), Range( 0f, 0.25f )] public float ShadowLift { get; set; } = 0f;

	/// <summary>The colour blacks are lifted toward. Cool dark tones read as gloom, warm ones as haze.</summary>
	[Property, Feature( "Grading" ), HideIf( nameof( ShadowLift ), 0f )] public Color LiftColor { get; set; } = new Color( 0.4f, 0.45f, 0.6f );

	/// <summary>Only pixels darker than this brightness get lifted. Keep it small so the lift never becomes a veil over the mid-tones.</summary>
	[Property, Feature( "Grading" ), Range( 0.01f, 0.5f ), HideIf( nameof( ShadowLift ), 0f )] public float LiftRange { get; set; } = 0.08f;

	/// <summary>Remap brightness onto a two-colour ramp. 0 = off, 1 = fully duotone. The ramp colours appear once this is above 0.</summary>
	[Property, Feature( "Grading" ), Range( 0f, 1f )] public float Duotone { get; set; } = 0f;

	[Property, Feature( "Grading" ), HideIf( nameof( Duotone ), 0f )] public Color ShadowColor { get; set; } = new Color( 0.05f, 0.02f, 0.12f );
	[Property, Feature( "Grading" ), HideIf( nameof( Duotone ), 0f )] public Color HighlightColor { get; set; } = new Color( 1f, 0.9f, 0.7f );

	// ------------------------------------------------------------ Colour depth

	/// <summary>Reduce the number of colours, with optional dithering. The core PS1/PS2 ingredient.</summary>
	[Property, Title( "Enabled" ), Feature( "Color Depth", Icon = "gradient" )] public bool ColorDepth { get; set; } = false;

	[Property, Feature( "Color Depth" )] public RetroQuantizeMode QuantizeMode { get; set; } = RetroQuantizeMode.Rgb;

	[Property, Feature( "Color Depth" ), Range( 1f, 8f ), ShowIf( nameof( QuantizeMode ), RetroQuantizeMode.Rgb )] public int BitsRed { get; set; } = 5;
	[Property, Feature( "Color Depth" ), Range( 1f, 8f ), ShowIf( nameof( QuantizeMode ), RetroQuantizeMode.Rgb )] public int BitsGreen { get; set; } = 6;
	[Property, Feature( "Color Depth" ), Range( 1f, 8f ), ShowIf( nameof( QuantizeMode ), RetroQuantizeMode.Rgb )] public int BitsBlue { get; set; } = 5;

	/// <summary>Number of brightness steps.</summary>
	[Property, Feature( "Color Depth" ), Range( 2f, 64f ), ShowIf( nameof( QuantizeMode ), RetroQuantizeMode.Luminance )] public int LuminanceSteps { get; set; } = 8;

	/// <summary>A one-row image whose pixels are the palette, left to right (max 64 used). Import it uncompressed with no mips.</summary>
	[Property, Feature( "Color Depth" ), ShowIf( nameof( QuantizeMode ), RetroQuantizeMode.Palette )] public Texture Palette { get; set; }

	/// <summary>How many palette entries to use, counted from the left. 0 = the whole row.</summary>
	[Property, Feature( "Color Depth" ), Range( 0f, 64f ), ShowIf( nameof( QuantizeMode ), RetroQuantizeMode.Palette )] public int PaletteColors { get; set; } = 0;

	[Property, Feature( "Color Depth" ), ShowIf( nameof( QuantizeMode ), RetroQuantizeMode.Palette )] public RetroPaletteDistance PaletteDistance { get; set; } = RetroPaletteDistance.Perceptual;

	/// <summary>Quantise in gamma space (perceptually even steps, like real 8-bit hardware) instead of linear.</summary>
	[Property, Feature( "Color Depth" )] public bool QuantizeInGamma { get; set; } = true;

	[Property, Feature( "Color Depth" )] public RetroDither Dither { get; set; } = RetroDither.Bayer4x4;

	/// <summary>1 = exactly one colour step of dither, the textbook amount. Lower is subtler, higher is noisier.</summary>
	[Property, Feature( "Color Depth" ), Range( 0f, 2f ), HideIf( nameof( Dither ), RetroDither.None )] public float DitherStrength { get; set; } = 1f;

	/// <summary>Size of a dither cell in virtual pixels. 2 makes the pattern chunkier than the pixels.</summary>
	[Property, Feature( "Color Depth" ), Range( 1f, 4f ), HideIf( nameof( Dither ), RetroDither.None )] public int DitherScale { get; set; } = 1;

	/// <summary>Shift the pattern every frame. Hides the pattern in motion, adds shimmer.</summary>
	[Property, Feature( "Color Depth" ), HideIf( nameof( Dither ), RetroDither.None )] public bool AnimateDither { get; set; } = false;

	/// <summary>
	/// Slide the pattern with the camera's yaw and pitch (the Return of the Obra Dinn trick), so looking around
	/// carries the dither with the world instead of the world swimming under a grid fixed to the screen.
	/// Walking still slides it; only rotation is compensated.
	/// </summary>
	[Property, Feature( "Color Depth" ), HideIf( nameof( Dither ), RetroDither.None )] public bool LockDitherToView { get; set; } = false;

	// --------------------------------------------------------------------- CRT

	/// <summary>Screen-grid overlays: tube curvature, scanlines, phosphor mask, pixel grid, vignette.</summary>
	[Property, Title( "Enabled" ), Feature( "CRT", Icon = "tv" )] public bool Crt { get; set; } = false;

	/// <summary>Barrel distortion of the whole image. Corners outside the tube go black.</summary>
	[Property, Feature( "CRT" ), Range( 0f, 0.3f )] public float Curvature { get; set; } = 0f;

	[Property, Feature( "CRT" ), Range( 0f, 1f )] public float ScanlineStrength { get; set; } = 0f;
	[Property, Feature( "CRT" )] public RetroScanlinePitch ScanlinePitch { get; set; } = RetroScanlinePitch.LowResRow;

	/// <summary>0 = soft sine profile, 1 = hard half-row black line.</summary>
	[Property, Feature( "CRT" ), Range( 0f, 1f )] public float ScanlineHardness { get; set; } = 0f;

	[Property, Feature( "CRT" )] public RetroMask Mask { get; set; } = RetroMask.None;
	[Property, Feature( "CRT" ), Range( 0f, 1f ), HideIf( nameof( Mask ), RetroMask.None )] public float MaskStrength { get; set; } = 0.5f;

	/// <summary>Dark lines between virtual pixels, like an LCD or Game Boy screen. Needs Resolution on.</summary>
	[Property, Feature( "CRT" ), Range( 0f, 1f )] public float PixelGridStrength { get; set; } = 0f;

	/// <summary>Grid line width in screen pixels.</summary>
	[Property, Feature( "CRT" ), Range( 0.5f, 4f )] public float PixelGridWidth { get; set; } = 1f;

	[Property, Feature( "CRT" ), Range( 0f, 1f )] public float VignetteStrength { get; set; } = 0f;

	/// <summary>Distance from the centre where darkening starts. 1 reaches the screen edge.</summary>
	[Property, Feature( "CRT" ), Range( 0.1f, 1.5f )] public float VignetteRadius { get; set; } = 0.8f;
	[Property, Feature( "CRT" ), Range( 0.05f, 1.5f )] public float VignetteSoftness { get; set; } = 0.6f;

	/// <summary>0 = ellipse that fits the screen, 1 = perfect circle.</summary>
	[Property, Feature( "CRT" ), Range( 0f, 1f )] public float VignetteRoundness { get; set; } = 0.5f;

	// ----------------------------------------------------------------- Presets

	/// <summary>15-bit colour, 240 lines, wide pixels, 4x4 dither. Short sight: smooth fog to near-black with a faint cold lift so the dark has shape.</summary>
	[Button( "PS1", "videogame_asset" ), Group( "Presets" )]
	public void PresetPs1()
	{
		ResetToClean();
		LowResolution = true; Resolution = RetroResolution.P240; PixelAspect = 1.33f; SampleQuality = RetroSampleQuality.Point; Upscale = RetroUpscale.Nearest;
		ColorDepth = true; QuantizeMode = RetroQuantizeMode.Rgb; BitsRed = 5; BitsGreen = 5; BitsBlue = 5; Dither = RetroDither.Bayer4x4; DitherStrength = 1f;
		DepthFog = true; FogColor = Color.Black; FogStart = 250f; FogEnd = 1000f; FogSteps = 0; FogCurve = 2f; FogMax = 1f; FogAffectsSky = true;
		Grading = true; ShadowLift = 0.015f; LiftColor = new Color( 0.3f, 0.35f, 0.55f ); LiftRange = 0.06f;
	}

	/// <summary>480 lines, smooth TV upscale, 16-bit colour with subtle 8x8 dither, faint scanlines. Dark distance haze pulls the view in.</summary>
	[Button( "PS2 on a TV", "tv" ), Group( "Presets" )]
	public void PresetPs2()
	{
		ResetToClean();
		LowResolution = true; Resolution = RetroResolution.P480; PixelAspect = 1f; SampleQuality = RetroSampleQuality.Quad; Upscale = RetroUpscale.Smooth; ChromaticAberration = 1f;
		ColorDepth = true; QuantizeMode = RetroQuantizeMode.Rgb; BitsRed = 5; BitsGreen = 6; BitsBlue = 5; Dither = RetroDither.Bayer8x8; DitherStrength = 0.75f;
		DepthFog = true; FogColor = Color.Black; FogStart = 300f; FogEnd = 1300f; FogSteps = 0; FogCurve = 2f; FogMax = 1f; FogAffectsSky = true;
		Grading = true; ShadowLift = 0.015f; LiftColor = new Color( 0.3f, 0.35f, 0.55f ); LiftRange = 0.06f;
		Crt = true; ScanlineStrength = 0.25f; ScanlinePitch = RetroScanlinePitch.LowResRow; ScanlineHardness = 0f; Mask = RetroMask.ApertureGrille; MaskStrength = 0.2f; VignetteStrength = 0.35f; Curvature = 0.04f;
	}

	/// <summary>Chunky 200-line grid, luminance stepping, 16-band darkness fog, ink edges. Blacks lifted to a cold floor so unlit areas are dark, not gone.</summary>
	[Button( "Doom", "local_fire_department" ), Group( "Presets" )]
	public void PresetDoom()
	{
		ResetToClean();
		LowResolution = true; Resolution = RetroResolution.Custom; CustomHeight = 200; PixelAspect = 1.2f; SampleQuality = RetroSampleQuality.Point; Upscale = RetroUpscale.Nearest;
		ColorDepth = true; QuantizeMode = RetroQuantizeMode.Luminance; LuminanceSteps = 14; Dither = RetroDither.Bayer2x2; DitherStrength = 0.5f;
		DepthFog = true; FogColor = Color.Black; FogStart = 250f; FogEnd = 1600f; FogSteps = 16; FogCurve = 1.6f; FogMax = 1f; FogAffectsSky = false;
		DepthEdges = true; EdgeColor = Color.Black; EdgeThreshold = 0.06f; EdgeStrength = 1f;
		Grading = true; Saturation = 0.85f; Contrast = 1.05f; ShadowLift = 0.03f; LiftColor = new Color( 0.3f, 0.35f, 0.55f ); LiftRange = 0.08f;
	}

	/// <summary>Game Boy Advance: 160 lines, 15-bit colour, light 2x2 dither, bright low-contrast LCD with a faint pixel grid.</summary>
	[Button( "Game Boy Advance", "smartphone" ), Group( "Presets" )]
	public void PresetGameBoyAdvance()
	{
		ResetToClean();
		LowResolution = true; Resolution = RetroResolution.Custom; CustomHeight = 160; PixelAspect = 1f; SampleQuality = RetroSampleQuality.Quad; Upscale = RetroUpscale.Nearest;
		ColorDepth = true; QuantizeMode = RetroQuantizeMode.Rgb; BitsRed = 5; BitsGreen = 5; BitsBlue = 5; Dither = RetroDither.Bayer2x2; DitherStrength = 0.5f;
		Grading = true; Brightness = 1.15f; Contrast = 0.9f; Saturation = 1.15f; ShadowLift = 0.04f; LiftColor = new Color( 0.5f, 0.5f, 0.55f ); LiftRange = 0.12f;
		Crt = true; PixelGridStrength = 0.2f; PixelGridWidth = 1f;
	}

	/// <summary>Turn every feature off; the frame passes through untouched.</summary>
	[Button( "Clean", "cleaning_services" ), Group( "Presets" )]
	public void PresetClean()
	{
		ResetToClean();
	}

	private void ResetToClean()
	{
		Blend = 1f; DebugSplit = 0f;
		LowResolution = false; PixelAspect = 1f; ChromaticAberration = 0f; SampleQuality = RetroSampleQuality.Point; Upscale = RetroUpscale.Nearest;
		DepthFog = false; FogSteps = 0; FogCurve = 1f; FogMax = 1f; FogAffectsSky = true;
		DepthEdges = false;
		Grading = false; Brightness = 1f; Contrast = 1f; Saturation = 1f; Gamma = 1f; Tint = Color.White; ShadowLift = 0f; LiftRange = 0.08f; Duotone = 0f;
		ColorDepth = false; QuantizeInGamma = true; DitherScale = 1; AnimateDither = false; LockDitherToView = false;
		Crt = false; Curvature = 0f; ScanlineStrength = 0f; ScanlineHardness = 0f; Mask = RetroMask.None; MaskStrength = 0.5f; PixelGridStrength = 0f; VignetteStrength = 0f;
	}

	// ----------------------------------------------------------------- Runtime

	private CommandList _commands;
	private CommandList _fogCommands;
	private CommandList _depthCommands;
	private CommandList _tubeCommands;
	private Material _material;

	private static Stage ToEngineStage( RetroStage stage ) => stage switch
	{
		RetroStage.BeforePostProcess => Stage.BeforePostProcess,
		RetroStage.AfterTransparent => Stage.AfterTransparent,
		_ => Stage.AfterPostProcess,
	};

	private bool RippleActive => HitRipple && RippleStrength > 0f && Time.Now - _rippleStart < RippleSeconds;

	private bool AnyFeatureActive =>
		LowResolution || DepthFog || DepthEdges || Grading || ColorDepth || HitBoost > 0f || RippleActive
		|| ( Crt && ( Curvature > 0f || ScanlineStrength > 0f || Mask != RetroMask.None || PixelGridStrength > 0f || VignetteStrength > 0f ) )
		|| DebugSplit > 0f;

	/// <summary>The taken-hit pulse from HitFeel plus the portal transition's pull, in source pixels (0 outside either).</summary>
	private float HitBoost => HitAberration * VoidMage.Combat.HitFeel.AberrationBoost01 + TransitionAberration;

	/// <summary>Extra aberration in source pixels, written by the portal pull and arrival (VoidMage.Run.PortalTransition).</summary>
	public static float TransitionAberration { get; set; }

	private int ResolvedHeight => Resolution switch
	{
		RetroResolution.Native => 0,
		RetroResolution.Custom => Math.Max( CustomHeight, 16 ),
		_ => (int) Resolution,
	};

	// Called by the post-process system for every camera that renders the scene, each
	// frame. The list is a grab and a blit, rebuilt each call so every slider tuned live
	// in the editor shows up immediately. Nothing is inserted when no feature is on.
	public override void Render()
	{
		if ( Blend <= 0f || !AnyFeatureActive )
			return;

		_material ??= Material.FromShader( "shaders/voidmage_retro.shader" );
		if ( _material is null )
			return;

		// The fog as its own early pass (see FogBeforeTransparents): the same shader, fog only, right
		// after the opaque world; the late pass then runs with its fog off.
		var earlyFog = DepthFog && FogBeforeTransparents;
		if ( earlyFog )
		{
			_fogCommands ??= new CommandList( "Retro Fog" );
			_fogCommands.Reset();
			var fc = _fogCommands.Attributes;
			fc.Set( "RetroFogOnly", 1f );
			fc.Set( "RetroBlend", Blend );
			fc.Set( "RetroSplit", DebugSplit );
			SetFogAttributes( fc, true );
			_fogCommands.GrabFrameTexture( "ColorBuffer" );
			_fogCommands.Blit( _material );
			InsertCommandList( _fogCommands, Stage.AfterSkybox, 500, "Retro Fog" );
		}

		// Over the HUD, the depth-based features (late fog, edges) cannot follow the screen pass to
		// AfterUI: the interface has no depth, so they would fog and ink the HUD by the wall behind it.
		// They get their own pass on the world at ApplyStage, on the same virtual grid (sampling only,
		// no grading, colour depth or CRT), and the screen pass then dresses everything after the UI.
		var lateFog = DepthFog && !earlyFog;
		var hudEverything = OverHud == RetroHudCoverage.Everything;
		var depthPass = hudEverything && ( lateFog || DepthEdges );

		// Curvature only over the HUD: the world pass runs flat and one warp after the UI bends world and
		// interface together (warping twice would double the bend and leave the HUD's corners unwarped).
		var tubePass = OverHud == RetroHudCoverage.Curvature && Crt && Curvature > 0f;
		if ( depthPass )
		{
			_depthCommands ??= new CommandList( "Retro Depth" );
			_depthCommands.Reset();
			var dc = _depthCommands.Attributes;

			dc.Set( "RetroFogOnly", 0f );
			dc.Set( "RetroCurveOnly", 0f );
			dc.Set( "RetroBlend", Blend );
			dc.Set( "RetroSplit", DebugSplit );
			SetResolutionAttributes( dc, 0f );
			dc.Set( "RetroShockStrength", 0f );
			SetFogAttributes( dc, lateFog );
			SetEdgeAttributes( dc, DepthEdges );
			dc.Set( "RetroGradeEnabled", 0f );
			dc.Set( "RetroQuantEnabled", 0f );
			dc.Set( "RetroPalette", Texture.White, 0 );
			dc.Set( "RetroCrtEnabled", 0f );

			_depthCommands.GrabFrameTexture( "ColorBuffer" );
			_depthCommands.Blit( _material );
			InsertCommandList( _depthCommands, ToEngineStage( ApplyStage ), 500, "Retro Depth" );
		}

		_commands ??= new CommandList( "Retro Filter" );
		_commands.Reset();

		var c = _commands.Attributes;

		c.Set( "RetroFogOnly", 0f );
		c.Set( "RetroCurveOnly", 0f );
		c.Set( "RetroBlend", Blend );
		c.Set( "RetroSplit", DebugSplit );

		// Resolution
		SetResolutionAttributes( c, (LowResolution ? ChromaticAberration : 0f) + HitBoost );

		// Hit ripple: the world point projected each frame (it stays put while the view turns), the ring's
		// radius growing with time, the strength falling away; nothing behind the camera
		var rippleStrength = 0f; var rippleRadius = 0f; var rippleCentre = new Vector2( 0.5f, 0.5f );
		if ( RippleActive && Camera.IsValid() )
		{
			var t01 = Math.Clamp( (Time.Now - _rippleStart) / RippleSeconds, 0f, 1f );
			var screen = Camera.PointToScreenNormal( _rippleWorld, out var behind );
			if ( !behind )
			{
				rippleCentre = screen;
				rippleRadius = t01 * RippleSeconds * RippleSpeed * MathF.Max( Camera.ScreenRect.Height, 1f );
				rippleStrength = RippleStrength * _rippleScale * MathF.Pow( 1f - t01, 1.5f );
			}
		}
		c.Set( "RetroShockCenterX", rippleCentre.x );
		c.Set( "RetroShockCenterY", rippleCentre.y );
		c.Set( "RetroShockRadius", rippleRadius );
		c.Set( "RetroShockWidth", RippleWidth );
		c.Set( "RetroShockStrength", rippleStrength );

		// Depth fog and edges: here only when they are not already in the early fog pass or the depth pass
		SetFogAttributes( c, lateFog && !depthPass );
		SetEdgeAttributes( c, DepthEdges && !depthPass );

		// Grading
		c.Set( "RetroGradeEnabled", Grading ? 1f : 0f );
		c.Set( "RetroBrightness", Brightness );
		c.Set( "RetroContrast", Contrast );
		c.Set( "RetroSaturation", Saturation );
		c.Set( "RetroGamma", Gamma );
		c.Set( "RetroTint", (Vector3) Tint );
		c.Set( "RetroShadowLift", ShadowLift );
		c.Set( "RetroLiftColor", (Vector3) LiftColor );
		c.Set( "RetroLiftRange", LiftRange );
		c.Set( "RetroDuotone", Duotone );
		c.Set( "RetroShadowColor", (Vector3) ShadowColor );
		c.Set( "RetroHighlightColor", (Vector3) HighlightColor );

		// Colour depth + dither. A palette mode with no texture falls back to RGB.
		var quantMode = QuantizeMode;
		var paletteCount = 0;
		if ( quantMode == RetroQuantizeMode.Palette )
		{
			if ( Palette is null || Palette.Width <= 0 )
				quantMode = RetroQuantizeMode.Rgb;
			else
				paletteCount = Math.Min( PaletteColors > 0 ? Math.Min( PaletteColors, Palette.Width ) : Palette.Width, 64 );
		}

		c.Set( "RetroQuantEnabled", ColorDepth ? 1f : 0f );
		c.Set( "RetroQuantMode", (float) (int) quantMode );
		c.Set( "RetroBits", new Vector3( BitsRed, BitsGreen, BitsBlue ) );
		c.Set( "RetroLumSteps", (float) LuminanceSteps );
		c.Set( "RetroPaletteCount", (float) paletteCount );
		c.Set( "RetroPaletteDistance", (float) (int) PaletteDistance );
		c.Set( "RetroPalette", paletteCount > 0 ? Palette : Texture.White, 0 );
		c.Set( "RetroQuantGamma", QuantizeInGamma ? 1f : 0f );
		c.Set( "RetroDitherMode", (float) (int) Dither );
		c.Set( "RetroDitherStrength", DitherStrength );
		c.Set( "RetroDitherScale", (float) DitherScale );
		c.Set( "RetroDitherAnimate", AnimateDither ? 1f : 0f );
		c.Set( "RetroDitherLock", LockDitherToView ? 1f : 0f );
		c.Set( "RetroDitherShift", LockDitherToView ? ViewDitherShift() : Vector2.Zero );

		// CRT
		c.Set( "RetroCrtEnabled", Crt ? 1f : 0f );
		c.Set( "RetroCurvature", tubePass ? 0f : Curvature );
		c.Set( "RetroScanStrength", ScanlineStrength );
		c.Set( "RetroScanPitch", (float) (int) ScanlinePitch );
		c.Set( "RetroScanHardness", ScanlineHardness );
		c.Set( "RetroMaskType", (float) (int) Mask );
		c.Set( "RetroMaskStrength", MaskStrength );
		c.Set( "RetroGridStrength", PixelGridStrength );
		c.Set( "RetroGridWidth", PixelGridWidth );
		c.Set( "RetroVignetteStrength", VignetteStrength );
		c.Set( "RetroVignetteRadius", VignetteRadius );
		c.Set( "RetroVignetteSoftness", VignetteSoftness );
		c.Set( "RetroVignetteRoundness", VignetteRoundness );

		_commands.GrabFrameTexture( "ColorBuffer" );
		_commands.Blit( _material );

		// After the UI stage (7500) the HUD and menus are in the frame; the camera's own overlay
		// list (debug overlays) sits at AfterUI order 5000, so 500 keeps that clean on top.
		InsertCommandList( _commands, hudEverything ? Stage.AfterUI : ToEngineStage( ApplyStage ), 500, "Retro Filter" );

		if ( tubePass )
		{
			_tubeCommands ??= new CommandList( "Retro Tube" );
			_tubeCommands.Reset();
			var tc = _tubeCommands.Attributes;
			tc.Set( "RetroFogOnly", 0f );
			tc.Set( "RetroCurveOnly", 1f );
			tc.Set( "RetroBlend", Blend );
			tc.Set( "RetroSplit", DebugSplit );
			tc.Set( "RetroCurvature", Curvature );
			_tubeCommands.GrabFrameTexture( "ColorBuffer" );
			_tubeCommands.Blit( _material );
			InsertCommandList( _tubeCommands, Stage.AfterUI, 500, "Retro Tube" );
		}
	}

	private void SetResolutionAttributes( CommandList.AttributeAccess c, float aberration )
	{
		c.Set( "RetroResHeight", LowResolution ? (float) ResolvedHeight : 0f );
		c.Set( "RetroPixelAspect", PixelAspect );
		c.Set( "RetroSampleQuality", (float) (int) SampleQuality );
		c.Set( "RetroUpscale", (float) (int) Upscale );
		c.Set( "RetroAberration", aberration );
	}

	private void SetEdgeAttributes( CommandList.AttributeAccess c, bool enabled )
	{
		c.Set( "RetroEdgeEnabled", enabled ? 1f : 0f );
		c.Set( "RetroEdgeColor", (Vector3) EdgeColor );
		c.Set( "RetroEdgeThreshold", EdgeThreshold );
		c.Set( "RetroEdgeStrength", EdgeStrength );
	}

	/// <summary>
	/// How far the dither pattern slides, in virtual pixels, for the camera's current yaw and pitch: the
	/// angle times the focal length measured in virtual pixels, so a point at the screen centre keeps its
	/// dither cell while the view turns. Wide virtual pixels (PixelAspect) make the vertical rate differ.
	/// </summary>
	private Vector2 ViewDitherShift()
	{
		if ( !Camera.IsValid() )
			return Vector2.Zero;

		var rect = Camera.ScreenRect;
		if ( rect.Width <= 0f || rect.Height <= 0f )
			return Vector2.Zero;
		var aspect = rect.Width / rect.Height;

		var pixelAspect = LowResolution ? MathF.Max( PixelAspect, 0.1f ) : 1f;
		var gridW = LowResolution ? MathF.Max( MathF.Round( ResolvedHeight * aspect / pixelAspect ), 1f ) : rect.Width;

		// The composed view (after modifiers such as the FOV punch); the raw camera if it isn't ready yet.
		var view = Camera.View;
		var fov = view.FieldOfView >= 1f ? view.FieldOfView : Camera.FieldOfView;
		var rotation = view.FieldOfView >= 1f ? view.Rotation : Camera.WorldRotation;

		// Horizontal FOV, whichever axis the camera specifies its own on.
		var halfH = fov * 0.5f * MathF.PI / 180f;
		if ( Camera.FovAxis == CameraComponent.Axis.Vertical )
			halfH = MathF.Atan( MathF.Tan( halfH ) * aspect );
		var focal = ( gridW * 0.5f ) / MathF.Max( MathF.Tan( halfH ), 0.001f ); // horizontal virtual px per radian

		var angles = rotation.Angles();
		var yaw = angles.yaw * MathF.PI / 180f;     // + = turning left: the world, and the pattern, move right
		var pitch = angles.pitch * MathF.PI / 180f; // + = looking down: the world, and the pattern, move up
		return new Vector2( yaw * focal, -pitch * focal * pixelAspect );
	}

	private void SetFogAttributes( CommandList.AttributeAccess c, bool enabled )
	{
		c.Set( "RetroFogEnabled", enabled ? 1f : 0f );
		c.Set( "RetroFogColor", (Vector3) FogColor );
		c.Set( "RetroFogStart", FogStart );
		c.Set( "RetroFogEnd", Math.Max( FogEnd, FogStart + 1f ) );
		c.Set( "RetroFogSteps", (float) FogSteps );
		c.Set( "RetroFogCurve", FogCurve );
		c.Set( "RetroFogMax", FogMax );
		c.Set( "RetroFogSky", FogAffectsSky ? 1f : 0f );
		c.Set( "RetroFogSeal", FogSealEdges ? 1f : 0f );
	}
}

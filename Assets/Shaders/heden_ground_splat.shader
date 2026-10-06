// HEDEN ground splat — a textured translucent quad laid flat on a surface: the scorch
// mark a fireball leaves, using Synty's GroundBreak cracks as the stamp. The texture is
// whatever the vmat binds; the shader adds the ember-to-soot cooldown, a radial mask so
// the square quad never shows, and heden_beam's depth feather so the mark hugs uneven
// ground. Driven by GroundScorch through attributes:
//   g_flEmber    1..0  the cooling glow — cracks start hot and fade to soot
//   g_flFade     1..0  global alpha envelope before the mark removes itself
//   g_vEmberColor      the spell's ember palette

HEADER
{
	Description = "HEDEN ground splat — textured surface mark with ember cooldown, unlit translucent";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#include "common/shared.hlsl"
	// Classifies the material as translucent (sbox_pixel.fxc then emits `BoolAttribute( translucent, true )`): drawn
	// AFTER all opaques, once this frame's depth chain exists, and sorted back-to-front. Without it the quad landed in
	// the OPAQUE bucket -- blend states alone do not move it -- and Depth::GetWorldPosition read LAST frame's chain: a
	// still camera matched and the mark held, a moving one reconstructed the floor off the mark's plane and the
	// projected blood dropped out frame by frame (user, 2026-09-22: "they flicker on and off ... only when moving the
	// camera"). heden_water carries the same define for the same reason.
	#define S_TRANSLUCENT 1
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/pixel.hlsl"

	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );

	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default4( 1.0, 1.0, 1.0, 1.0 ) );
	Texture2D g_tColor < Channel( RGBA, Box( TextureColor ), Srgb ); SrgbRead( true ); OutputFormat( BC7 ); >;
	SamplerState g_sTexture < Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	float g_flEmber < Attribute( "g_flEmber" ); Default( 0.0 ); >;
	float g_flFade < Attribute( "g_flFade" ); Default( 1.0 ); >;
	float3 g_vEmberColor < Attribute( "g_vEmberColor" ); Default3( 1.0, 0.8, 0.3 ); >;

	// The cold mark's tint — near-black soot — and its resting opacity.
	float3 g_vSootColor < UiType( Color ); Default3( 0.05, 0.045, 0.04 ); UiGroup( "Splat,10/10" ); >;
	float g_flOpacity < Default( 0.85 ); Range( 0.0, 1.0 ); UiGroup( "Splat,10/11" ); >;

	// World units over which the mark fades where it meets geometry it crosses. Must stay
	// UNDER the quad's lift off its own surface (BuildGroundScorch's 4 units), or the
	// feather fades the whole mark against the very ground it lies on and it only shows
	// at grazing view angles.
	float g_flDepthFeather < Default( 3.0 ); Range( 0.0, 64.0 ); UiGroup( "Splat,10/20" ); >;

	// Projected marks (blood, potion splashes; GroundScorch.Place project: true). The quad is only the area the mark
	// may draw in: every pixel paints the scene surface BEHIND it, looked up from the depth buffer and mapped into the
	// mark's own plane (origin on the surface, U/V axes already divided by the mark's diameter). A pixel whose surface
	// is not near that plane (the quad hanging past a table edge or a step, the crate it landed on broken away, a
	// wall face at right angles) is simply not drawn, so a mark can never hang in the air.
	float g_flProject < Attribute( "g_flProject" ); Default( 0.0 ); >;
	float3 g_vSplatOrigin < Attribute( "g_vSplatOrigin" ); Default3( 0.0, 0.0, 0.0 ); >;
	float3 g_vSplatNormal < Attribute( "g_vSplatNormal" ); Default3( 0.0, 0.0, 1.0 ); >;
	float3 g_vSplatU < Attribute( "g_vSplatU" ); Default3( 1.0, 0.0, 0.0 ); >;
	float3 g_vSplatV < Attribute( "g_vSplatV" ); Default3( 0.0, 1.0, 0.0 ); >;
	// how far off the mark's plane a surface may be and still take it (world units)
	float g_flSplatDepth < Attribute( "g_flSplatDepth" ); Default( 5.0 ); >;
	// the quad's size over the mark's (the quad is drawn bigger so the lifted quad still covers the whole mark)
	float g_flSplatOversize < Attribute( "g_flSplatOversize" ); Default( 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float project = step( 0.5, g_flProject );

		// every gradient is taken up front, outside any branch or clip
		float2 quadUv = i.vTextureCoords.xy;
		float2 quadDx = ddx( quadUv );
		float2 quadDy = ddy( quadUv );
		float3 scenePos = Depth::GetWorldPosition( i.vPositionSs.xy );
		float3 sceneDx = ddx( scenePos );
		float3 sceneDy = ddy( scenePos );

		float3 d = scenePos - g_vSplatOrigin;
		float height = dot( d, g_vSplatNormal );
		float2 projectedUv = float2( dot( d, g_vSplatU ), dot( d, g_vSplatV ) ) + 0.5;
		float2 uv = lerp( quadUv, projectedUv, project );

		// the surface's own facing, from the depth positions: a face turned away from the mark's plane (the side of a
		// step, a table's edge) fades out instead of taking a smeared stretch of the stamp
		float facing = abs( dot( normalize( cross( sceneDx, sceneDy ) + 1e-6 ), g_vSplatNormal ) );
		float projectedFade = saturate( ( facing - 0.45 ) / 0.25 )
			* ( 1.0 - smoothstep( g_flSplatDepth * 0.6, g_flSplatDepth, abs( height ) ) )
			* step( abs( projectedUv.x - 0.5 ), 0.5 ) * step( abs( projectedUv.y - 0.5 ), 0.5 );
		float surfaceFade = lerp( 1.0, projectedFade, project );

		// mip selection from the quad's own UVs (the projected ones jump at depth edges and would pick a blurry mip there)
		float gradScale = lerp( 1.0, g_flSplatOversize, project );
		float4 tex = g_tColor.SampleGrad( g_sTexture, uv, quadDx * gradScale, quadDy * gradScale );

		// Soft radial mask: whatever the stamp's own alpha does, the square quad's edges
		// and corners must never print.
		float2 p = uv * 2.0 - 1.0;
		float mask = 1.0 - smoothstep( 0.8, 1.0, length( p ) );

		// Ember-hot cracks cooling into soot. The ember pass runs over 1 to feed bloom
		// while it lasts; the resting mark is flat dark matter.
		float3 color = lerp( g_vSootColor, g_vEmberColor * 1.8, saturate( g_flEmber ) );
		color *= tex.rgb;

		float alpha = tex.a * mask * g_flOpacity * saturate( g_flFade ) * surfaceFade;

		// The mark melts into bumps and props rather than slicing them — heden_beam's
		// soft landing. (A projected mark already lies on the surface itself.)
		if ( g_flDepthFeather > 0 && g_flProject < 0.5 )
		{
			float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
			alpha *= clamp( distance( scenePos, worldPos ) / g_flDepthFeather, 0.0, 1.0 );
		}

		clip( alpha - 0.001 );

		return float4( color, saturate( alpha ) );
	}
}

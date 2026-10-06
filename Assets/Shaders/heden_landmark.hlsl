#ifndef HEDEN_LANDMARK_HLSL
#define HEDEN_LANDMARK_HLSL

//
// Landmark rendering — one object allowed past the camera far plane.
//
// The worldroot tree must read from ~9000 units while the camera's ZFar stays at 7500
// and the world's gradient fog goes fully opaque at 7000. Raising ZFar globally would
// start drawing every fully-fogged StreamedChunk between 7500 and its ~10000+ cull
// distance, so instead the landmark's own batch opts in via a per-SceneObject attribute
// (set by NodeRenderBatcher on the node's private instanced group) and the shaders do
// two things:
//
//   VS  Landmark_Project — view depth beyond 97% of the far plane is compressed into
//       the last sliver before it, along the vertex's own camera ray. The ray direction
//       is unchanged, so the screen position is identical; the mapping is monotone in
//       depth, so self-occlusion stays correct. Ordering against UNsquashed geometry can
//       only go wrong beyond 0.97 * ZFar — where everything is 100% fog already.
//       Runs in Depth mode too, deliberately: the main camera's depth prepass must land
//       on the same depth as the forward pass. In sun shadow passes the "camera" is the
//       light; moving a caster along the light ray does not move its shadow.
//
//   PS  Landmark_Fog — gradient fog with its END stretched to the landmark's distance
//       (start unchanged), so the tree keeps fading over the whole run and reaches full
//       sky colour exactly where WorldStreamingService culls it. Everything else keeps
//       the scene's fog verbatim.
//
// The attribute defaults to 0 → every other object compiles to the stock paths.
//

// Per-SceneObject (NodeRenderBatcher). 0 = not a landmark.
float g_flHedenLandmarkFarZ < Attribute( "HedenLandmarkFarZ" ); Default( 0.0 ); >;

#if ( PROGRAM == VFX_PROGRAM_VS )

float4 Landmark_Project( float3 vPositionWs )
{
	if ( g_flHedenLandmarkFarZ > 0.0 )
	{
		float3 vToVertex = vPositionWs - g_vCameraPositionWs.xyz;
		float flDepth = dot( vToVertex, g_vCameraDirWs.xyz );
		float flLim = 0.97 * g_flFarPlane;

		if ( flDepth > flLim )
		{
			// 1024 of headroom past the cull distance covers the model's own radius —
			// the streaming position is the trunk pivot, the canopy reaches further.
			float flTarget = 0.995 * g_flFarPlane;
			float flScale = ( flTarget - flLim ) / max( g_flHedenLandmarkFarZ + 1024.0 - flLim, 1.0 );
			float flNewDepth = flLim + ( flDepth - flLim ) * flScale;
			vPositionWs = g_vCameraPositionWs.xyz + vToVertex * ( flNewDepth / flDepth );
		}
	}

	return Position3WsToPs( vPositionWs );
}

#endif // VFX_PROGRAM_VS

#if ( PROGRAM == VFX_PROGRAM_PS )

#include "common/classes/Fog.hlsl"

float3 Landmark_Fog( float3 vPositionWs, float2 vScreenPos, float3 vColor )
{
	if ( g_flHedenLandmarkFarZ <= 0.0 )
		return Fog::Apply( vPositionWs, vScreenPos, vColor );

	float3 vToCamera = vPositionWs - g_vCameraPositionWs;

	// Gradient fog re-derived from the engine constants (vr_gradient_fog.fxc), horizontal
	// end remapped from the scene's EndDistance to the landmark distance. Same culling
	// branch as the stock path — with fog disabled its near distance is Inf and this skips.
	float flDistSqr = dot( vToCamera.xy, vToCamera.xy );
	float flScaleH = g_vGradientFogBiasAndScale.z;
	[branch]
	if ( flScaleH > 0.0 && flDistSqr > g_vGradientFogCullingParams.x && vPositionWs.z < g_vGradientFogCullingParams.y )
	{
		float flStart = -g_vGradientFogBiasAndScale.x / flScaleH;
		float flNormH = saturate( ( length( vToCamera.xy ) - flStart ) / max( g_flHedenLandmarkFarZ - flStart, 1.0 ) );
		float flNormV = saturate( g_vGradientFogBiasAndScale.y + g_vGradientFogBiasAndScale.w * vPositionWs.z );
		float flLevel = pow( flNormH, m_vGradientFogExponents.x ) * pow( flNormV, m_vGradientFogExponents.y ) * g_vGradientFogColor_Opacity.w;
		vColor = lerp( vColor, g_vGradientFogColor_Opacity.rgb, flLevel );
	}

	// Mirror the remainder of Fog::Apply so a landmark still honours the other fogs.
	vColor = ApplyCubemapFog( vColor, vPositionWs, vToCamera );
	vColor = ApplyVolumetricFog( vColor, vPositionWs, vScreenPos );
	return vColor;
}

#endif // VFX_PROGRAM_PS

#endif // HEDEN_LANDMARK_HLSL

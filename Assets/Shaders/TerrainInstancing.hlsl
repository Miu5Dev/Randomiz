#ifndef RANDOMIZ_TERRAIN_INSTANCING_INCLUDED
#define RANDOMIZ_TERRAIN_INSTANCING_INCLUDED

// Shared "Draw Instanced" terrain support for Custom/TerrainLayered.
// When the Terrain component has "Draw Instanced" enabled, Unity submits a single
// flat patch grid per tile and expects the vertex shader to rebuild the surface by
// sampling the terrain heightmap. The per-vertex normal is read from the terrain
// normal map (not the per-pixel keyword) so the slope-based layer blend computed in
// the vertex stage keeps working.
//
// Requirements for every pass that includes this file:
//   * #pragma multi_compile_instancing
//   * #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
//   * _TerrainHeightmapScale / _TerrainHeightmapRecipSize declared inside UnityPerMaterial,
//     guarded by UNITY_INSTANCING_ENABLED.
//   * Core.hlsl included beforehand (provides UnpackHeightmap + the instancing macros).

#ifdef UNITY_INSTANCING_ENABLED
    TEXTURE2D(_TerrainHeightmapTexture);
    TEXTURE2D(_TerrainNormalmapTexture);
    SAMPLER(sampler_TerrainNormalmapTexture);
#endif

UNITY_INSTANCING_BUFFER_START(Terrain)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData) // (xBase, yBase, skipScale, ~)
UNITY_INSTANCING_BUFFER_END(Terrain)

// Rebuilds object-space position, normal and terrain UV from the heightmap for the
// instanced patch currently being drawn. No-op when instancing is disabled, so it is
// safe to call unconditionally from every vertex stage.
void TerrainInstancing(inout float4 positionOS, inout float3 normalOS, inout float2 uv)
{
#ifdef UNITY_INSTANCING_ENABLED
    float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
    float2 sampleCoords = (positionOS.xy + instanceData.xy) * instanceData.z; // (vertex + base) * skipScale
    float  height       = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

    positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
    positionOS.y  = height * _TerrainHeightmapScale.y;
    normalOS      = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2.0 - 1.0;
    uv            = sampleCoords * _TerrainHeightmapRecipSize.zw;
#endif
}

#endif // RANDOMIZ_TERRAIN_INSTANCING_INCLUDED

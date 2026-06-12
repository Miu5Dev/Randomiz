// TerrainLayered.shader - URP
// Cel-shaded terrain | height/slope blend | splatmap paint | day/night cycle
// Shadow coord is computed PER-FRAGMENT: a per-vertex coord breaks at shadow cascade
//   boundaries and draws a ring-shaped false shadow on the terrain.
// Noise + blend weights are computed in the vertex shader -> no per-pixel cost.
Shader "Custom/TerrainLayered"
{
    Properties
    {
        [HideInInspector] _Control  ("Splatmap",  2D) = "red"   {}
        [HideInInspector] _MainTex  ("Main Tex",  2D) = "white" {}

        _Splat0  ("Layer 0 Albedo (Grass)", 2D) = "white" {}
        _Normal0 ("Layer 0 Normal (Grass)", 2D) = "bump"  {}
        _Tiling0 ("Layer 0 Tiling",        Float) = 8.0

        _Splat1  ("Layer 1 Albedo (Dirt)",  2D) = "white" {}
        _Normal1 ("Layer 1 Normal (Dirt)",  2D) = "bump"  {}
        _Tiling1 ("Layer 1 Tiling",         Float) = 6.0

        _Splat2  ("Layer 2 Albedo (Stone)", 2D) = "white" {}
        _Normal2 ("Layer 2 Normal (Stone)", 2D) = "bump"  {}
        _Tiling2 ("Layer 2 Tiling",         Float) = 5.0

        _Splat3  ("Layer 3 Albedo (Snow)",  2D) = "white" {}
        _Normal3 ("Layer 3 Normal (Snow)",  2D) = "bump"  {}
        _Tiling3 ("Layer 3 Tiling",         Float) = 4.0

        _HeightGrassTop  ("Grass Top Y",         Float)        = 38.0
        _HeightDirtTop   ("Dirt Top Y",           Float)        = 55.0
        _HeightStoneTop  ("Stone Top Y",          Float)        = 75.0
        _BlendWidth      ("Blend Width",          Float)        = 6.0

        _SlopeStoneAngle ("Stone Slope Start",    Range(20,80)) = 35.0
        _SlopeStoneFull  ("Stone Slope Full",     Range(25,90)) = 55.0
        _SlopeSnowAngle  ("Snow Disappear Start", Range(30,90)) = 65.0
        _SlopeSnowFull   ("Snow Disappear Full",  Range(35,90)) = 80.0

        _PaintBlend      ("Paint Brush Influence", Range(0,1))  = 1.0

        _CelBands        ("Cel Bands",            Range(1,8))   = 3.0
        _CelShadowColor  ("Cel Shadow Color",     Color)        = (0.18,0.18,0.25,1.0)

        _NormalStrength  ("Normal Strength",      Range(0,2))   = 1.0
        _AmbientStrength ("Skybox Ambient",       Range(0,1))   = 0.25

        _NoiseScale      ("Noise Scale",          Float)        = 0.03
        _NoiseStrength   ("Noise Strength",       Range(0,1))   = 0.35
        _MacroScale      ("Macro Scale",          Float)        = 0.008
        _MacroStrength   ("Macro Strength",       Range(0,1))   = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType"        = "Opaque"
            "RenderPipeline"    = "UniversalPipeline"
            "Queue"             = "Geometry"
            "TerrainCompatible" = "True"
        }
        LOD 300

        // =====================================================================
        // Pass 0 - ForwardLit
        // =====================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Terrain "Draw Instanced": patches share the terrain transform, so no per-instance matrices.
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma multi_compile_local __ _TERRAIN_BASE_MAP_GEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Control); SAMPLER(sampler_Control);
            TEXTURE2D(_Splat0);  SAMPLER(sampler_Splat0);
            TEXTURE2D(_Normal0); SAMPLER(sampler_Normal0);
            TEXTURE2D(_Splat1);  SAMPLER(sampler_Splat1);
            TEXTURE2D(_Normal1); SAMPLER(sampler_Normal1);
            TEXTURE2D(_Splat2);  SAMPLER(sampler_Splat2);
            TEXTURE2D(_Normal2); SAMPLER(sampler_Normal2);
            TEXTURE2D(_Splat3);  SAMPLER(sampler_Splat3);
            TEXTURE2D(_Normal3); SAMPLER(sampler_Normal3);

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST;
                float4 _MainTex_ST;
                float  _Tiling0, _Tiling1, _Tiling2, _Tiling3;
                float  _HeightGrassTop, _HeightDirtTop, _HeightStoneTop, _BlendWidth;
                float  _SlopeStoneAngle, _SlopeStoneFull;
                float  _SlopeSnowAngle,  _SlopeSnowFull;
                float  _PaintBlend;
                float  _CelBands;
                float4 _CelShadowColor;
                float  _NormalStrength;
                float  _AmbientStrength;
                float  _NoiseScale, _NoiseStrength;
                float  _MacroScale, _MacroStrength;

                // Heightmap params, set by Unity only when the Terrain uses Draw Instanced.
                #ifdef UNITY_INSTANCING_ENABLED
                    float4 _TerrainHeightmapRecipSize; // (1/w, 1/h, 1/(w-1), 1/(h-1))
                    float4 _TerrainHeightmapScale;     // (scale.x, scale.y/maxHeight, scale.z, 0)
                #endif
            CBUFFER_END

            // Heightmap-based vertex rebuild for the Terrain "Draw Instanced" path.
            #include "TerrainInstancing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
                // Blend weights computed in the vertex shader (cheap, avoids per-pixel noise).
                float4 layerWeights : TEXCOORD4; // x=grass y=dirt z=stone w=snow
                float  macroVal     : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // -- Noise (solo usada en vertice) ---------------------------------
            float2 hash2v(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }
            float valueNoiseV(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash2v(i).x;
                float b = hash2v(i + float2(1,0)).x;
                float c = hash2v(i + float2(0,1)).x;
                float d = hash2v(i + float2(1,1)).x;
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }
            float fbmV(float2 p)
            {
                float v = 0.0; float a = 0.5;
                [unroll] for (int i = 0; i < 2; i++) { v += a * valueNoiseV(p); p *= 2.0; a *= 0.5; }
                return v;
            }

            // -- Helpers -------------------------------------------------------
            float smoothBlend(float x, float center, float width)
            {
                return saturate((x - (center - width * 0.5)) / max(width, 0.001));
            }
            float3 UnpackNormalScaled(float4 packed, float scale)
            {
                float3 n = UnpackNormal(packed); n.xy *= scale; return normalize(n);
            }
            float CelQuantize(float lit, float bands)
            {
                return floor(lit * bands) / max(bands - 1.0, 1.0);
            }
            float3 TangentToWorldNormal(float3 tn, float3 nWS)
            {
                float3 up = abs(nWS.y) < 0.999 ? float3(0,1,0) : float3(1,0,0);
                float3 t  = normalize(cross(up, nWS));
                float3 b  = normalize(cross(nWS, t));
                return normalize(tn.x * t + tn.y * b + tn.z * nWS);
            }

            // -- Vertex --------------------------------------------------------
            Varyings vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Rebuild this patch from the heightmap when Draw Instanced is on (no-op otherwise).
                TerrainInstancing(IN.positionOS, IN.normalOS, IN.uv);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS  = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = nrm.normalWS;
                OUT.uv          = IN.uv;
                OUT.fogFactor   = ComputeFogFactor(pos.positionCS.z);

                // FIX 2: calcular blend weights y noise en vertice
                float2 wXZ    = pos.positionWS.xz;
                float  worldY = pos.positionWS.y;
                float3 normWS = nrm.normalWS;

                float noiseVal = fbmV(wXZ * _NoiseScale);
                float macroVal = fbmV(wXZ * _MacroScale);
                OUT.macroVal   = macroVal;

                float yNoisy = worldY
                    + (noiseVal - 0.5) * 2.0 * _NoiseStrength * 12.0
                    + (macroVal - 0.5) * 2.0 * _MacroStrength * 20.0;

                float slopeDeg    = degrees(acos(saturate(normWS.y)));
                float w_dirt      = smoothBlend(yNoisy, _HeightGrassTop, _BlendWidth);
                float w_stone     = smoothBlend(yNoisy, _HeightDirtTop,  _BlendWidth);
                float w_snow      = smoothBlend(yNoisy, _HeightStoneTop, _BlendWidth);

                float wGrass = (1.0 - w_dirt);
                float wDirt  = w_dirt  * (1.0 - w_stone);
                float wStone = w_stone * (1.0 - w_snow);
                float wSnow  = w_snow;

                float slopeStoneW = saturate((slopeDeg - _SlopeStoneAngle) / max(_SlopeStoneFull - _SlopeStoneAngle, 0.001));
                float slopeNoSnow = saturate((slopeDeg - _SlopeSnowAngle)  / max(_SlopeSnowFull  - _SlopeSnowAngle,  0.001));

                float snowBonus = wSnow * slopeNoSnow;
                wSnow  = wSnow * (1.0 - slopeNoSnow);
                wStone = wStone + snowBonus;

                float flatTotal = wGrass + wDirt;
                wGrass -= wGrass * slopeStoneW;
                wDirt  -= wDirt  * slopeStoneW;
                wStone += flatTotal * slopeStoneW;

                float wTotal = max(wGrass + wDirt + wStone + wSnow, 0.001);
                OUT.layerWeights = float4(wGrass, wDirt, wStone, wSnow) / wTotal;

                return OUT;
            }

            // -- Fragment (solo textura + iluminacion, sin noise) ---------------
            half4 frag(Varyings IN) : SV_Target
            {
                float2 wXZ  = IN.positionWS.xz;
                float3 normWS = normalize(IN.normalWS);
                float4 lw   = IN.layerWeights;

                // Texturas de albedo (un sample por capa, sin stochastic en frag)
                float3 alb0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, wXZ * _Tiling0).rgb;
                float3 alb1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat1, wXZ * _Tiling1).rgb;
                float3 alb2 = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat2, wXZ * _Tiling2).rgb;
                float3 alb3 = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat3, wXZ * _Tiling3).rgb;

                // Normales
                float3 nrm0 = UnpackNormalScaled(SAMPLE_TEXTURE2D(_Normal0, sampler_Normal0, wXZ * _Tiling0), _NormalStrength);
                float3 nrm1 = UnpackNormalScaled(SAMPLE_TEXTURE2D(_Normal1, sampler_Normal1, wXZ * _Tiling1), _NormalStrength);
                float3 nrm2 = UnpackNormalScaled(SAMPLE_TEXTURE2D(_Normal2, sampler_Normal2, wXZ * _Tiling2), _NormalStrength);
                float3 nrm3 = UnpackNormalScaled(SAMPLE_TEXTURE2D(_Normal3, sampler_Normal3, wXZ * _Tiling3), _NormalStrength);

                float3 albAuto = alb0*lw.x + alb1*lw.y + alb2*lw.z + alb3*lw.w;
                float3 nrmAuto = nrm0*lw.x + nrm1*lw.y + nrm2*lw.z + nrm3*lw.w;

                // Splatmap painting
                float4 splat    = SAMPLE_TEXTURE2D(_Control, sampler_Control, IN.uv);
                float  splatSum = splat.r + splat.g + splat.b + splat.a;
                float  paintedWeight = saturate(splat.g + splat.b + splat.a);
                float  paintMask = saturate(paintedWeight + (1.0 - splat.r) * step(0.001, splatSum));
                paintMask = saturate(paintMask * _PaintBlend);
                splat /= max(splatSum, 0.001);

                float3 albPaint = alb0*splat.r + alb1*splat.g + alb2*splat.b + alb3*splat.a;
                float3 nrmPaint = nrm0*splat.r + nrm1*splat.g + nrm2*splat.b + nrm3*splat.a;

                float3 albedo     = lerp(albAuto, albPaint, paintMask);
                float3 tangentNrm = lerp(nrmAuto, nrmPaint, paintMask);
                albedo *= lerp(0.85, 1.15, IN.macroVal);

                float3 N = TangentToWorldNormal(tangentNrm, normWS);

                // Shadow coord computed PER-PIXEL. A per-vertex coord selects the shadow
                // cascade per vertex; interpolating it across a cascade boundary draws a
                // ring-shaped false shadow on the big terrain triangles. Per-fragment picks
                // the correct cascade for each pixel.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight  = GetMainLight(shadowCoord, IN.positionWS, half4(1,1,1,1));

                float  sunHeight  = saturate(mainLight.direction.y * 2.0 + 0.1);
                float  sunFade    = smoothstep(0.0, 0.25, sunHeight);
                float3 mainColor  = mainLight.color * sunFade;
                float  mainShadow = mainLight.shadowAttenuation * sunFade;

                float  NdotL  = saturate(dot(N, mainLight.direction));
                float  lit    = NdotL * mainShadow;
                float  celLit = CelQuantize(lit, _CelBands);
                float3 col    = lerp(_CelShadowColor.rgb, mainColor, celLit);

                float  ambientOcc = saturate(NdotL * 0.5 + 0.5) * mainShadow;
                float3 ambient    = SampleSH(N) * ambientOcc * _AmbientStrength;
                col += ambient;

                #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS)
                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.positionWS;
                inputData.normalWS                = N;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask              = half4(1,1,1,1);

                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light al  = GetAdditionalLight(lightIndex, IN.positionWS);
                    float ndl = saturate(dot(N, al.direction));
                    float cl  = CelQuantize(ndl * al.shadowAttenuation, _CelBands);
                    col      += lerp(_CelShadowColor.rgb, al.color, cl) * al.distanceAttenuation * albedo;
                LIGHT_LOOP_END
                #endif

                float3 finalColor = albedo * col;
                finalColor = MixFog(finalColor, IN.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // =====================================================================
        // Pass 1 - ShadowCaster
        // =====================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma multi_compile_shadowcaster
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST; float4 _MainTex_ST;
                float  _Tiling0, _Tiling1, _Tiling2, _Tiling3;
                float  _HeightGrassTop, _HeightDirtTop, _HeightStoneTop, _BlendWidth;
                float  _SlopeStoneAngle, _SlopeStoneFull;
                float  _SlopeSnowAngle,  _SlopeSnowFull;
                float  _PaintBlend; float _CelBands; float4 _CelShadowColor;
                float  _NormalStrength; float _AmbientStrength;
                float  _NoiseScale, _NoiseStrength;
                float  _MacroScale, _MacroStrength;
                #ifdef UNITY_INSTANCING_ENABLED
                    float4 _TerrainHeightmapRecipSize;
                    float4 _TerrainHeightmapScale;
                #endif
            CBUFFER_END

            #include "TerrainInstancing.hlsl"

            struct ShadowAttr { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ShadowVary { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            ShadowVary shadowVert(ShadowAttr IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ShadowVary OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float2 terrainUV = 0.0;
                TerrainInstancing(IN.positionOS, IN.normalOS, terrainUV);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                #ifdef _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(posWS - _LightPosition.xyz);
                #else
                    float3 lightDir = _MainLightPosition.xyz;
                #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, lightDir));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = posCS;
                return OUT;
            }
            half4 shadowFrag(ShadowVary IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // =====================================================================
        // Pass 2 - DepthOnly
        // =====================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex   depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST; float4 _MainTex_ST;
                float  _Tiling0, _Tiling1, _Tiling2, _Tiling3;
                float  _HeightGrassTop, _HeightDirtTop, _HeightStoneTop, _BlendWidth;
                float  _SlopeStoneAngle, _SlopeStoneFull; float _SlopeSnowAngle, _SlopeSnowFull;
                float  _PaintBlend; float _CelBands; float4 _CelShadowColor;
                float  _NormalStrength; float _AmbientStrength;
                float  _NoiseScale, _NoiseStrength; float _MacroScale, _MacroStrength;
                #ifdef UNITY_INSTANCING_ENABLED
                    float4 _TerrainHeightmapRecipSize;
                    float4 _TerrainHeightmapScale;
                #endif
            CBUFFER_END

            #include "TerrainInstancing.hlsl"

            struct DepthAttr { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVary { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            DepthVary depthVert(DepthAttr IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN); DepthVary OUT; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 terrainNrm = 0.0; float2 terrainUV = 0.0;
                TerrainInstancing(IN.positionOS, terrainNrm, terrainUV);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 depthFrag(DepthVary IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // =====================================================================
        // Pass 3 - DepthNormals
        // =====================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back

            HLSLPROGRAM
            #pragma vertex   depthNormVert
            #pragma fragment depthNormFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST; float4 _MainTex_ST;
                float  _Tiling0, _Tiling1, _Tiling2, _Tiling3;
                float  _HeightGrassTop, _HeightDirtTop, _HeightStoneTop, _BlendWidth;
                float  _SlopeStoneAngle, _SlopeStoneFull; float _SlopeSnowAngle, _SlopeSnowFull;
                float  _PaintBlend; float _CelBands; float4 _CelShadowColor;
                float  _NormalStrength; float _AmbientStrength;
                float  _NoiseScale, _NoiseStrength; float _MacroScale, _MacroStrength;
                #ifdef UNITY_INSTANCING_ENABLED
                    float4 _TerrainHeightmapRecipSize;
                    float4 _TerrainHeightmapScale;
                #endif
            CBUFFER_END

            #include "TerrainInstancing.hlsl"

            struct DNAttr { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DNVary  { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            DNVary depthNormVert(DNAttr IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN); DNVary OUT; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float2 terrainUV = 0.0;
                TerrainInstancing(IN.positionOS, IN.normalOS, terrainUV);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }
            float4 depthNormFrag(DNVary IN) : SV_Target
            {
                return float4(normalize(IN.normalWS) * 0.5 + 0.5, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

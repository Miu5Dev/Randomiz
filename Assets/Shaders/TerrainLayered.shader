Shader "Custom/TerrainLayered"
{
    Properties
    {
        [HideInInspector] _Control ("Splatmap", 2D) = "red" {}

        _Albedo0 ("Layer 0 Albedo (Grass)", 2D) = "white" {}
        _Normal0 ("Layer 0 Normal (Grass)", 2D) = "bump"  {}
        _Tiling0 ("Layer 0 Tiling",        Float) = 8.0

        _Albedo1 ("Layer 1 Albedo (Dirt)",  2D) = "white" {}
        _Normal1 ("Layer 1 Normal (Dirt)",  2D) = "bump"  {}
        _Tiling1 ("Layer 1 Tiling",         Float) = 6.0

        _Albedo2 ("Layer 2 Albedo (Stone)", 2D) = "white" {}
        _Normal2 ("Layer 2 Normal (Stone)", 2D) = "bump"  {}
        _Tiling2 ("Layer 2 Tiling",         Float) = 5.0

        _Albedo3 ("Layer 3 Albedo (Snow)",  2D) = "white" {}
        _Normal3 ("Layer 3 Normal (Snow)",  2D) = "bump"  {}
        _Tiling3 ("Layer 3 Tiling",         Float) = 4.0

        _HeightGrassTop  ("Grass Top Y",          Float)       = 38.0
        _HeightDirtTop   ("Dirt Top Y",           Float)       = 55.0
        _HeightStoneTop  ("Stone Top Y",          Float)       = 75.0
        _BlendWidth      ("Blend Width",          Float)       = 6.0

        _SlopeStoneAngle ("Stone Slope Start",    Range(20,80)) = 35.0
        _SlopeStoneFull  ("Stone Slope Full",     Range(25,90)) = 55.0
        _SlopeSnowAngle  ("Snow Disappear Start", Range(30,90)) = 65.0
        _SlopeSnowFull   ("Snow Disappear Full",  Range(35,90)) = 80.0

        _PaintBlend      ("Paint Brush Influence", Range(0,1))  = 0.0

        _CelBands        ("Cel Bands",            Range(1,8))   = 3.0
        _CelShadowColor  ("Cel Shadow Color",     Color)        = (0.18, 0.18, 0.25, 1.0)

        _NormalStrength  ("Normal Strength",      Range(0,2))   = 1.0

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

        // ------------------------------------------------------------------
        // Forward Lit
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Control); SAMPLER(sampler_Control);
            TEXTURE2D(_Albedo0); SAMPLER(sampler_Albedo0);
            TEXTURE2D(_Normal0); SAMPLER(sampler_Normal0);
            TEXTURE2D(_Albedo1); SAMPLER(sampler_Albedo1);
            TEXTURE2D(_Normal1); SAMPLER(sampler_Normal1);
            TEXTURE2D(_Albedo2); SAMPLER(sampler_Albedo2);
            TEXTURE2D(_Normal2); SAMPLER(sampler_Normal2);
            TEXTURE2D(_Albedo3); SAMPLER(sampler_Albedo3);
            TEXTURE2D(_Normal3); SAMPLER(sampler_Normal3);

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST;
                float _Tiling0, _Tiling1, _Tiling2, _Tiling3;
                float _HeightGrassTop, _HeightDirtTop, _HeightStoneTop, _BlendWidth;
                float _SlopeStoneAngle, _SlopeStoneFull;
                float _SlopeSnowAngle,  _SlopeSnowFull;
                float _PaintBlend;
                float _CelBands;
                float4 _CelShadowColor;
                float _NormalStrength;
                float _NoiseScale, _NoiseStrength;
                float _MacroScale, _MacroStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash2(i).x;
                float b = hash2(i + float2(1,0)).x;
                float c = hash2(i + float2(0,1)).x;
                float d = hash2(i + float2(1,1)).x;
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0; float a = 0.5;
                for (int i = 0; i < 2; i++) { v += a * valueNoise(p); p *= 2.0; a *= 0.5; }
                return v;
            }

            float3 sampleStoch(TEXTURE2D_PARAM(tex, samp), float2 uv)
            {
                float2 offset = (hash2(floor(uv * 4.0)) * 2.0 - 1.0) * 0.25;
                float  blend  = valueNoise(uv * 3.7);
                return lerp(SAMPLE_TEXTURE2D(tex, samp, uv).rgb,
                            SAMPLE_TEXTURE2D(tex, samp, uv + offset).rgb,
                            smoothstep(0.3, 0.7, blend));
            }

            float4 sampleStochN(TEXTURE2D_PARAM(tex, samp), float2 uv)
            {
                float2 offset = (hash2(floor(uv * 4.0)) * 2.0 - 1.0) * 0.25;
                float  blend  = valueNoise(uv * 3.7);
                return lerp(SAMPLE_TEXTURE2D(tex, samp, uv),
                            SAMPLE_TEXTURE2D(tex, samp, uv + offset),
                            smoothstep(0.3, 0.7, blend));
            }

            float smoothBlend(float x, float center, float width)
            {
                return saturate((x - (center - width * 0.5)) / max(width, 0.001));
            }

            float3 UnpackNormalScaled(float4 packed, float scale)
            {
                float3 n = UnpackNormal(packed);
                n.xy *= scale;
                return normalize(n);
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

            Varyings vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.uv         = IN.uv;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 wXZ    = IN.positionWS.xz;
                float  worldY = IN.positionWS.y;
                float3 normWS = normalize(IN.normalWS);
                float  slopeDeg = degrees(acos(saturate(normWS.y)));

                float noiseVal = fbm(wXZ * _NoiseScale);
                float macroVal = fbm(wXZ * _MacroScale);
                float yNoisy   = worldY
                    + (noiseVal - 0.5) * 2.0 * _NoiseStrength * 12.0
                    + (macroVal - 0.5) * 2.0 * _MacroStrength * 20.0;

                float3 alb0 = sampleStoch(_Albedo0, sampler_Albedo0, wXZ * _Tiling0);
                float3 alb1 = sampleStoch(_Albedo1, sampler_Albedo1, wXZ * _Tiling1);
                float3 alb2 = sampleStoch(_Albedo2, sampler_Albedo2, wXZ * _Tiling2);
                float3 alb3 = sampleStoch(_Albedo3, sampler_Albedo3, wXZ * _Tiling3);

                float3 nrm0 = UnpackNormalScaled(sampleStochN(_Normal0, sampler_Normal0, wXZ * _Tiling0), _NormalStrength);
                float3 nrm1 = UnpackNormalScaled(sampleStochN(_Normal1, sampler_Normal1, wXZ * _Tiling1), _NormalStrength);
                float3 nrm2 = UnpackNormalScaled(sampleStochN(_Normal2, sampler_Normal2, wXZ * _Tiling2), _NormalStrength);
                float3 nrm3 = UnpackNormalScaled(sampleStochN(_Normal3, sampler_Normal3, wXZ * _Tiling3), _NormalStrength);

                float w_dirt  = smoothBlend(yNoisy, _HeightGrassTop, _BlendWidth);
                float w_stone = smoothBlend(yNoisy, _HeightDirtTop,  _BlendWidth);
                float w_snow  = smoothBlend(yNoisy, _HeightStoneTop, _BlendWidth);

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
                wGrass /= wTotal; wDirt /= wTotal; wStone /= wTotal; wSnow /= wTotal;

                float3 albAuto = alb0*wGrass + alb1*wDirt + alb2*wStone + alb3*wSnow;
                float3 nrmAuto = nrm0*wGrass + nrm1*wDirt + nrm2*wStone + nrm3*wSnow;

                float4 splat = SAMPLE_TEXTURE2D(_Control, sampler_Control, IN.uv);
                float  splatSum = max(splat.r+splat.g+splat.b+splat.a, 0.001);
                splat /= splatSum;

                float3 albedo     = lerp(albAuto, alb0*splat.r + alb1*splat.g + alb2*splat.b + alb3*splat.a, _PaintBlend);
                float3 tangentNrm = lerp(nrmAuto, nrm0*splat.r + nrm1*splat.g + nrm2*splat.b + nrm3*splat.a, _PaintBlend);

                albedo *= lerp(0.85, 1.15, macroVal);

                float3 N = TangentToWorldNormal(tangentNrm, normWS);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);

                float  NdotL    = saturate(dot(N, mainLight.direction));
                float  lit      = NdotL * mainLight.shadowAttenuation;
                float  celLit   = CelQuantize(lit, _CelBands);
                float3 celColor = lerp(_CelShadowColor.rgb, float3(1,1,1), celLit);
                float3 ambient  = SampleSH(N);

                float3 addLight = float3(0,0,0);
                uint   addCount = GetAdditionalLightsCount();
                for (uint i = 0; i < addCount; ++i)
                {
                    Light al  = GetAdditionalLight(i, IN.positionWS);
                    float ndl = saturate(dot(N, al.direction));
                    float cl  = CelQuantize(ndl * al.shadowAttenuation, _CelBands);
                    addLight += lerp(_CelShadowColor.rgb, float3(1,1,1), cl) * al.color * al.distanceAttenuation;
                }

                float3 finalColor = albedo * (celColor * mainLight.color + ambient + addLight);
                finalColor = MixFog(finalColor, IN.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Shadow Caster
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST;
                float _Tiling0, _Tiling1, _Tiling2, _Tiling3;
                float _HeightGrassTop, _HeightDirtTop, _HeightStoneTop, _BlendWidth;
                float _SlopeStoneAngle, _SlopeStoneFull;
                float _SlopeSnowAngle,  _SlopeSnowFull;
                float _PaintBlend;
                float _CelBands;
                float4 _CelShadowColor;
                float _NormalStrength;
                float _NoiseScale, _NoiseStrength;
                float _MacroScale, _MacroStrength;
            CBUFFER_END

            struct ShadowAttr { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ShadowVary { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            ShadowVary shadowVert(ShadowAttr IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                ShadowVary OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _MainLightPosition.xyz));
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

        // ------------------------------------------------------------------
        // Depth Only  (escribe al depth buffer - necesario para foam del agua)
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST;
                float _Tiling0, _Tiling1, _Tiling2, _Tiling3;
                float _HeightGrassTop, _HeightDirtTop, _HeightStoneTop, _BlendWidth;
                float _SlopeStoneAngle, _SlopeStoneFull;
                float _SlopeSnowAngle,  _SlopeSnowFull;
                float _PaintBlend;
                float _CelBands;
                float4 _CelShadowColor;
                float _NormalStrength;
                float _NoiseScale, _NoiseStrength;
                float _MacroScale, _MacroStrength;
            CBUFFER_END

            struct DepthAttr { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVary { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            DepthVary depthVert(DepthAttr IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                DepthVary OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 depthFrag(DepthVary IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Depth Normals  (necesario para que SampleSceneDepth del agua funcione)
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   depthNormVert
            #pragma fragment depthNormFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST;
                float _Tiling0, _Tiling1, _Tiling2, _Tiling3;
                float _HeightGrassTop, _HeightDirtTop, _HeightStoneTop, _BlendWidth;
                float _SlopeStoneAngle, _SlopeStoneFull;
                float _SlopeSnowAngle,  _SlopeSnowFull;
                float _PaintBlend;
                float _CelBands;
                float4 _CelShadowColor;
                float _NormalStrength;
                float _NoiseScale, _NoiseStrength;
                float _MacroScale, _MacroStrength;
            CBUFFER_END

            struct DNAttr
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVary
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DNVary depthNormVert(DNAttr IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                DNVary OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 depthNormFrag(DNVary IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                // Empaqueta la normal en [0,1] para el buffer de normales de URP
                return float4(normalWS * 0.5 + 0.5, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

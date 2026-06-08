Shader "Custom/TerrainLayered"
{
    Properties
    {
        // Layer 0 - Base
        _Albedo0        ("Layer 0 Albedo",    2D) = "white" {}
        _Normal0        ("Layer 0 Normal",    2D) = "bump"  {}
        _Tiling0        ("Layer 0 Tiling",    Float) = 4.0
        _Height0        ("Layer 0 Height",    Float) = 0.0

        // Layer 1
        _Albedo1        ("Layer 1 Albedo",    2D) = "white" {}
        _Normal1        ("Layer 1 Normal",    2D) = "bump"  {}
        _Tiling1        ("Layer 1 Tiling",    Float) = 4.0
        _Height1        ("Layer 1 Height",    Float) = 2.0

        // Layer 2
        _Albedo2        ("Layer 2 Albedo",    2D) = "white" {}
        _Normal2        ("Layer 2 Normal",    2D) = "bump"  {}
        _Tiling2        ("Layer 2 Tiling",    Float) = 4.0
        _Height2        ("Layer 2 Height",    Float) = 5.0

        // Layer 3 - Cliff / top
        _Albedo3        ("Layer 3 Albedo",    2D) = "white" {}
        _Normal3        ("Layer 3 Normal",    2D) = "bump"  {}
        _Tiling3        ("Layer 3 Tiling",    Float) = 4.0
        _Height3        ("Layer 3 Height",    Float) = 9.0

        _HeightSharpness("Height Blend Sharpness", Float) = 8.0

        // Slope cliff override
        _SlopeAngleCliff("Cliff Angle (degrees)", Range(0,90)) = 45.0
        _SlopeBlend     ("Slope Blend Width",     Range(0,30)) = 5.0

        // Cel shading
        _CelBands       ("Cel Bands",          Range(1,8))   = 3.0
        _CelShadowColor ("Cel Shadow Color",   Color)        = (0.2, 0.2, 0.3, 1.0)

        // Normal strength
        _NormalStrength ("Normal Strength",    Range(0,2))   = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
        }
        LOD 300

        // ------------------------------------------------------------------
        // Forward Lit pass
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ---- Textures & Samplers ----
            TEXTURE2D(_Albedo0); SAMPLER(sampler_Albedo0);
            TEXTURE2D(_Normal0); SAMPLER(sampler_Normal0);
            TEXTURE2D(_Albedo1); SAMPLER(sampler_Albedo1);
            TEXTURE2D(_Normal1); SAMPLER(sampler_Normal1);
            TEXTURE2D(_Albedo2); SAMPLER(sampler_Albedo2);
            TEXTURE2D(_Normal2); SAMPLER(sampler_Normal2);
            TEXTURE2D(_Albedo3); SAMPLER(sampler_Albedo3);
            TEXTURE2D(_Normal3); SAMPLER(sampler_Normal3);

            // ---- CBuffer ----
            CBUFFER_START(UnityPerMaterial)
                float _Tiling0, _Height0;
                float _Tiling1, _Height1;
                float _Tiling2, _Height2;
                float _Tiling3, _Height3;
                float _HeightSharpness;
                float _SlopeAngleCliff;
                float _SlopeBlend;
                float _CelBands;
                float4 _CelShadowColor;
                float _NormalStrength;
            CBUFFER_END

            // ---- Structs ----
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 tangentWS   : TEXCOORD2;  // w = bitangent sign
                float2 uv          : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                float  fogFactor   : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---- Helpers ----

            // Height-based blend weight (sharp transitions).
            // Sharpness compresses the transition band from ~6 units down to ~0.05.
            float HeightBlend(float worldY, float center, float sharpness)
            {
                return saturate((worldY - center) * sharpness * 0.5 + 0.5);
            }

            float3 UnpackNormalScaled(float4 packed, float scale)
            {
                float3 n = UnpackNormal(packed);
                n.xy *= scale;
                return normalize(n);
            }

            // Cel quantize a lighting value
            float CelQuantize(float lit, float bands)
            {
                return floor(lit * bands) / max(bands - 1.0, 1.0);
            }

            // ---- Vertex ----
            Varyings vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = nrmInputs.normalWS;
                OUT.tangentWS  = float4(nrmInputs.tangentWS,
                                        IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv         = IN.uv;
                OUT.shadowCoord= GetShadowCoord(posInputs);
                OUT.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            // ---- Fragment ----
            half4 frag(Varyings IN) : SV_Target
            {
                // World-space XZ position drives tiling
                float2 wXZ = IN.positionWS.xz;

                // Sample all 4 layers
                float3 alb0 = SAMPLE_TEXTURE2D(_Albedo0, sampler_Albedo0, wXZ * _Tiling0).rgb;
                float3 alb1 = SAMPLE_TEXTURE2D(_Albedo1, sampler_Albedo1, wXZ * _Tiling1).rgb;
                float3 alb2 = SAMPLE_TEXTURE2D(_Albedo2, sampler_Albedo2, wXZ * _Tiling2).rgb;
                float3 alb3 = SAMPLE_TEXTURE2D(_Albedo3, sampler_Albedo3, wXZ * _Tiling3).rgb;

                float3 nrm0 = UnpackNormalScaled(SAMPLE_TEXTURE2D(_Normal0, sampler_Normal0, wXZ * _Tiling0), _NormalStrength);
                float3 nrm1 = UnpackNormalScaled(SAMPLE_TEXTURE2D(_Normal1, sampler_Normal1, wXZ * _Tiling1), _NormalStrength);
                float3 nrm2 = UnpackNormalScaled(SAMPLE_TEXTURE2D(_Normal2, sampler_Normal2, wXZ * _Tiling2), _NormalStrength);
                float3 nrm3 = UnpackNormalScaled(SAMPLE_TEXTURE2D(_Normal3, sampler_Normal3, wXZ * _Tiling3), _NormalStrength);

                // Height-based blending (bottom -> top: layer0 -> layer1 -> layer2 -> layer3)
                float worldY   = IN.positionWS.y;
                float sharp    = _HeightSharpness;

                float w01  = HeightBlend(worldY, _Height1, sharp);   // 0->1 transition
                float w12  = HeightBlend(worldY, _Height2, sharp);   // 1->2 transition
                float w23  = HeightBlend(worldY, _Height3, sharp);   // 2->3 transition

                float3 albHeight = alb0;
                albHeight = lerp(albHeight, alb1, w01);
                albHeight = lerp(albHeight, alb2, w12);
                albHeight = lerp(albHeight, alb3, w23);

                float3 nrmHeight = nrm0;
                nrmHeight = lerp(nrmHeight, nrm1, w01);
                nrmHeight = lerp(nrmHeight, nrm2, w12);
                nrmHeight = lerp(nrmHeight, nrm3, w23);

                // Slope-based cliff override (layer 3).
                // Uses degrees(acos(normalWS.y)) against _SlopeAngleCliff +/- _SlopeBlend.
                float3 normWS    = normalize(IN.normalWS);
                float  slopeDeg  = degrees(acos(saturate(normWS.y)));
                float  slopeBlend = saturate((slopeDeg - (_SlopeAngleCliff - _SlopeBlend)) /
                                             max(_SlopeBlend * 2.0, 0.001));

                float3 albedo    = lerp(albHeight, alb3, slopeBlend);
                float3 tangentNrm= lerp(nrmHeight, nrm3, slopeBlend);

                // TBN -> world normal
                float3 tangentWS  = normalize(IN.tangentWS.xyz);
                float3 bitangentWS= normalize(cross(normWS, tangentWS) * IN.tangentWS.w);
                float3 N = normalize(tangentNrm.x * tangentWS +
                                     tangentNrm.y * bitangentWS +
                                     tangentNrm.z * normWS);

                // Main light + shadow
                float4 shadowCoord = IN.shadowCoord;
                Light  mainLight   = GetMainLight(shadowCoord);
                float  NdotL       = saturate(dot(N, mainLight.direction));
                float  shadowAtten = mainLight.shadowAttenuation;
                float  lit         = NdotL * shadowAtten;

                // Cel quantize: floor(lit*bands)/(bands-1) lerped between shadow color and white
                float  celLit   = CelQuantize(lit, _CelBands);
                float3 celColor = lerp(_CelShadowColor.rgb, float3(1,1,1), celLit);

                // SH ambient
                float3 ambient = SampleSH(N);

                // Additional lights
                float3 additionalLight = float3(0,0,0);
                uint   addLightCount   = GetAdditionalLightsCount();
                for (uint i = 0; i < addLightCount; ++i)
                {
                    Light addLight = GetAdditionalLight(i, IN.positionWS);
                    float addNdotL = saturate(dot(N, addLight.direction));
                    float addLit   = CelQuantize(addNdotL * addLight.shadowAttenuation, _CelBands);
                    additionalLight += lerp(_CelShadowColor.rgb, float3(1,1,1), addLit)
                                      * addLight.color * addLight.distanceAttenuation;
                }

                float3 finalColor = albedo * (celColor * mainLight.color + ambient + additionalLight);

                // Fog
                finalColor = MixFog(finalColor, IN.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Shadow Caster pass
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
            #pragma multi_compile_shadowcaster

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Depth Only pass
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

// PlayerProximity.shader - URP
// Cel-shaded player shader. Proximity fade is SELF-CONTAINED: the shader measures
// the distance from the object PIVOT to the camera and dithers itself out when the
// camera gets close. No external script required.
//
// Why pivot distance (not per-fragment): the pivot is uniform for the whole mesh,
// so every pixel of the mesh clips at the same dither threshold. That's what stops
// inner geometry (sword, back faces) from showing through the holes.
//
// _ProximityAlpha and _HitAlpha remain as OPTIONAL overrides a script may drive
// (e.g. HitReaction for the Zelda blink). Final visibility = min of all three.
//
// Additional lights use LIGHT_LOOP macros (Forward AND Forward+).
// Custom ShadowCaster / DepthOnly passes avoid URP/Lit keyword pollution.
// PURE ASCII in all comments - non-ASCII characters crash Unity's HLSL compiler.
Shader "Custom/PlayerProximity"
{
    Properties
    {
        [Header(Base)]
        _BaseMap     ("Texture",      2D)    = "white" {}
        _BaseColor   ("Color",        Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0.25,0.30,0.45,1)

        [Header(Cel Shading)]
        _Steps      ("Shade Steps",   Range(1,8))    = 2
        _StepSmooth ("Band Softness", Range(0,0.49)) = 0.04
        _Threshold  ("Light Threshold", Range(0,1))  = 0.5

        [Header(Rim)]
        _RimPower ("Rim Power", Range(0,16)) = 4
        _RimColor ("Rim Color", Color)       = (0.55,0.6,1,1)

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)]
        _OutlineOn   ("Enable Outline", Float) = 1
        _OutlineWidth ("Outline Width (world units)", Range(0,0.1)) = 0.02
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)

        [Header(Proximity Fade self contained)]
        _FadeStart ("Fade Start Dist (visible)", Range(0,12)) = 1.6
        _FadeEnd   ("Fade End Dist (invisible)", Range(0,12)) = 0.5

        [Header(Optional Script Overrides)]
        _ProximityAlpha ("Proximity Alpha", Range(0,1)) = 1
        _HitFlashColor  ("Hit Flash Color", Color)      = (1,0.1,0.1,1)
        _HitFlashAmount ("Flash Amount",    Range(0,1)) = 0
        _HitAlpha       ("Hit Alpha",       Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // -----------------------------------------------------------------
        // Pass 0 - Outline (inverted hull). ZTest LEqual so the hull is occluded
        // by closer solid geometry - hides seams between touching objects, keeps
        // only the outer silhouette.
        // -----------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull  Front
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   OutlineVert
            #pragma fragment OutlineFrag
            #pragma shader_feature_local _OUTLINE_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CelShadingCore.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _HitFlashColor;
                float  _Steps;
                float  _StepSmooth;
                float  _Threshold;
                float  _RimPower;
                float  _OutlineWidth;
                float  _FadeStart;
                float  _FadeEnd;
                float  _ProximityAlpha;
                float  _HitFlashAmount;
                float  _HitAlpha;
            CBUFFER_END

            // Self-contained proximity alpha from the object pivot distance.
            // 1 = far (visible), 0 = close (invisible). Uniform across the mesh.
            float AutoProximity()
            {
                float3 pivotWS = TransformObjectToWorld(float3(0,0,0));
                float  d       = distance(pivotWS, _WorldSpaceCameraPos);
                return saturate((d - _FadeEnd) / max(_FadeStart - _FadeEnd, 0.0001));
            }

            struct Attributes
            {
                float4 posOS       : POSITION;
                float3 normalOS    : NORMAL;
                float3 smoothNrmOS : TEXCOORD3;  // smooth normal auto-baked into UV3
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                #ifdef _OUTLINE_ON
                    // Constant WORLD-space hull inflation (toon-shader standard).
                    // Direction = baked smooth normal (UV3) when present, else the
                    // vertex normal (seamless on smooth meshes).
                    float3 nOS = (dot(IN.smoothNrmOS, IN.smoothNrmOS) > 1e-4)
                                 ? IN.smoothNrmOS : IN.normalOS;
                    float3 posWS  = TransformObjectToWorld(IN.posOS.xyz);
                    float3 normWS = TransformObjectToWorldNormal(nOS);
                    posWS += normWS * _OutlineWidth;
                    OUT.posCS = TransformWorldToHClip(posWS);
                #else
                    OUT.posCS = float4(0, 0, 0, -1);
                #endif
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float a = min(AutoProximity() * _ProximityAlpha, _HitAlpha);
                clip(BayerDither4x4(IN.posCS.xy) - (1.0 - a));
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // Pass 1 - ForwardLit
        // -----------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull  Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   PlayerVert
            #pragma fragment PlayerFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CelShadingCore.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _HitFlashColor;
                float  _Steps;
                float  _StepSmooth;
                float  _Threshold;
                float  _RimPower;
                float  _OutlineWidth;
                float  _FadeStart;
                float  _FadeEnd;
                float  _ProximityAlpha;
                float  _HitFlashAmount;
                float  _HitAlpha;
            CBUFFER_END

            // Self-contained proximity alpha from the object pivot distance.
            float AutoProximity()
            {
                float3 pivotWS = TransformObjectToWorld(float3(0,0,0));
                float  d       = distance(pivotWS, _WorldSpaceCameraPos);
                return saturate((d - _FadeEnd) / max(_FadeStart - _FadeEnd, 0.0001));
            }

            struct Attributes
            {
                float4 posOS    : POSITION;
                float3 normalOS : NORMAL;
                float2 uv       : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 posCS     : SV_POSITION;
                float3 normalWS  : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv        : TEXCOORD2;
                float  fogFactor : TEXCOORD3;
                float3 posWS     : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings PlayerVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.posOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);

                OUT.posCS     = vpi.positionCS;
                OUT.posWS     = vpi.positionWS;
                OUT.normalWS  = vni.normalWS;
                OUT.viewDirWS = GetWorldSpaceViewDir(vpi.positionWS);
                OUT.uv        = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 PlayerFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // Self-contained proximity fade + optional script overrides.
                // Uniform per-mesh (pivot distance) so inner geometry never shows.
                float a     = min(AutoProximity() * _ProximityAlpha, _HitAlpha);
                float bayer = BayerDither4x4(IN.posCS.xy);
                clip(bayer - (1.0 - a));

                float4 tex    = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                float3 normal = normalize(IN.normalWS);
                float3 view   = normalize(IN.viewDirWS);

                // Main light. FULL overload applies URP's shadow distance fade
                // (no hard dark disc following the camera).
                float4 shadowCoord = TransformWorldToShadowCoord(IN.posWS);
                Light  main = GetMainLight(shadowCoord, IN.posWS, half4(1,1,1,1));

                // Cast shadow folded into the lambert term BEFORE the cel step, so
                // cast + form shadow are one band (no cascade-seam disc).
                float NdotL = dot(normal, main.direction);
                float ndl   = saturate(NdotL * 0.5 + 0.5);
                float ramp  = saturate(ndl * main.shadowAttenuation + (0.5 - _Threshold));
                float cel   = CelStep(ramp, _Steps, _StepSmooth);
                float3 col  = lerp(_ShadowColor.rgb * tex.rgb, tex.rgb, cel) * main.color;

                // Additional lights - Forward / Forward+ safe.
                #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS)
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.posWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.posCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light al    = GetAdditionalLight(lightIndex, IN.posWS);
                    float aNdL  = saturate(dot(normal, al.direction));
                    float aCel  = CelStep(aNdL, _Steps, _StepSmooth);
                    col        += al.color * aCel * al.shadowAttenuation * al.distanceAttenuation * tex.rgb;
                LIGHT_LOOP_END
                #endif

                // Rim
                if (_RimPower > 0.001)
                {
                    float rim = 1.0 - saturate(dot(view, normal));
                    rim       = pow(rim, _RimPower);
                    col      += _RimColor.rgb * rim;
                }

                // Hit flash tint
                col = lerp(col, _HitFlashColor.rgb, _HitFlashAmount);
                col = MixFog(col, IN.fogFactor);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // Pass 2 - ShadowCaster
        // -----------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite    On
            ZTest     LEqual
            ColorMask 0
            Cull      Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _HitFlashColor;
                float  _Steps;
                float  _StepSmooth;
                float  _Threshold;
                float  _RimPower;
                float  _OutlineWidth;
                float  _FadeStart;
                float  _FadeEnd;
                float  _ProximityAlpha;
                float  _HitFlashAmount;
                float  _HitAlpha;
            CBUFFER_END

            float3 _LightDirection;

            struct Attributes
            {
                float4 posOS    : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS  = TransformObjectToWorld(IN.posOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, _LightDirection));
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // Pass 3 - DepthOnly
        // -----------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite    On
            ColorMask R
            Cull      Back

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _HitFlashColor;
                float  _Steps;
                float  _StepSmooth;
                float  _Threshold;
                float  _RimPower;
                float  _OutlineWidth;
                float  _FadeStart;
                float  _FadeEnd;
                float  _ProximityAlpha;
                float  _HitFlashAmount;
                float  _HitAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 posCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                return OUT;
            }

            half4 DepthFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

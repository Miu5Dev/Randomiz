// CelShading.shader - URP
// Cartoon / toon shading for enemies, props and environment.
// Defaults: outline ON, rim ON, 2 shade steps.
// Hit feedback via MaterialPropertyBlock (_HitFlashAmount, _HitAlpha).
// Custom ShadowCaster / DepthOnly passes avoid URP/Lit keyword pollution.
// Additional lights use LIGHT_LOOP macros so they work in Forward AND Forward+.
// PURE ASCII in all comments - non-ASCII characters crash Unity's HLSL compiler.
Shader "Custom/CelShading"
{
    Properties
    {
        [Header(Base)]
        _BaseMap        ("Texture",         2D)             = "white" {}
        _BaseColor      ("Color",           Color)          = (1,1,1,1)
        _ShadowColor    ("Shadow Color",    Color)          = (0.25,0.30,0.45,1)

        [Header(Cel Shading)]
        _Steps          ("Shade Steps",     Range(1,8))     = 2
        _StepSmooth     ("Band Softness",   Range(0,0.49))  = 0.06
        _Threshold      ("Light Threshold", Range(0,1))     = 0.5

        [Header(Rim)]
        _RimPower       ("Rim Power",       Range(0,16))    = 4
        _RimColor       ("Rim Color",       Color)          = (0.55,0.6,1,1)

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)]
        _OutlineOn      ("Enable Outline", Float) = 1
        _OutlineWidth   ("Outline Width (world units)", Range(0,0.1)) = 0.02
        _OutlineColor   ("Outline Color",   Color)          = (0,0,0,1)

        [Header(Hit Feedback MPB Only)]
        _HitFlashColor  ("Hit Flash Color", Color)          = (1,0.1,0.1,1)
        _HitFlashAmount ("Flash Amount",    Range(0,1))     = 0
        _HitAlpha       ("Hit Alpha",       Range(0,1))     = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // -----------------------------------------------------------------
        // Pass 0 - Outline (inverted hull). ZTest LEqual + ZWrite so the inflated
        // back-face hull is OCCLUDED wherever closer solid geometry exists. That
        // hides the seams between touching/stacked objects (the hull pushed toward
        // a neighbour fails the depth test against that neighbour's solid surface),
        // leaving only the outer silhouette - from the camera's point of view.
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
                float  _HitFlashAmount;
                float  _HitAlpha;
            CBUFFER_END

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
                    // Inverted hull, constant WORLD-space width (toon-shader standard).
                    // Direction = baked smooth normal in UV3 when present (closes gaps
                    // on hard-edge meshes / cubes), else the vertex normal (already
                    // seamless on smooth meshes: spheres, capsules, characters).
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
                clip(BayerDither4x4(IN.posCS.xy) - (1.0 - _HitAlpha));
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
            #pragma vertex   CelVert
            #pragma fragment CelFrag
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
                float  _HitFlashAmount;
                float  _HitAlpha;
            CBUFFER_END

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

            Varyings CelVert(Attributes IN)
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

            half4 CelFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float bayer = BayerDither4x4(IN.posCS.xy);
                clip(bayer - (1.0 - _HitAlpha));

                float4 tex    = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                float3 normal = normalize(IN.normalWS);
                float3 view   = normalize(IN.viewDirWS);

                // Main light. Use the FULL GetMainLight overload (shadowCoord +
                // positionWS + shadowMask): it applies URP's shadow distance fade
                // internally (MixRealtimeAndBakedShadows), so cast shadows fade
                // smoothly to "lit" past the shadow Max Distance. The simple
                // GetMainLight(shadowCoord) overload does NOT fade, which made a hard
                // dark disc follow the camera at the shadow-distance boundary.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.posWS);
                Light  main = GetMainLight(shadowCoord, IN.posWS, half4(1,1,1,1));

                // Combine cast shadow INTO the lambert term BEFORE the cel step, so
                // form shadow + cast shadow are a single quantised band. Multiplying
                // the shadow AFTER the step (the old way) made the cascade-boundary
                // jump in shadowAttenuation show up as a second hard edge - the disc
                // that followed the camera. As one ramp, the cascade seam only ever
                // matters right at the terminator, so it disappears in practice.
                float NdotL = dot(normal, main.direction);
                float ndl   = saturate(NdotL * 0.5 + 0.5);
                float ramp  = saturate(ndl * main.shadowAttenuation + (0.5 - _Threshold));
                float cel   = CelStep(ramp, _Steps, _StepSmooth);
                float3 col  = lerp(_ShadowColor.rgb * tex.rgb, tex.rgb, cel) * main.color;

                // Additional lights - LIGHT_LOOP works in Forward AND Forward+.
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

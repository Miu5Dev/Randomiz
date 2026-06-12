// CelShading.shader - URP
// Cartoon / toon shading for enemies, props and environment.
// Supports day/night cycle sun fade.
// PURE ASCII in all comments.
Shader "Custom/CelShading"
{
    Properties
    {
        [Header(Base)]
        _BaseMap         ("Texture",              2D)            = "white" {}
        _BaseColor       ("Color",                Color)         = (1,1,1,1)
        _ShadowColor     ("Shadow Color",         Color)         = (0.25,0.30,0.45,1)

        [Header(Cel Shading)]
        _Steps           ("Shade Steps",          Range(1,8))    = 2
        _StepSmooth      ("Band Softness",        Range(0,0.49)) = 0.06
        _Threshold       ("Light Threshold",      Range(0,1))    = 0.5

        [Header(Ambient)]
        _AmbientStrength ("Skybox Ambient",       Range(0,1))    = 0.25

        [Header(Rim)]
        _RimPower        ("Rim Power",            Range(0,16))   = 4
        _RimColor        ("Rim Color",            Color)         = (0.55,0.6,1,1)

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)]
        _OutlineOn       ("Enable Outline",       Float)         = 1
        _OutlineWidth    ("Outline Width",        Range(0,0.1))  = 0.02
        _OutlineColor    ("Outline Color",        Color)         = (0,0,0,1)

        [Header(Hit Feedback MPB Only)]
        _HitFlashColor   ("Hit Flash Color",      Color)         = (1,0.1,0.1,1)
        _HitFlashAmount  ("Flash Amount",         Range(0,1))    = 0
        _HitAlpha        ("Hit Alpha",            Range(0,1))    = 1
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
        // Pass 0 - Outline
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
                float4 _BaseColor; float4 _ShadowColor; float4 _RimColor;
                float4 _OutlineColor; float4 _HitFlashColor;
                float  _Steps; float _StepSmooth; float _Threshold;
                float  _AmbientStrength;
                float  _RimPower; float _OutlineWidth;
                float  _HitFlashAmount; float _HitAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS       : POSITION;
                float3 normalOS    : NORMAL;
                float3 smoothNrmOS : TEXCOORD3;
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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor; float4 _ShadowColor; float4 _RimColor;
                float4 _OutlineColor; float4 _HitFlashColor;
                float  _Steps; float _StepSmooth; float _Threshold;
                float  _AmbientStrength;
                float  _RimPower; float _OutlineWidth;
                float  _HitFlashAmount; float _HitAlpha;
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

                float4 shadowCoord = TransformWorldToShadowCoord(IN.posWS);
                Light  main = GetMainLight(shadowCoord, IN.posWS, half4(1,1,1,1));

                // Ciclo dia/noche: ignorar luz cuando viene desde abajo
                float sunHeight  = saturate(main.direction.y * 2.0 + 0.1);
                float sunFade    = smoothstep(0.0, 0.25, sunHeight);
                float3 mainColor = main.color * sunFade;
                float  mainShadow = main.shadowAttenuation * sunFade;

                float NdotL = dot(normal, main.direction);
                float ndl   = saturate(NdotL);
                float ramp  = saturate(ndl * mainShadow + (0.5 - _Threshold));
                float cel   = CelStep(ramp, _Steps, _StepSmooth);

                float3 litColor    = tex.rgb * mainColor;
                float3 shadowColor = tex.rgb * _ShadowColor.rgb;
                float3 col         = lerp(shadowColor, litColor, cel);

                // Ambient escalado por orientacion Y sombra
                float ambientOcclusion = saturate(ndl * 0.5 + 0.5) * mainShadow;
                float3 ambient = SampleSH(normal) * ambientOcclusion * _AmbientStrength;
                col += tex.rgb * ambient;

                // Additional lights
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

                // Rim solo en zonas iluminadas
                if (_RimPower > 0.001)
                {
                    float rim = 1.0 - saturate(dot(view, normal));
                    col      += _RimColor.rgb * pow(rim, _RimPower) * cel;
                }

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
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor; float4 _ShadowColor; float4 _RimColor;
                float4 _OutlineColor; float4 _HitFlashColor;
                float  _Steps; float _StepSmooth; float _Threshold; float _AmbientStrength;
                float  _RimPower; float _OutlineWidth; float _HitFlashAmount; float _HitAlpha;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 posCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN); UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS    = TransformObjectToWorld(IN.posOS.xyz);
                float3 normWS   = TransformObjectToWorldNormal(IN.normalOS);
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float4 posCS    = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, lightDir));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.posCS = posCS;
                return OUT;
            }
            half4 ShadowFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // -----------------------------------------------------------------
        // Pass 3 - DepthOnly
        // -----------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor; float4 _ShadowColor; float4 _RimColor;
                float4 _OutlineColor; float4 _HitFlashColor;
                float  _Steps; float _StepSmooth; float _Threshold; float _AmbientStrength;
                float  _RimPower; float _OutlineWidth; float _HitFlashAmount; float _HitAlpha;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 posCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN); UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                return OUT;
            }
            half4 DepthFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
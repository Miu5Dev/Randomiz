// CelTree.shader - URP
// Cel-shaded tree shader compatible with Unity Terrain tree system.
// Supports: outline, wind, alpha cutout, billboard, day/night cycle.
// PURE ASCII in all comments.
Shader "Custom/CelTree"
{
    Properties
    {
        [Header(Base)]
        _MainTex         ("Albedo (RGB) Alpha (A)", 2D)            = "white" {}
        _Color           ("Color Tint",             Color)         = (1,1,1,1)
        _Cutoff          ("Alpha Cutoff",           Range(0,1))    = 0.333

        [Header(Cel Shading)]
        _ShadowColor     ("Shadow Color",           Color)         = (0.18,0.25,0.18,1)
        _Steps           ("Shade Steps",            Range(1,8))    = 2
        _StepSmooth      ("Band Softness",          Range(0,0.49)) = 0.08
        _Threshold       ("Light Threshold",        Range(0,1))    = 0.5
        _BackfaceLight   ("Backface Light",         Range(0,1))    = 0.3

        [Header(Ambient)]
        _AmbientStrength ("Skybox Ambient",         Range(0,1))    = 0.25

        [Header(Rim)]
        _RimPower        ("Rim Power",              Range(0,16))   = 5
        _RimColor        ("Rim Color",              Color)         = (0.4,0.7,0.3,1)

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)]
        _OutlineOn       ("Enable Outline",         Float)         = 1
        _OutlineWidth    ("Outline Width",          Range(0,0.1))  = 0.015
        _OutlineColor    ("Outline Color",          Color)         = (0,0,0,1)

        [Header(Wind)]
        _WindStrength    ("Wind Strength",          Range(0,1))    = 0.15
        _WindSpeed       ("Wind Speed",             Range(0,5))    = 1.2
        _WindScale       ("Wind Noise Scale",       Float)         = 0.05

        [HideInInspector] _TreeBillboardCameraRight  ("", Vector)  = (1,0,0,0)
        [HideInInspector] _TreeBillboardCameraUp     ("", Vector)  = (0,1,0,0)
        [HideInInspector] _TreeBillboardCameraForward("", Vector)  = (0,0,1,0)
        [HideInInspector] _TreeBillboardPos          ("", Vector)  = (0,0,0,0)
        [HideInInspector] _TreeBillboardSize         ("", Vector)  = (1,1,0,0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "TransparentCutout"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "AlphaTest"
            "IgnoreProjector" = "True"
            "DisableBatching" = "LODFading"
        }
        LOD 200

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
            #pragma vertex   TreeOutlineVert
            #pragma fragment TreeOutlineFrag
            #pragma shader_feature_local _OUTLINE_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color; float4 _ShadowColor; float4 _RimColor; float4 _OutlineColor;
                float  _Cutoff; float _OutlineWidth; float _BackfaceLight;
                float  _Steps; float _StepSmooth; float _Threshold; float _AmbientStrength;
                float  _RimPower; float _WindStrength; float _WindSpeed; float _WindScale;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS    : POSITION;
                float3 normalOS : NORMAL;
                float2 uv       : TEXCOORD0;
                float4 color    : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 uv    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 ApplyWindOutline(float3 posOS, float3 posWS, float windMask, float windPhase)
            {
                float time  = _Time.y * _WindSpeed;
                float wave  = sin(time + posWS.x * _WindScale + windPhase * 6.2832);
                float wave2 = sin(time * 0.73 + posWS.z * _WindScale);
                return posOS + float3(wave, 0, wave2) * _WindStrength * windMask;
            }

            Varyings TreeOutlineVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                #ifdef _OUTLINE_ON
                    float3 posOS  = ApplyWindOutline(
                        IN.posOS.xyz, TransformObjectToWorld(IN.posOS.xyz),
                        IN.color.g, IN.color.r
                    );
                    float3 posWS  = TransformObjectToWorld(posOS);
                    float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                    posWS        += normWS * _OutlineWidth;
                    OUT.posCS     = TransformWorldToHClip(posWS);
                #else
                    OUT.posCS = float4(0, 0, 0, -1);
                #endif
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 TreeOutlineFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a * _Color.a - _Cutoff);
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
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   TreeVert
            #pragma fragment TreeFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile __ NATURE_RENDERPIPELINE
            #pragma multi_compile __ LOD_FADE_CROSSFADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color; float4 _ShadowColor; float4 _RimColor; float4 _OutlineColor;
                float  _Cutoff; float _OutlineWidth; float _BackfaceLight;
                float  _Steps; float _StepSmooth; float _Threshold; float _AmbientStrength;
                float  _RimPower;
                float  _WindStrength; float _WindSpeed; float _WindScale;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS    : POSITION;
                float3 normalOS : NORMAL;
                float2 uv       : TEXCOORD0;
                float4 color    : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 posCS     : SV_POSITION;
                float3 posWS     : TEXCOORD0;
                float3 normalWS  : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float2 uv        : TEXCOORD3;
                float  fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float CelStep(float ramp, float steps, float sm)
            {
                float s = floor(ramp * steps) / max(steps - 1.0, 1.0);
                float f = frac(ramp * steps);
                float e = smoothstep(0.5 - sm, 0.5 + sm, f) / max(steps - 1.0, 1.0);
                return saturate(s + e);
            }

            float3 ApplyWind(float3 posOS, float3 posWS, float windMask, float windPhase)
            {
                float time  = _Time.y * _WindSpeed;
                float wave  = sin(time + posWS.x * _WindScale + windPhase * 6.2832);
                float wave2 = sin(time * 0.73 + posWS.z * _WindScale);
                return posOS + float3(wave, 0, wave2) * _WindStrength * windMask;
            }

            Varyings TreeVert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 posOS = ApplyWind(
                    IN.posOS.xyz, TransformObjectToWorld(IN.posOS.xyz),
                    IN.color.g, IN.color.r
                );
                VertexPositionInputs vpi = GetVertexPositionInputs(posOS);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);
                OUT.posCS     = vpi.positionCS;
                OUT.posWS     = vpi.positionWS;
                OUT.normalWS  = vni.normalWS;
                OUT.viewDirWS = GetWorldSpaceViewDir(vpi.positionWS);
                OUT.uv        = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.fogFactor = ComputeFogFactor(vpi.positionCS.z);
                return OUT;
            }

            half4 TreeFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color;
                clip(tex.a - _Cutoff);

                #ifdef LOD_FADE_CROSSFADE
                    float2 scrPos = IN.posCS.xy * 0.25;
                    float bayer = frac(dot(floor(scrPos), float2(0.5, 0.25)) * 2.0);
                    clip(unity_LODFade.x - bayer);
                #endif

                float3 normal = normalize(IN.normalWS);
                float3 view   = normalize(IN.viewDirWS);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.posWS);
                Light  main = GetMainLight(shadowCoord, IN.posWS, half4(1,1,1,1));

                // Ciclo dia/noche
                float  sunHeight  = saturate(main.direction.y * 2.0 + 0.1);
                float  sunFade    = smoothstep(0.0, 0.25, sunHeight);
                float3 mainColor  = main.color * sunFade;
                float  mainShadow = main.shadowAttenuation * sunFade;

                float NdotL     = dot(normal, main.direction);
                float NdotLBack = dot(-normal, main.direction);
                float ndl       = saturate(max(NdotL, NdotLBack * _BackfaceLight));
                float ramp      = saturate(ndl * mainShadow + (0.5 - _Threshold));
                float cel       = CelStep(ramp, _Steps, _StepSmooth);

                float3 litColor    = tex.rgb * mainColor;
                float3 shadowColor = tex.rgb * _ShadowColor.rgb;
                float3 col         = lerp(shadowColor, litColor, cel);

                float ambientOcclusion = saturate(ndl * 0.5 + 0.5) * mainShadow;
                float3 ambient = SampleSH(normal) * ambientOcclusion * _AmbientStrength;
                col += tex.rgb * ambient;

                #if defined(_ADDITIONAL_LIGHTS) || defined(_FORWARD_PLUS)
                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.posWS;
                inputData.normalWS                = normal;
                inputData.viewDirectionWS         = view;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.posCS);
                inputData.shadowMask              = half4(1,1,1,1);

                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light al       = GetAdditionalLight(lightIndex, IN.posWS);
                    float aNdL     = dot(normal, al.direction);
                    float aNdLBack = dot(-normal, al.direction);
                    float aNdl     = saturate(max(aNdL, aNdLBack * _BackfaceLight));
                    float aCel     = CelStep(aNdl, _Steps, _StepSmooth);
                    col += al.color * aCel * al.shadowAttenuation * al.distanceAttenuation * tex.rgb;
                LIGHT_LOOP_END
                #endif

                if (_RimPower > 0.001)
                {
                    float rim = 1.0 - saturate(dot(view, normal));
                    col      += _RimColor.rgb * pow(rim, _RimPower) * cel;
                }

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
            Cull      Off

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile __ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color; float4 _ShadowColor; float4 _RimColor; float4 _OutlineColor;
                float  _Cutoff; float _OutlineWidth; float _BackfaceLight;
                float  _Steps; float _StepSmooth; float _Threshold; float _AmbientStrength;
                float  _RimPower; float _WindStrength; float _WindSpeed; float _WindScale;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS    : POSITION;
                float3 normalOS : NORMAL;
                float2 uv       : TEXCOORD0;
                float4 color    : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 posCS : SV_POSITION;
                float2 uv    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 ApplyWindShadow(float3 posOS, float3 posWS, float windMask, float windPhase)
            {
                float time  = _Time.y * _WindSpeed;
                float wave  = sin(time + posWS.x * _WindScale + windPhase * 6.2832);
                float wave2 = sin(time * 0.73 + posWS.z * _WindScale);
                return posOS + float3(wave, 0, wave2) * _WindStrength * windMask;
            }

            Varyings ShadowVert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = ApplyWindShadow(
                    IN.posOS.xyz, TransformObjectToWorld(IN.posOS.xyz),
                    IN.color.g, IN.color.r
                );

                float3 posWS    = TransformObjectToWorld(posOS);
                float3 nrmWS    = TransformObjectToWorldNormal(IN.normalOS);
                float3 lightDir = normalize(_MainLightPosition.xyz);

                posWS = ApplyShadowBias(posWS, nrmWS, lightDir);

                float4 posCS = TransformWorldToHClip(posWS);
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.posCS = posCS;
                OUT.uv    = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a * _Color.a - _Cutoff);
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
            ZWrite On ColorMask R Cull Off

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color; float4 _ShadowColor; float4 _RimColor; float4 _OutlineColor;
                float  _Cutoff; float _OutlineWidth; float _BackfaceLight;
                float  _Steps; float _StepSmooth; float _Threshold; float _AmbientStrength;
                float  _RimPower; float _WindStrength; float _WindSpeed; float _WindScale;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings DepthVert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN); Varyings OUT; UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv    = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }
            half4 DepthFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(tex.a * _Color.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Nature/Soft Occlusion Leaves"
}
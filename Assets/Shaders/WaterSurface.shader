Shader "Custom/WaterSurface"
{
    Properties
    {
        _DeepColor      ("Deep Color",          Color)        = (0.05, 0.15, 0.35, 0.9)
        _ShallowColor   ("Shallow Color",       Color)        = (0.3,  0.65, 0.75, 0.6)

        _NormalA        ("Normal Map A",        2D)           = "bump" {}
        _NormalB        ("Normal Map B",        2D)           = "bump" {}
        _NormalTiling   ("Normal Tiling",       Float)        = 2.0
        _NormalStrength ("Normal Strength",     Range(0,2))   = 0.6
        _ScrollA        ("Scroll Speed A",      Vector)       = (0.04, 0.02, 0, 0)
        _ScrollB        ("Scroll Speed B",      Vector)       = (-0.02, 0.05, 0, 0)

        _FresnelPower   ("Fresnel Power",       Range(0.1,8)) = 3.0

        _WaveSpeed      ("Wave Speed",          Float)        = 1.2
        _WaveStrength   ("Wave Strength",       Float)        = 0.12
        _WaveScale      ("Wave Scale",          Float)        = 0.4

        _FoamDepth      ("Foam Depth Range",    Float)        = 0.4
        _FoamColor      ("Foam Color",          Color)        = (1,1,1,1)

        _SpecColor2     ("Specular Color",      Color)        = (1,1,1,1)
        _Shininess      ("Shininess",           Float)        = 64.0
        _CelSpecCut     ("Cel Specular Cutoff", Range(0,1))   = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent"
        }
        LOD 200

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ShallowColor;
                float  _NormalTiling;
                float  _NormalStrength;
                float2 _ScrollA;
                float2 _ScrollB;
                float  _FresnelPower;
                float  _WaveSpeed;
                float  _WaveStrength;
                float  _WaveScale;
                float  _FoamDepth;
                float4 _FoamColor;
                float4 _SpecColor2;
                float  _Shininess;
                float  _CelSpecCut;
            CBUFFER_END

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
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                float4 screenPos  : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WaveHeight(float3 posWS)
            {
                float wave = sin(posWS.x * _WaveScale + _Time.y * _WaveSpeed)
                           + sin(posWS.z * _WaveScale * 0.7 + _Time.y * _WaveSpeed * 1.3);
                return wave * _WaveStrength * 0.5;
            }

            Varyings vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posOS    = IN.positionOS.xyz;
                float3 posWSpre = TransformObjectToWorld(posOS);
                posOS.y += WaveHeight(posWSpre);

                VertexPositionInputs pos = GetVertexPositionInputs(posOS);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.tangentWS  = float4(nrm.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv         = IN.uv;
                // FIX: shadowCoord eliminado del vertex; se calcula en fragment
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                OUT.screenPos  = ComputeScreenPos(pos.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 baseUV = IN.positionWS.xz * _NormalTiling;
                float2 uvA    = baseUV + _ScrollA * _Time.y;
                float2 uvB    = baseUV + _ScrollB * _Time.y;

                float3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, uvA));
                float3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, uvB));
                float3 combinedTN = normalize(float3((nA.xy + nB.xy) * _NormalStrength, nA.z));

                float3 normWS     = normalize(IN.normalWS);
                float3 tangentWS  = normalize(IN.tangentWS.xyz);
                float3 bitangWS   = normalize(cross(normWS, tangentWS) * IN.tangentWS.w);
                float3 N = normalize(combinedTN.x * tangentWS + combinedTN.y * bitangWS + combinedTN.z * normWS);

                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);

                float  fresnel    = pow(1.0 - saturate(dot(N, viewDir)), _FresnelPower);
                float4 waterColor = lerp(_DeepColor, _ShallowColor, fresnel);

                // ----------------------------------------------------------
                // Foam por interseccion con geometria opaca (terrain incluido)
                // FIX: usar screenPos.xy/w correctamente y LinearEyeDepth
                // ----------------------------------------------------------
                float2 screenUV  = IN.screenPos.xy / IN.screenPos.w;
                float  sceneRawZ = SampleSceneDepth(screenUV);
                float  sceneEye  = LinearEyeDepth(sceneRawZ, _ZBufferParams);
                // surfaceZ: distancia ojo->fragmento agua en eye space
                float  surfaceEye = IN.screenPos.w;
                float  depthDiff  = sceneEye - surfaceEye;

                // Si el depth buffer no tiene dato valido, fallback a rim fresnel
                float rimFresnel = pow(1.0 - saturate(dot(normWS, viewDir)), 4.0);
                float foamMask   = (depthDiff > 0.001)
                                   ? saturate(1.0 - depthDiff / max(_FoamDepth, 0.001))
                                   : rimFresnel * 0.5;

                // ----------------------------------------------------------
                // Lighting  (FIX: shadowCoord calculado en fragment)
                // ----------------------------------------------------------
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);

                float3 L     = mainLight.direction;
                float  NdotL = saturate(dot(N, L));
                float  sha   = mainLight.shadowAttenuation;

                float3 H      = normalize(L + viewDir);
                float  NdotH  = saturate(dot(N, H));
                float  spec   = step(_CelSpecCut, pow(NdotH, _Shininess)) * sha;

                float3 lightC = mainLight.color * (NdotL * sha + spec * _SpecColor2.rgb);
                float3 ambient = SampleSH(N);

                float3 finalRGB = waterColor.rgb * (lightC + ambient);
                finalRGB = lerp(finalRGB, _FoamColor.rgb, foamMask);
                float  alpha    = lerp(waterColor.a, 1.0, foamMask);

                finalRGB = MixFog(finalRGB, IN.fogFactor);
                return half4(finalRGB, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

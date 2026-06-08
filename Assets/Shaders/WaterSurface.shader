Shader "Custom/WaterSurface"
{
    Properties
    {
        // Color
        _DeepColor      ("Deep Color",         Color)        = (0.05, 0.15, 0.35, 0.9)
        _ShallowColor   ("Shallow Color",      Color)        = (0.3,  0.65, 0.75, 0.6)

        // Normal maps (two scrolling layers)
        _NormalA        ("Normal Map A",       2D)           = "bump" {}
        _NormalB        ("Normal Map B",       2D)           = "bump" {}
        _NormalTiling   ("Normal Tiling",      Float)        = 2.0
        _NormalStrength ("Normal Strength",    Range(0,2))   = 0.6
        _ScrollA        ("Scroll Speed A",     Vector)       = (0.04, 0.02, 0, 0)
        _ScrollB        ("Scroll Speed B",     Vector)       = (-0.02, 0.05, 0, 0)

        // Fresnel
        _FresnelPower   ("Fresnel Power",      Range(0.1,8)) = 3.0

        // Wave vertex offset
        _WaveSpeed      ("Wave Speed",         Float)        = 1.2
        _WaveStrength   ("Wave Strength",      Float)        = 0.12
        _WaveScale      ("Wave Scale",         Float)        = 0.4

        // Depth foam
        _FoamDepth      ("Foam Depth Range",   Float)        = 0.4
        _FoamColor      ("Foam Color",         Color)        = (1,1,1,1)

        // Cel specular
        _SpecColor2     ("Specular Color",     Color)        = (1,1,1,1)
        _Shininess      ("Shininess",          Float)        = 64.0
        _CelSpecCut     ("Cel Specular Cutoff",Range(0,1))   = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ---- Textures & Samplers ----
            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);

            // ---- CBuffer ----
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
                float4 tangentWS   : TEXCOORD2;
                float2 uv          : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                float  fogFactor   : TEXCOORD5;
                float4 screenPos   : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---- Wave helper ----
            // Sine-wave vertex offset using _WaveSpeed/_WaveStrength/_WaveScale
            float WaveHeight(float3 posWS)
            {
                float wave = sin(posWS.x * _WaveScale + _Time.y * _WaveSpeed)
                           + sin(posWS.z * _WaveScale * 0.7 + _Time.y * _WaveSpeed * 1.3);
                return wave * _WaveStrength * 0.5;
            }

            // ---- Vertex ----
            Varyings vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Apply wave displacement in object space (Y axis)
                float3 posOS = IN.positionOS.xyz;
                float3 posWS_pre = TransformObjectToWorld(posOS);
                posOS.y += WaveHeight(posWS_pre);

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = nrmInputs.normalWS;
                OUT.tangentWS  = float4(nrmInputs.tangentWS,
                                        IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv         = IN.uv;
                OUT.shadowCoord= GetShadowCoord(posInputs);
                OUT.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);
                OUT.screenPos  = ComputeScreenPos(posInputs.positionCS);
                return OUT;
            }

            // ---- Fragment ----
            half4 frag(Varyings IN) : SV_Target
            {
                // Scrolling UV for two normal layers
                float2 baseUV  = IN.positionWS.xz * _NormalTiling;
                float2 uvA     = baseUV + _ScrollA * _Time.y;
                float2 uvB     = baseUV + _ScrollB * _Time.y;

                // Sample and combine normals (additive XY blend)
                float3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, uvA));
                float3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, uvB));
                float3 combinedTangentNrm = normalize(float3(
                    (nA.xy + nB.xy) * _NormalStrength,
                    nA.z));

                // TBN transform
                float3 normWS     = normalize(IN.normalWS);
                float3 tangentWS  = normalize(IN.tangentWS.xyz);
                float3 bitangentWS= normalize(cross(normWS, tangentWS) * IN.tangentWS.w);
                float3 N = normalize(combinedTangentNrm.x * tangentWS +
                                     combinedTangentNrm.y * bitangentWS +
                                     combinedTangentNrm.z * normWS);

                // View direction
                float3 viewDirWS = normalize(GetCameraPositionWS() - IN.positionWS);

                // Fresnel lerp between DeepColor and ShallowColor
                float  fresnel   = pow(1.0 - saturate(dot(N, viewDirWS)), _FresnelPower);
                float4 waterColor= lerp(_DeepColor, _ShallowColor, fresnel);

                // Depth-based foam using SampleSceneDepth
                float2 screenUV  = IN.screenPos.xy / IN.screenPos.w;
                float  sceneRawZ = SampleSceneDepth(screenUV);
                float  sceneZ    = LinearEyeDepth(sceneRawZ, _ZBufferParams);
                float  surfaceZ  = IN.screenPos.w;         // eye-space depth of this fragment
                float  depthDiff = sceneZ - surfaceZ;

                // Rim-fresnel fallback if depth texture unavailable (depthDiff ~ 0 or negative)
                float  rimFresnel = pow(1.0 - saturate(dot(normWS, viewDirWS)), 4.0);
                float  foamMask   = (depthDiff > 0.001)
                                    ? saturate(1.0 - depthDiff / _FoamDepth)
                                    : rimFresnel * 0.5;

                // Main light + shadow
                Light  mainLight  = GetMainLight(IN.shadowCoord);
                float3 L          = mainLight.direction;
                float  NdotL      = saturate(dot(N, L));
                float  shadowAtten= mainLight.shadowAttenuation;

                // Cel-shaded specular: step(_CelSpecCut, pow(NdotH,...))
                float3 H          = normalize(L + viewDirWS);
                float  NdotH      = saturate(dot(N, H));
                float  specRaw    = pow(NdotH, _Shininess);
                float  specular   = step(_CelSpecCut, specRaw) * shadowAtten;

                float3 lightContrib = mainLight.color * (NdotL * shadowAtten + specular * _SpecColor2.rgb);

                // SH ambient
                float3 ambient    = SampleSH(N);

                float3 finalRGB   = waterColor.rgb * (lightContrib + ambient);

                // Foam overlay
                finalRGB = lerp(finalRGB, _FoamColor.rgb, foamMask);

                // Alpha: blend base alpha with foam
                float  alpha      = lerp(waterColor.a, 1.0, foamMask);

                // Fog
                finalRGB = MixFog(finalRGB, IN.fogFactor);

                return half4(finalRGB, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

Shader "Custom/FurShaderShell"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MainTex("Texture", 2D) = "white" {}

        _Specular("Specular", Color) = (0, 0, 0, 1)
        _Shininess("Shininess", Range(0.01, 128.0)) = 8.0
        _FurDirLightExposure ("Fur Direct Light Exposure", Range(0, 5)) = 1
        _LightFilter("Light Filter",  Range(-1.0, 1.0)) = 0.1
        _FresnelLV ("Fresnel Level", Range(0, 5)) = 1
        _StrandSpecColor1 ("Strand Spec Color 1", Color) = (1, 1, 1, 1)
        _StrandSpecColor2 ("Strand Spec Color 2", Color) = (1, 0.85, 0.65, 1)

        _StrandSpecPower1 ("Strand Spec Power 1", Range(1, 256)) = 64
        _StrandSpecPower2 ("Strand Spec Power 2", Range(1, 256)) = 32

        _SpecShift1 ("Spec Shift 1", Range(-1, 1)) = 0.1
        _SpecShift2 ("Spec Shift 2", Range(-1, 1)) = -0.2

        _StrandSpecStrength ("Strand Spec Strength", Range(0, 5)) = 1


        [NoScaleOffset] _OcclusionMap ("AO Map", 2D) = "white" {}
        //_OcclusionStrength ("AO Strength", Range(0, 1)) = 1
        _OcclusionColor ("AO Color", Color) = (0, 0, 0, 1)
        
        
        _FurTex("Fur Pattern", 2D) = "white" {}        
        _FurLength("Fur Length", Range(0.0, 1)) = 0.5
        _FurDensity("Fur Density", Range(0, 2)) = 0.57
        _FurThinness ("Fur Thinness", Range(0.01, 10)) = 5
        _FurShading ("Fur Shading", Range(0.0, 1)) = 0.302
        
        _ForceGlobal ("Force Global", Vector) = (0, 0, 0, 0)
        _ForceLocal ("Force Local", Vector) = (0, 0, 0, 0)

    }
    SubShader
    {
        Tags
        {
            "LightMode" = "UniversalForward"
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        pass{
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_FurTex); SAMPLER(sampler_FurTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _Specular;
                float _FurDirLightExposure;
                float _LightFilter;

                float _Shininess;
                float _OcclusionStrength;
                float4 _OcclusionColor;
                float _FresnelLV;
                float4 _StrandSpecColor1;
                float4 _StrandSpecColor2;
                float _StrandSpecPower1;
                float _StrandSpecPower2;
                float _SpecShift1;
                float _SpecShift2;
                float _StrandSpecStrength;

                float4 _FurTex_ST;
                float _FurLayerCount;
                float _FurLength;
                float _FurDensity;
                float _FurThinness;
                float _FurShading;

                float4 _ForceGlobal;
                float4 _ForceLocal;

            CBUFFER_END

            struct VertexData 
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;

            };

            struct Interpolators 
            {
                float4 posCS: SV_POSITION;
                float4 uv: TEXCOORD0;
                float3 normalWS: TEXCOORD1;
                float3 normalVS: TEXCOORD2;
                float3 posWS: TEXCOORD3;
                half furStep : TEXCOORD4;
                float3 tangentWS : TEXCOORD5;
                float3 bitangentWS : TEXCOORD6;
            };

            Interpolators vert(VertexData v) {
                Interpolators i;
                float furStep = (v.instanceID + 1.0) / max(_FurLayerCount, 1.0);
                float3 furPositionOS = v.positionOS.xyz + v.normalOS * _FurLength * furStep;
                furPositionOS += clamp(TransformWorldToObjectDir(_ForceGlobal.xyz, false) + _ForceLocal.xyz, -1, 1) 
                                * pow(furStep, 3) * _FurLength;
                VertexPositionInputs posInputs = GetVertexPositionInputs(furPositionOS);
                VertexNormalInputs normInputs = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                
                i.posCS = posInputs.positionCS;
                i.uv.xy = TRANSFORM_TEX(v.uv, _MainTex);
                i.uv.zw = TRANSFORM_TEX(v.uv, _FurTex);
                i.normalWS = normInputs.normalWS;
                i.normalVS = normalize(TransformWorldToViewDir(i.normalWS));
                i.posWS = posInputs.positionWS;
                i.furStep = furStep;
                i.tangentWS = normInputs.tangentWS;
                i.bitangentWS = normInputs.bitangentWS;

                return i;
            }

            half StrandSpecular(half3 T, half3 V, half3 L, half exponent)
            {
                half3 H = normalize(L + V);
                half dotTH = dot(T, H);
                half sinTH = sqrt(saturate(1.0 - dotTH * dotTH));
                half dirAtten = smoothstep(-1.0, 0.0, dotTH);
                return dirAtten * pow(sinTH, exponent);
            }

            half4 frag(Interpolators i) : SV_Target{
                Light mainLight = GetMainLight();

                half3 normalWS = normalize(i.normalWS);
                half3 normalVS = normalize(i.normalVS);
                half3 lightWS = normalize(mainLight.direction);
                half3 viewWS = normalize(GetWorldSpaceViewDir(i.posWS));
                half3 halfWS = normalize(viewWS + lightWS);
                
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).rgb * _Color.rgb;
                half3 noiseCombine = SAMPLE_TEXTURE2D(_FurTex, sampler_FurTex, i.uv.zw * _FurThinness).rgb;
                half mixedNoise =  noiseCombine.g * 0.9 + noiseCombine.b * 0.8;
                half alpha = saturate(mixedNoise - (i.furStep * i.furStep) * _FurDensity);
                // half rawAO = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv.xy).r;
                // half ao = lerp(1.0, rawAO, _OcclusionStrength);
                // Ignoring The texture for fast iteration, COME BACK TO THIS LATER
                half normalAO = saturate(normalVS.y * 0.25 + 0.35);
                half3 SH = normalAO.xxx;
                half occlusion = i.furStep * i.furStep + 0.04;
                occlusion = saturate(occlusion);
                half3 SHL = lerp(_OcclusionColor.rgb * SH, SH, occlusion);
                // Adding Rim Light to enhance silhouette
                half fresnel = 1 - max(0, dot(normalWS, viewWS));
                half rimLight = fresnel * occlusion;
                rimLight *= rimLight * _FresnelLV * SH;
                SHL += rimLight;

                //direct lights
                half3 lightDir = normalize(mainLight.direction);
                half NoL = dot(lightDir, normalWS);
                half wrappedNdotL = saturate(NoL * 0.5 + 0.5);
                half3 dirLight = mainLight.color * wrappedNdotL * _FurDirLightExposure;
                //half3 ambient = SHL * albedo;
                //half3 diffuse = mainLight.color * albedo * saturate(dot(normalWS, lightWS));
                // half3 specular = mainLight.color * _Specular.rgb * pow(saturate(dot(normalWS, halfWS)), _Shininess);
                half3 T1 = normalize(i.bitangentWS + _SpecShift1 * normalWS);
                half3 T2 = normalize(i.bitangentWS + _SpecShift2 * normalWS);

                half spec1 = StrandSpecular(T1, viewWS, lightWS, _StrandSpecPower1);
                half spec2 = StrandSpecular(T2, viewWS, lightWS, _StrandSpecPower2);

                // 外层高光更明显
                spec1 *= i.furStep;
                spec2 *= i.furStep;

                // 用 noise/alpha 做高光细节遮罩
                half specMask = mixedNoise * mixedNoise;

                half3 strandSpecular =
                    (spec1 * _StrandSpecColor1.rgb + spec2 * _StrandSpecColor2.rgb)
                    * specMask
                    * occlusion
                    * mainLight.color
                    * _StrandSpecStrength;
                half directVisibility = saturate(NoL + _LightFilter);
                strandSpecular *= directVisibility;
                half3 color = (SHL + dirLight) * albedo + strandSpecular;//
                
                return half4(color, alpha);  
                }
            ENDHLSL
        }
    }
    FallBack "Diffuse"
}

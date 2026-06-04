Shader "Custom/FurShaderBase"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MainTex("Texture", 2D) = "white" {}

        _Specular("Specular", Color) = (0, 0, 0, 1)
        _Shininess("Shininess", Range(0.01, 128.0)) = 8.0

        [NoScaleOffset] _OcclusionMap ("AO Map", 2D) = "white" {}
        _OcclusionStrength ("AO Strength", Range(0, 1)) = 1
        
        _FurTex("Fur Pattern", 2D) = "white" {}
        _FurStep("FURSTEP", Range(0.0, 1)) = 0
        
        _FurLength("Fur Length", Range(0.0, 1)) = 0.5
        _FurDensity("Fur Density", Range(0, 2)) = 0.57
        _FurThinness ("Fur Thinness", Range(0.01, 10)) = 5
        _FurShading ("Fur Shading", Range(0.0, 1)) = 0.302
        
        _ForceGlobal ("Force Global", Vector) = (0, 0, 0, 0)
        _ForceLocal ("Force Local", Vector) = (0, 0, 0, 0)

        _RimColor ("Rim Color", Color) = (0, 0, 0, 1)
        _RimPower ("Rim Power", Range(0.0, 8.0)) = 6.0
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
                float _Shininess;
                float _OcclusionStrength;

                float4 _FurTex_ST;
                float _FurStep;
                
                float _FurLength;
                float _FurDensity;
                float _FurThinness;
                float _FurShading;

                float4 _ForceGlobal;
                float4 _ForceLocal;

                float4 _RimColor;
                float _RimPower;
            CBUFFER_END

            struct VertexData 
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;

            };

            struct Interpolators 
            {
                float4 posCS: SV_POSITION;
                float4 uv: TEXCOORD0;
                float3 normalWS: TEXCOORD1;
                float3 posWS: TEXCOORD2;
            };

            Interpolators vert(VertexData v) {
                Interpolators i;
                float3 furPositionOS = v.positionOS.xyz + v.normalOS * _FurLength * _FurStep;
                furPositionOS += clamp(TransformWorldToObjectDir(_ForceGlobal.xyz, false) + _ForceLocal.xyz, -1, 1) 
                                * pow(_FurStep, 3) * _FurLength;
                VertexPositionInputs posInputs = GetVertexPositionInputs(furPositionOS);
                VertexNormalInputs normInputs = GetVertexNormalInputs(v.normalOS, v.tangentOS);

                i.posCS = posInputs.positionCS;
                i.uv.xy = TRANSFORM_TEX(v.uv, _MainTex);
                i.uv.zw = TRANSFORM_TEX(v.uv, _FurTex);

                i.normalWS = normInputs.normalWS;
                i.posWS = posInputs.positionWS;
                return i;
            }

            half4 frag(Interpolators i) : SV_Target{
                Light mainLight = GetMainLight();

                half3 normalWS = normalize(i.normalWS);
                half3 lightWS = normalize(mainLight.direction);
                half3 viewWS = normalize(GetWorldSpaceViewDir(i.posWS));
                half3 halfWS = normalize(viewWS + lightWS);
                
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy).rgb * _Color.rgb;
                albedo -= (pow(1 - _FurStep, 3)) * _FurShading;
                float rim = 1.0 - saturate(dot(viewWS, normalWS));
                albedo += half4(_RimColor.rgb * pow(rim, _RimPower), 1.0);

                half rawAO = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv.xy).r;
                half ao = lerp(1.0, rawAO, _OcclusionStrength);

                half3 ambient = SampleSH(normalWS) * albedo * ao;
                half3 diffuse = mainLight.color * albedo * saturate(dot(normalWS, lightWS));
                half3 specular = mainLight.color * _Specular.rgb * pow(saturate(dot(normalWS, halfWS)), _Shininess);

                half3 color = ambient + diffuse + specular;
                half noise = SAMPLE_TEXTURE2D(_FurTex, sampler_FurTex, i.uv.zw * _FurThinness).r;
                half alpha = saturate(noise - (_FurStep * _FurStep) * _FurDensity);
                return half4(color, alpha);  
                }
            ENDHLSL
        }
    }
    FallBack "Diffuse"
}

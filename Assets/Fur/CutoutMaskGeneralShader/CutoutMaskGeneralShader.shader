Shader "Custom/Cutout Mask General Shader"
{
    Properties
    {
        [Header(PBR Parameters)]
        _MainTex ("Main Texture", 2D) = "white" {}
        _MetallicGlossMap ("Metallic Map", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1.0

        [Space(10)]
        [Header(Visibility Mask)]
        [NoScaleOffset] _VisibilityMask ("Visibility Mask (R Fade)", 2D) = "black" {}
        _MaskThreshold ("Mask (R Fade) Threshold", Range(0, 1)) = 0.99

        [Space(10)]
        [Header(Fade Parameters)]
        _FadeIn ("Fade In (Global Alpha)", Range(0, 1)) = 1
        _FadeInWidth("Fade In Width", Range(0, 1)) = 0.1
        
       
    }
    
    SubShader
    {
        // Queue=Geometry-1(1999)：在白领子(Geometry 2000)之前执行两个 Pass
        // Pass1 写深度遮挡领子，Pass2 用 alpha blend 实现渐隐，两者都早于领子渲染
        Tags { "RenderType"="transparent" "Queue"="Geometry-1" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        // =========================================================
        // Pass 1：深度预写
        // 仅对 effectiveFadeIn=1（完全不透明）的片元写深度
        // → 白领子在此之后渲染时，被遮挡的片元深度测试失败，不会穿透毛衣
        // =========================================================
        Pass
        {
            Name "DepthPrepass"
            Cull Off
            ColorMask 0
            ZWrite On
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CutoutMaskGeneralShaderToolbox.cginc" //共用代码
            
            struct AttrD  { float4 posOS : POSITION; float2 uv : TEXCOORD0; };
            struct VaryD  { float4 posCS : SV_POSITION; float3 posWS : TEXCOORD0; 
                            float2 uvMain : TEXCOORD1; float2 uvMask :TEXCOORD2; float3 posOS : TEXCOORD3; };
            
            VaryD vertD(AttrD v)
            {
                VaryD o;
                VertexPositionInputs p = GetVertexPositionInputs(v.posOS.xyz);
                o.posCS = p.positionCS;
                o.posWS = p.positionWS;
                o.uvMain = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvMask = v.uv; //直接用原始UV采样Mask，避免ST变换引入误差
                o.posOS = v.posOS.xyz;
                return o;
            }

            half4 fragD(VaryD i) : SV_Target
            {
                float alpha = ComputeMaskThresholdAlpha(i.uvMask, i.posOS);
                clip(alpha); // 只对完全不透明区域写深度
                return 0;
            }
            ENDHLSL
        }

        // =========================================================
        // Pass 2：光照 + alpha 渐隐
        // =========================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CutoutMaskGeneralShaderToolbox.cginc" //共用代码 

            TEXTURE2D(_MainTex);        SAMPLER(sampler_MainTex);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);
            TEXTURE2D(_OcclusionMap);   SAMPLER(sampler_OcclusionMap);
            

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvMain : TEXCOORD0;
                float2 uvMask : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float3 tangentWS : TEXCOORD4;
                float3 bitangentWS : TEXCOORD5;
                float3 posOS : TEXCOORD6;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs posInputs = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                
                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS = normInputs.normalWS;
                o.tangentWS = normInputs.tangentWS;
                o.bitangentWS = normInputs.bitangentWS;
                o.uvMain = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvMask = v.uv; // 直接用原始UV采样Mask，避免ST变换引入误差
                o.posOS = v.positionOS.xyz;
                return o;
            }
            
            half4 frag (Varyings i,  bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float effectiveFadeIn = ComputeMaskThresholdAlpha(i.uvMask, i.posOS);

                // 1. 采样贴图
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uvMain);
                half4 metallicGloss = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uvMain);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uvMain), _BumpScale);
                half occlusion = lerp(1.0h, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uvMain).g, _OcclusionStrength);
                
                // 2. 更精确的法线转换 
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(i.tangentWS, i.bitangentWS, i.normalWS));
                normalWS = normalize(normalWS);
                
                // 3. 准备 PBR 需要的表面数据 (SurfaceData)
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo.rgb;
                surfaceData.metallic = metallicGloss.r * _Metallic;
                surfaceData.smoothness = metallicGloss.a * _Glossiness;
                surfaceData.occlusion = occlusion;
                surfaceData.alpha = 1.0;
                surfaceData.specular = half3(0,0,0);
                surfaceData.emission = half3(0,0,0);
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;

                // 4. 准备 PBR 需要的环境和灯光数据 (InputData)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = normalize(GetWorldSpaceViewDir(i.positionWS));
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.shadowCoord = shadowCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                // 直接调用 URP 内置的 PBR 渲染核心，输出带 alpha 的颜色
                half3 finalColor = UniversalFragmentPBR(inputData, surfaceData);
                return half4(finalColor, effectiveFadeIn);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
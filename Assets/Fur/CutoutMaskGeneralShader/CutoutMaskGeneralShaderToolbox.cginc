#if !defined(UNITY_SHADER_CUTOUT_TOOLBOX_INCLUDED)
#define UNITY_SHADER_CUTOUT_TOOLBOX_INCLUDED

TEXTURE2D(_VisibilityMask);   SAMPLER(sampler_VisibilityMask);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _BumpMap_ST;
    half _Metallic; 
    half _Glossiness; 
    half _BumpScale; 
    half _OcclusionStrength;
    
    float _MaskThreshold;

    float _FadeIn; 
    float _FadeInWidth;
CBUFFER_END


float ComputeMaskThresholdAlpha(float2 uvMask, float3 positionOS)
{
    float4 mask = SAMPLE_TEXTURE2D(_VisibilityMask, sampler_VisibilityMask, uvMask);
    float maskRed = step(_MaskThreshold, mask.r);
    float maskAlpha = mask.a;

    float upperBound = min(maskAlpha + _FadeInWidth, 1.0);
    float lowerBound = max(maskAlpha - _FadeInWidth, 0.0);

    float thresholdAlpha = saturate((_FadeIn - lowerBound) / max(upperBound - lowerBound, 1e-5));
    thresholdAlpha = smoothstep(0.0, 1.0, thresholdAlpha);
    float finalAlpha = lerp(1.0, thresholdAlpha, maskRed);
    return saturate(finalAlpha);
}
#endif
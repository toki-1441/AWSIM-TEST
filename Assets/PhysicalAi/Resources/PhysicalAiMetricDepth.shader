// Copyright 2026
// SPDX-License-Identifier: Apache-2.0
Shader "Hidden/PhysicalAi/MetricDepth"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "MetricDepth"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
            float _PhysicalAiDepthMax;
            float Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float deviceDepth = CustomPassLoadCameraDepth(uint2(input.positionCS.xy));
                float metres = LinearEyeDepth(deviceDepth, _ZBufferParams);
                // Zero is invalid: sky, far clip, or beyond the configured sensor range.
                return deviceDepth == UNITY_RAW_FAR_CLIP_VALUE || metres > _PhysicalAiDepthMax ? 0 : metres;
            }
            ENDHLSL
        }
    }
    Fallback Off
}

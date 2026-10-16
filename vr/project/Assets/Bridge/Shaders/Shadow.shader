// SPDX-License-Identifier: LicenseRef-BRIDGE
// A soft rounded-rectangle shadow under a floating glass panel (or anything boxy), so the panel reads
// as standing off what is behind it. The quad is the panel grown by the softness on every side: uv1 is
// its size in metres, uv2 is (corner radius, softness), and the vertex colour's alpha is the strength.
Shader "Bridge/Shadow"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-1" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Shadow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "BridgeCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 size : TEXCOORD1;
                float4 shape : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 size : TEXCOORD1;
                float4 shape : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.size = v.size;
                o.shape = v.shape;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float soft = max(i.shape.y, 1e-4);
                float2 p = (i.uv - 0.5) * i.size;
                float d = RoundedRectDistance(p, i.size * 0.5 - soft, i.shape.x);
                float a = 1.0 - smoothstep(-soft * 0.35, soft, d);
                a *= a;
                return half4(i.color.rgb, a * i.color.a * _Strength);
            }
            ENDHLSL
        }
    }
}

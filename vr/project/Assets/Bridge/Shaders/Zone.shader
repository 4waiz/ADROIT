// SPDX-License-Identifier: LicenseRef-BRIDGE
// A safety laser scanner's field drawn on the floor, and the target rings lessons ask you to stand
// in: a quad lying flat whose uv0 spans the outer circle. The band between _Inner and 1 (fractions
// of the radius) is filled faintly, its outer edge drawn bright with dashes that run round it.
Shader "Bridge/Zone"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.7, 0.1, 1)
        _Inner ("Inner radius (fraction)", Range(0, 1)) = 0
        _Fill ("Fill", Range(0, 1)) = 0.12
        _Edge ("Edge", Range(0, 2)) = 0.9
        _EdgeWidth ("Edge width (fraction)", Range(0.001, 0.2)) = 0.02
        _Dashes ("Dashes", Float) = 48
        _Speed ("Dash speed", Float) = 0.15
        _Pulse ("Pulse", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Zone"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "BridgeCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Inner;
                float _Fill;
                float _Edge;
                float _EdgeWidth;
                float _Dashes;
                float _Speed;
                float _Pulse;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 c = i.uv * 2.0 - 1.0;
                float r = length(c);
                float aa = max(fwidth(r), 1e-4);
                float outer = 1.0 - smoothstep(1.0 - aa, 1.0, r);
                float inner = smoothstep(_Inner - aa, _Inner, r);
                float band = outer * inner;
                float edge = smoothstep(1.0 - _EdgeWidth - aa, 1.0 - _EdgeWidth, r) * outer;
                float angle = atan2(c.y, c.x) / 6.2831853 + 0.5;
                float dash = step(0.45, frac(angle * _Dashes - _Time.y * _Speed));
                float pulse = 1.0 + _Pulse * sin(_Time.y * 7.0);
                float a = (band * _Fill + edge * _Edge * lerp(0.55, 1.0, dash)) * pulse;
                return half4(_Color.rgb * i.color.rgb, saturate(a * _Color.a * i.color.a));
            }
            ENDHLSL
        }
    }
}

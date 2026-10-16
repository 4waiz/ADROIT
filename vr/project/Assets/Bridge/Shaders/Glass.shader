// SPDX-License-Identifier: LicenseRef-BRIDGE
// The UI's frosted glass. A panel is one quad that carries its own shape in its vertices: uv0 runs
// 0..1 across it, uv1 is its size in metres, uv2 is (corner radius, -, -, rim width) and the vertex
// colour is the tint, its alpha how frosted it is. The panel blurs the scene behind it (URP's opaque
// texture), mixes in the tint, and adds the light a real pane catches: a bright rim along its upper
// edges, a darker lower lip, a sheen that slides as the head moves. With _Blur at 0 it is a flat
// overlay instead (buttons, bars and chips that sit on a panel).
Shader "Bridge/Glass"
{
    Properties
    {
        _Blur ("Blur radius (texels)", Float) = 10
        _Frost ("Frost", Range(0, 1)) = 1
        _Rim ("Rim light", Range(0, 2)) = 1
        _Sheen ("Sheen", Range(0, 1)) = 0.35
        _Saturation ("Background saturation", Range(0, 2)) = 1.15
        _Brighten ("Background lift", Range(0, 1)) = 0.06
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glass"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "BridgeCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Blur;
                float _Frost;
                float _Rim;
                float _Sheen;
                float _Saturation;
                float _Brighten;
                float _ZWrite;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
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
                float4 screenPos : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float3 normalWS : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.size = v.size;
                o.shape = v.shape;
                o.color = v.color;
                return o;
            }

            // A golden-angle disc of taps over the (downsampled) opaque texture.
            half3 BlurScene(float2 uv, float radius)
            {
                float2 texel = _CameraOpaqueTexture_TexelSize.xy;
                half3 sum = 0;
                float total = 0;
                [unroll]
                for (int k = 0; k < 16; k++)
                {
                    float r = sqrt((k + 0.5) / 16.0);
                    float a = k * 2.39996323;
                    float2 o = float2(cos(a), sin(a)) * r * radius * texel;
                    float w = 1.0 - 0.5 * r;
                    sum += SampleSceneColor(uv + o) * w;
                    total += w;
                }
                return sum / total;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 half_ = i.size * 0.5;
                float2 p = (i.uv - 0.5) * i.size;
                float d = RoundedRectDistance(p, half_, i.shape.x);
                float aa = max(fwidth(d), 1e-5);
                float coverage = saturate(0.5 - d / aa);
                clip(coverage - 0.002);
                float inside = -d;

                half frost = saturate(i.color.a * _Frost);
                half3 col;
                half alpha;
                if (_Blur > 0.5)
                {
                    float2 suv = i.screenPos.xy / max(i.screenPos.w, 1e-5);
                    half3 bg = BlurScene(suv, _Blur);
                    half lum = dot(bg, half3(0.2126, 0.7152, 0.0722));
                    bg = lerp(lum.xxx, bg, _Saturation) + _Brighten;
                    col = lerp(bg, i.color.rgb, frost);
                    alpha = 1;
                }
                else
                {
                    col = i.color.rgb;
                    alpha = frost;
                }

                // Light from above: a little brighter at the top, a bright rim on the upper edges and
                // a darker lip below.
                col *= lerp(0.95, 1.04, i.uv.y);
                float rimWidth = max(i.shape.w, 0.0006);
                float rim = 1.0 - smoothstep(0.0, rimWidth, inside);
                float2 dir = normalize(p / max(half_, 1e-4) + 1e-5);
                float lit = saturate(dot(dir, normalize(float2(-0.45, 0.9))) * 0.5 + 0.5);
                col += rim * lerp(0.04, 0.5, lit) * _Rim;
                col -= (1.0 - smoothstep(0.0, rimWidth * 3.0, inside)) * (1.0 - lit) * 0.07 * _Rim;
                if (_Blur <= 0.5) alpha = saturate(alpha + rim * lerp(0.05, 0.35, lit) * _Rim);

                // A sheen that slides with the view, and a touch of Fresnel at grazing angles.
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 N = normalize(i.normalWS);
                float fresnel = pow(1.0 - saturate(abs(dot(N, V))), 4.0);
                float band = p.x / max(i.size.x, 1e-3) + 0.6 * p.y / max(i.size.y, 1e-3) + 0.8 * dot(V, float3(1, 0.35, 0));
                float sheen = exp(-pow((band - 0.15) * 3.2, 2.0)) * _Sheen * 0.12;
                col += sheen + fresnel * 0.08;
                col += (BridgeHash(i.positionCS.xy) - 0.5) / 255.0;
                return half4(col, alpha * coverage);
            }
            ENDHLSL
        }
    }
}

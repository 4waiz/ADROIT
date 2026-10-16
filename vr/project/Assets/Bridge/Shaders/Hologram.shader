// SPDX-License-Identifier: LicenseRef-BRIDGE
// The digital twin's look, and the ghost a lesson shows as a target pose: a translucent shell whose
// edges glow (Fresnel), crossed by slow scan lines. A first pass writes depth only, so the far side
// of the model does not show through its near side.
Shader "Bridge/Hologram"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.25, 0.75, 1.4, 1)
        _Base ("Body", Range(0, 1)) = 0.12
        _RimPower ("Rim power", Range(0.5, 8)) = 2.4
        _Rim ("Rim", Range(0, 4)) = 1.4
        _ScanDensity ("Scan lines per metre", Float) = 38
        _ScanSpeed ("Scan speed", Float) = 0.35
        _Scan ("Scan strength", Range(0, 1)) = 0.18
        _Pulse ("Pulse", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HologramDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "BridgeCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Base;
                float _RimPower;
                float _Rim;
                float _ScanDensity;
                float _ScanSpeed;
                float _Scan;
                float _Pulse;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "BridgeCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Base;
                float _RimPower;
                float _Rim;
                float _ScanDensity;
                float _ScanSpeed;
                float _Scan;
                float _Pulse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fresnel = pow(1.0 - saturate(abs(dot(N, V))), _RimPower);
                float scan = 0.5 + 0.5 * sin((i.positionWS.y * _ScanDensity - _Time.y * _ScanSpeed * _ScanDensity) * 6.2831853);
                scan = smoothstep(0.65, 1.0, scan);
                float pulse = 1.0 + _Pulse * (0.5 + 0.5 * sin(_Time.y * 6.0));
                float a = (_Base + fresnel * _Rim + scan * _Scan) * pulse;
                return half4(_Color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}

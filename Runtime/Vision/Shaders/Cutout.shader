// S-01 cutout: draws the camera picture and its person mask that the Python sidecar sends (CutoutWire). Unity does not segment anything;
// this shader only chooses what to show of a picture and a mask it was given.
//
//   _Mode 0  Original    the camera picture, opaque; the mask is ignored
//   _Mode 1  Person      the picture where the mask says person, transparent elsewhere (put any backdrop behind it)
//   _Mode 2  Silhouette  _SilhouetteColor where the mask says person, transparent elsewhere
//
// Both textures hold the TOP row first (the wire's order), so the shader samples at (u, 1 - v). The mask covers the whole picture even when it
// is smaller than it: one uv addresses both. The optional glow is a halo OUTSIDE the person: the brightest mask value within _GlowWidth mask
// pixels, minus this pixel's own, so it traces the outline of whatever the mask contains, one person or several.
//
// URP, unlit, alpha blended, no depth write. 16 extra mask taps while the glow is on; none while it is off.
Shader "VirtualMirror/Cutout"
{
    Properties
    {
        [NoScaleOffset] _ColorTex ("Camera picture (top row first)", 2D) = "white" {}
        [NoScaleOffset] _MaskTex ("Person mask (top row first)", 2D) = "black" {}
        [Enum(Original,0,Person,1,Silhouette,2)] _Mode ("Mode", Float) = 1
        [Toggle] _Mirror ("Mirror left to right", Float) = 0
        _SilhouetteColor ("Silhouette colour", Color) = (1, 0.08, 0.08, 1)
        _EdgeContrast ("Edge contrast (1 = the mask as sent)", Range(0.5, 4)) = 1
        _GlowColor ("Glow colour", Color) = (1, 0.35, 0.35, 1)
        _GlowWidth ("Glow width (mask pixels)", Range(0, 16)) = 0
        _GlowStrength ("Glow strength", Range(0, 2)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Cutout"
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ColorTex);
            SAMPLER(sampler_ColorTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_TexelSize;
                half4 _SilhouetteColor;
                half4 _GlowColor;
                half _Mode;
                half _Mirror;
                half _EdgeContrast;
                half _GlowWidth;
                half _GlowStrength;
                half _Opacity;
            CBUFFER_END

            static const float2 kRing[8] = {
                float2(1.0, 0.0), float2(0.7071, 0.7071), float2(0.0, 1.0), float2(-0.7071, 0.7071),
                float2(-1.0, 0.0), float2(-0.7071, -0.7071), float2(0.0, -1.0), float2(0.7071, -0.7071)
            };

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv).r;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = float2(_Mirror > 0.5 ? 1.0 - input.uv.x : input.uv.x, 1.0 - input.uv.y);
                half3 picture = SAMPLE_TEXTURE2D(_ColorTex, sampler_ColorTex, uv).rgb;
                if (_Mode < 0.5)
                {
                    return half4(picture, _Opacity);
                }

                half mask = SampleMask(uv);
                half person = saturate((mask - 0.5h) * _EdgeContrast + 0.5h);
                half3 body = _Mode < 1.5 ? picture : _SilhouetteColor.rgb;
                half bodyAlpha = person * (_Mode < 1.5 ? 1.0h : _SilhouetteColor.a);

                half glow = 0.0h;
                if (_GlowWidth > 0.01h && _GlowStrength > 0.001h)
                {
                    float2 reachStep = _MaskTex_TexelSize.xy * _GlowWidth;
                    half reach = 0.0h;
                    [unroll]
                    for (int k = 0; k < 8; k++)
                    {
                        reach = max(reach, SampleMask(uv + kRing[k] * reachStep));
                        reach = max(reach, SampleMask(uv + kRing[k] * reachStep * 0.5));
                    }
                    glow = saturate(reach - person) * _GlowStrength * _GlowColor.a;
                }

                half behind = glow * (1.0h - bodyAlpha);
                half alpha = bodyAlpha + behind;
                half3 rgb = (body * bodyAlpha + _GlowColor.rgb * behind) / max(alpha, 0.0001h);
                return half4(rgb, saturate(alpha) * _Opacity);
            }
            ENDHLSL
        }
    }
}

// S-01 cutout backdrop: what a person cut out of the camera picture is shown in front of. One full-view quad, opaque, drawn before the
// person. Kinds: 0 transparency checkerboard, 1 one colour, 2 a vertical gradient (A at the top, B at the bottom), 3 an image (cover fit,
// so it fills the view and crops what does not fit). "No backdrop" is not a kind: the view switches this quad off and the scene behind it
// (the camera's clear colour, any 3D content) shows through the transparent parts of the person.
Shader "VirtualMirror/CutoutBackdrop"
{
    Properties
    {
        [Enum(Checker,0,Color,1,Gradient,2,Image,3)] _Kind ("Kind", Float) = 0
        _ColorA ("Colour A (top)", Color) = (0.02, 0.03, 0.06, 1)
        _ColorB ("Colour B (bottom)", Color) = (0.10, 0.16, 0.30, 1)
        _CheckerA ("Checker light", Color) = (0.36, 0.36, 0.38, 1)
        _CheckerB ("Checker dark", Color) = (0.24, 0.24, 0.26, 1)
        _Cells ("Checker squares across the short side", Range(4, 64)) = 18
        _Aspect ("View width over height", Float) = 1.7778
        [NoScaleOffset] _ImageTex ("Image", 2D) = "black" {}
        _ImageAspect ("Image width over height", Float) = 1.7778
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-10" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Backdrop"
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ImageTex);
            SAMPLER(sampler_ImageTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                half4 _CheckerA;
                half4 _CheckerB;
                half _Kind;
                half _Cells;
                half _Aspect;
                half _ImageAspect;
            CBUFFER_END

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

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                if (_Kind < 0.5)
                {
                    // Squares, not rectangles: the long side gets proportionally more of them. The short side of the view has _Cells.
                    float scale = _Cells / min(1.0, _Aspect);
                    float2 cell = floor(float2(uv.x * _Aspect, uv.y) * scale);
                    half parity = fmod(cell.x + cell.y, 2.0);
                    return parity < 0.5h ? _CheckerA : _CheckerB;
                }
                if (_Kind < 1.5)
                {
                    return _ColorA;
                }
                if (_Kind < 2.5)
                {
                    return lerp(_ColorB, _ColorA, uv.y);
                }
                float2 imageUv = uv;
                if (_Aspect > _ImageAspect)
                {
                    imageUv.y = 0.5 + (uv.y - 0.5) * (_ImageAspect / _Aspect);
                }
                else
                {
                    imageUv.x = 0.5 + (uv.x - 0.5) * (_Aspect / _ImageAspect);
                }
                return half4(SAMPLE_TEXTURE2D(_ImageTex, sampler_ImageTex, imageUv).rgb, 1.0h);
            }
            ENDHLSL
        }
    }
}

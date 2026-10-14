// Human Echo - the PERSON layer: the person field (picture in rgb, person mask in alpha) drawn as a true-colour cutout on ONE quad that
// is fitted inside the screen ("contain"), so the quad's uv IS the picture uv (v = 1 at the top, the field's own orientation).
//
//   _Mode 0 Picture    the camera's own colours
//   _Mode 1 Grayscale  a grey / white figure
//   _Mode 2 Flat       one flat colour wherever the mask says person
// All modes are multiplied by _Tint and _Brightness, then given _Contrast, then pushed toward white-hot by _Flash. The contrast pivots
// around the middle of the PERCEPTUAL (gamma) range, so it behaves the same in a linear and in a gamma colour-space project. _Flash
// goes above 1 on purpose: the colour buffer is HDR, so Bloom flares a flashing figure.
//
// _EdgeContrast hardens the soft mask edge around its 0.5 value. The optional rim is a thin coloured light on the OUTER side of the
// edge: the coverage of the mask inside a small disk (12 taps on a spiral, turned per pixel by interleaved gradient noise) is 0.5 on
// the edge and falls to 0 away from it; it only shows where the body itself is absent.
//
// Output is premultiplied (Blend One OneMinusSrcAlpha). All samples use an explicit level 0.
Shader "HumanEcho/Person"
{
    Properties
    {
        _MainTex ("Person field (picture rgb, mask alpha)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "Person"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define RIM_TAPS 12

            sampler2D _MainTex;

            float _Mode;             // 0 picture, 1 grayscale, 2 flat
            float4 _Tint;            // linear
            float _Opacity;
            float _Brightness;
            float _Contrast;
            float _Flash;
            float _EdgeContrast;
            float _RimOn;            // 1 = draw the rim
            float4 _RimColor;        // linear
            float4 _RimRadiusUv;     // xy: rim radius in picture uv
            float _RimStrength;      // HDR multiplier of the rim colour

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Harden(float mask)
            {
                return saturate((mask - 0.5) * _EdgeContrast + 0.5);
            }

            // The mask at a picture uv; 0 outside the picture, so the rim never grows from the picture's own border.
            float MaskAt(float2 uv)
            {
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return tex2Dlod(_MainTex, float4(uv, 0.0, 0.0)).a * inside;
            }

            float3 ToPerceptual(float3 color)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return color;
            #else
                return LinearToGammaSpace(color);
            #endif
            }

            float3 FromPerceptual(float3 color)
            {
            #ifdef UNITY_COLORSPACE_GAMMA
                return color;
            #else
                return GammaToLinearSpace(color);
            #endif
            }

            float InterleavedGradientNoise(float2 pixelPos)
            {
                return frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715))));
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 field = tex2D(_MainTex, i.uv);
                float mask = Harden(field.a);

                float3 rgb = field.rgb;
                if (_Mode > 1.5)
                {
                    rgb = float3(1.0, 1.0, 1.0);
                }
                else if (_Mode > 0.5)
                {
                    float grey = dot(rgb, float3(0.299, 0.587, 0.114));
                    rgb = float3(grey, grey, grey);
                }
                rgb = rgb * _Tint.rgb * _Brightness;

                if (abs(_Contrast - 1.0) > 0.001)
                {
                    float3 perceptual = ToPerceptual(rgb);
                    perceptual = max((perceptual - 0.5) * _Contrast + 0.5, 0.0);
                    rgb = FromPerceptual(perceptual);
                }

                // White-hot: first toward white, then brighter than white (HDR) so the figure blooms.
                rgb = lerp(rgb, float3(1.0, 1.0, 1.0), saturate(_Flash)) * (1.0 + _Flash);

                float body = mask * _Opacity;

                float rim = 0.0;
                if (_RimOn > 0.5)
                {
                    float turn = 6.2831853 * InterleavedGradientNoise(i.vertex.xy);
                    float sum = 0.0;
                    [unroll]
                    for (int t = 0; t < RIM_TAPS; t++)
                    {
                        float radius = sqrt((t + 0.5) / RIM_TAPS);
                        float angle = t * 2.39996323 + turn;
                        sum += Harden(MaskAt(i.uv + _RimRadiusUv.xy * radius * float2(cos(angle), sin(angle))));
                    }
                    float profile = saturate(2.0 * sum / RIM_TAPS);
                    rim = profile * profile * (1.0 - mask);
                }

                // Premultiplied: the body, then the rim light where the body is absent.
                float rimAlpha = saturate(rim);
                float3 rimRgb = _RimColor.rgb * _RimStrength * rimAlpha;
                float3 outRgb = rgb * body + rimRgb * (1.0 - body);
                float outAlpha = body + rimAlpha * (1.0 - body);
                return float4(outRgb, outAlpha);
            }
            ENDHLSL
        }
    }
}

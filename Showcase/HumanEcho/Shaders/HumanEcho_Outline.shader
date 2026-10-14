// Human Echo - the NEON OUTLINE: a thin, bright line along the edge of the person mask, drawn on ONE full-screen quad.
//
// Where the line is. The mask edge is soft (a few mask pixels wide) and flickers about 2 percent from frame to frame, so a hard
// threshold would shimmer. Instead the line follows the 0.5 iso-line of a slightly blurred mask, and its WIDTH comes from the distance
// to that iso-line: distance in screen pixels = (mask - 0.5) / (the mask's gradient per screen pixel). The gradient is taken from the
// five taps that are needed anyway (centre and four neighbours, one mask texel apart). That makes the line the same width in screen
// pixels whether the edge is soft or sharp, and at any mask resolution; a 2 percent flicker moves it by a fraction of a pixel.
//
// The mask is read from the live person field (_FieldTex, alpha) or, for a delayed outline, from one slice of the echo ring (_RingTex,
// a Texture2DArray). The ring is declared with UNITY_DECLARE_TEX2DARRAY from UnityCG.cginc (not the SRP core macros) for the reason
// given in HumanEcho_Echo.shader: one include family per shader file; "#pragma require 2darray" fails the compile instead of
// misbehaving on a device without texture arrays. All samples use an explicit level 0.
//
// Halo: a faint wider glow, added around the line. It is the coverage of the mask inside a disk of the halo's radius (12 taps on a
// spiral, turned per pixel by interleaved gradient noise so there is grain instead of banding): coverage is 0.5 on the edge and falls to
// 0 or 1 away from it, so "1 - |2 coverage - 1|" is a bump centred on the edge, on both sides of it, whatever the mask looks like.
//
// Electric jitter: the whole sampling position is displaced by a value noise of the screen position (linear interpolation between
// hashed lattice points, so the line zigzags instead of waving). The lattice is re-hashed in discrete time steps, so the line crackles.
//
// Output is premultiplied. The line replaces what is under it (alpha = line), the halo is added (alpha 0), both in HDR so Bloom flares them.
Shader "HumanEcho/Outline"
{
    Properties
    {
        _FieldTex ("Person field (live mask in alpha)", 2D) = "black" {}
        _RingTex ("Mask ring (delayed mask)", 2DArray) = "" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "Outline"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define HALO_TAPS 12

            sampler2D _FieldTex;
            UNITY_DECLARE_TEX2DARRAY(_RingTex);

            float _UseRing;          // 1 = read the ring slice, 0 = read the live field
            float _RingSlice;        // slice of the delayed mask
            float4 _PicScale;        // xy: screen width / picture width, screen height / picture height (world units)
            float _Aspect;           // screen width / screen height
            float4 _MaskTexel;       // 1 / width, 1 / height, width, height of the mask
            float4 _Color;           // linear rgb of the line
            float _Intensity;        // HDR multiplier of the line
            float _Width;            // line width in screen heights
            float _Softness;         // 0 = crisp, 1 = a wide soft falloff
            float _HaloWidth;        // halo radius in screen heights (0 = no halo)
            float _HaloStrength;     // halo brightness relative to the line
            float _JitterAmount;     // jitter distance in screen heights (0 = a clean line)
            float _JitterFrequency;  // jitter lattice points per screen height
            float _JitterStep;       // integer-valued time step, advanced on the CPU

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

            float Inside(float2 uv)
            {
                return step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            }

            // The mask at a picture uv. The samplers clamp, so beyond the picture's border this is the border's own value: a body that the
            // camera frame cuts off has no edge along the frame. The result is cut to the picture at the very end (see frag).
            float MaskAt(float2 picUv)
            {
                float mask;
                if (_UseRing > 0.5)
                {
                    mask = UNITY_SAMPLE_TEX2DARRAY_LOD(_RingTex, float3(picUv, _RingSlice), 0).r;
                }
                else
                {
                    mask = tex2Dlod(_FieldTex, float4(picUv, 0.0, 0.0)).a;
                }
                return mask;
            }

            // Hash without sine (Dave Hoskins): three numbers in, two numbers in 0..1 out.
            float2 Hash23(float3 p3)
            {
                p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Value noise that is LINEAR between lattice points (kinks, not curves: that is what makes the line zigzag).
            float2 LatticeNoise(float2 p, float timeStep)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 n00 = Hash23(float3(cell, timeStep));
                float2 n10 = Hash23(float3(cell + float2(1.0, 0.0), timeStep));
                float2 n01 = Hash23(float3(cell + float2(0.0, 1.0), timeStep));
                float2 n11 = Hash23(float3(cell + float2(1.0, 1.0), timeStep));
                return lerp(lerp(n00, n10, f.x), lerp(n01, n11, f.x), f.y);
            }

            // How far the sampling position is displaced at a screen position, in screen heights. Two octaves, the second smaller.
            float2 JitterOffset(float2 screenUv)
            {
                float2 p = (screenUv - 0.5) * float2(_Aspect, 1.0) * _JitterFrequency;
                float2 n = LatticeNoise(p, _JitterStep) * 0.65 + LatticeNoise(p * 2.37 + 17.3, _JitterStep + 31.0) * 0.35;
                return (n * 2.0 - 1.0) * _JitterAmount;
            }

            float InterleavedGradientNoise(float2 pixelPos)
            {
                return frac(52.9829189 * frac(dot(pixelPos, float2(0.06711056, 0.00583715))));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 shake = float2(0.0, 0.0);
                if (_JitterAmount > 0.0)
                {
                    shake = JitterOffset(i.uv);
                }
                float2 screenUv = i.uv + shake / float2(_Aspect, 1.0);
                float2 picUv = (screenUv - 0.5) * _PicScale.xy + 0.5;
                // The picture's frame is taken from the UNSHAKEN position, so the clip edge itself does not wobble.
                float2 framePicUv = (i.uv - 0.5) * _PicScale.xy + 0.5;

                // Five taps: the centre and the four neighbours one mask texel away.
                float2 texel = _MaskTexel.xy;
                float centre = MaskAt(picUv);
                float east = MaskAt(picUv + float2(texel.x, 0.0));
                float west = MaskAt(picUv - float2(texel.x, 0.0));
                float north = MaskAt(picUv + float2(0.0, texel.y));
                float south = MaskAt(picUv - float2(0.0, texel.y));
                float smoothMask = (2.0 * centre + east + west + north + south) / 6.0;

                // Mask change per screen pixel: central differences per texel, times mask texels per screen pixel.
                float2 perTexel = float2(east - west, north - south) * 0.5;
                float2 texelsPerPixel = _MaskTexel.zw * _PicScale.xy / _ScreenParams.xy;
                float slope = max(length(perTexel * texelsPerPixel), 1e-4);
                float distancePx = (smoothMask - 0.5) / slope;

                // The line: fully on within the inner part of the half width, fading to nothing a little beyond it. The extra half
                // pixel is the anti-aliasing minimum, so even the thinnest line is not a stair.
                float halfWidthPx = 0.5 * _Width * _ScreenParams.y;
                float edge0 = halfWidthPx * (1.0 - _Softness);
                float edge1 = halfWidthPx * (1.0 + _Softness) + 0.5;
                float core = 1.0 - smoothstep(edge0, edge1, abs(distancePx));

                float haloShape = 0.0;
                if (_HaloWidth > 0.0 && _HaloStrength > 0.0)
                {
                    float2 radiusUv = _HaloWidth * float2(1.0 / _Aspect, 1.0) * _PicScale.xy;
                    float turn = 6.2831853 * InterleavedGradientNoise(i.vertex.xy);
                    float sum = centre;
                    [unroll]
                    for (int t = 0; t < HALO_TAPS; t++)
                    {
                        float radius = sqrt((t + 0.5) / HALO_TAPS);
                        float angle = t * 2.39996323 + turn;
                        sum += MaskAt(picUv + radiusUv * radius * float2(cos(angle), sin(angle)));
                    }
                    float coverage = sum / (HALO_TAPS + 1.0);
                    float bump = 1.0 - abs(2.0 * coverage - 1.0);
                    haloShape = bump * bump;
                }

                float glow = _HaloStrength * haloShape;
                float3 color = _Color.rgb * _Intensity;
                float3 rgb = color * (core + glow * (1.0 - core));
                float frame = Inside(framePicUv);
                return float4(rgb * frame, core * frame);
            }
            ENDHLSL
        }
    }
}

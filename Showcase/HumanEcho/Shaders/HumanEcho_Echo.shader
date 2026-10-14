// Human Echo - the ECHO layer's shader. One file, two jobs:
//
//   pass 0 Capture   (Graphics.Blit only) copies the alpha of the person field into ONE slice of the ring, a Texture2DArray whose
//                    slices are the last N person masks. Its LightMode is a name URP never draws, because URP draws EVERY untagged pass
//                    of a shader on a mesh renderer: without the tag this pass would also run on the echo quad.
//   pass 1 Copies    (the echo quad, one full-screen quad) draws up to 16 copies of the mask in ONE pass. Per copy it reads the copy's
//                    own slice, so there is no per-copy draw call. The copies are composited oldest to newest with premultiplied alpha
//                    (Blend One OneMinusSrcAlpha), so the newest copy is on top.
//
// Texture arrays: the ring is declared with UNITY_DECLARE_TEX2DARRAY and read with UNITY_SAMPLE_TEX2DARRAY_LOD from UnityCG.cginc's
// HLSLSupport (not TEXTURE2D_ARRAY from the SRP core library). Reason: this shader needs vert_img for the blit pass, and vert_img lives
// in UnityCG.cginc, which cannot be combined with the SRP Core.hlsl in one pass (both define the same macros). The other Human Echo
// shaders use the same include family. "#pragma require 2darray" (and target 3.5) makes the shader fail to compile, instead of
// misbehaving, on a device without texture arrays. Samples use explicit level 0 so no sample ever depends on screen derivatives, which
// keeps the loops free of gradient-in-flow-control problems.
//
// Space. Everything is evaluated in SCREEN uv (0..1 over the whole screen, v = 1 at the top, u counted in screen WIDTHS). The picture is
// "contain" fitted inside the screen: picUv = (screenUv - 0.5) * _PicScale + 0.5 with _PicScale = (screenW / pictureW, screenH /
// pictureH) in world units, so outside 0..1 there is no picture and no mask. A copy is moved by sampling the mask at the position that
// ends up here after the move: undo the wave, undo the shift, undo the scale (about the screen centre), then map to picUv.
Shader "HumanEcho/Echo"
{
    Properties
    {
        // Graphics.Blit binds its source as _MainTex only when the shader declares the property.
        _MainTex ("Blit source (person field)", 2D) = "black" {}
        _EchoRing ("Mask ring (one slice per capture)", 2DArray) = "" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Capture"
            Tags { "LightMode" = "EchoCapture" }
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float4 frag(v2f_img i) : SV_Target
            {
                // The field's alpha is the person mask; the ring keeps it in the red channel of an R8 slice.
                float mask = tex2D(_MainTex, i.uv).a;
                return float4(mask, mask, mask, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Copies"
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_COPIES 16

            UNITY_DECLARE_TEX2DARRAY(_EchoRing);

            float4 _PicScale;        // xy: screen width / picture width, screen height / picture height (world units)
            float _Aspect;           // screen width / screen height
            float _CopyCount;        // copies to draw this frame, 1 .. MAX_COPIES
            float _Look;             // 0 solid, 1 stipple, 2 outline
            float _DotsPerHeight;    // stipple cells across the screen HEIGHT, so the dots stay round
            float _DotSoftness;      // stipple: soft part of the dot's radius, 0.02 .. 0.5
            float _EdgeContrast;     // 1 = the mask as sent, more = a harder edge
            float _OutlineWidth;     // outline look: line width in screen heights
            float _WaveAmount;       // ribbon shift per unit of age, screen heights
            float _WaveFrequency;    // ribbon waves, radians per screen height
            float _WavePhase;        // radians, advanced on the CPU
            float4 _CopyColor[MAX_COPIES];   // rgb: colour (linear, times gain), a: opacity
            float4 _CopyXform[MAX_COPIES];   // xy: shift in screen uv, z: scale about the screen centre
            float4 _CopyParam[MAX_COPIES];   // x: ring slice, y: age (1 = newest copy), zw: stipple grid shift in cells

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

            // Where copy c's picture is for a screen position: undo the ribbon, the shift and the scale of the copy, then map into the picture.
            float2 PictureUv(float2 screenUv, int c)
            {
                float4 xform = _CopyXform[c];
                float4 param = _CopyParam[c];
                float2 p = screenUv - 0.5;

                // The ribbon: older copies are pushed sideways by a sine of the height, the push growing with the age.
                float wave = sin(_WaveFrequency * p.y + _WavePhase + param.y * 0.9) * _WaveAmount * param.y;
                p.x -= wave / _Aspect;
                p -= xform.xy;
                p /= xform.z;
                return p * _PicScale.xy + 0.5;
            }

            // The mask of copy c at a picture uv. The ring's sampler clamps, so beyond the picture's border this is the border's own value.
            float MaskInPicture(float2 picUv, int c)
            {
                return UNITY_SAMPLE_TEX2DARRAY_LOD(_EchoRing, float3(picUv, _CopyParam[c].x), 0).r;
            }

            // The mask of copy c at a screen position; 0 outside the picture (the picture's frame is where the camera view ends).
            float MaskAt(float2 screenUv, int c)
            {
                float2 picUv = PictureUv(screenUv, c);
                return MaskInPicture(picUv, c) * Inside(picUv);
            }

            // Contrast around the middle of the mask's edge: 1 leaves it alone, more hardens the soft edge.
            float Harden(float mask)
            {
                return saturate((mask - 0.5) * _EdgeContrast + 0.5);
            }

            // Round dots on a screen-space grid; a dot's radius follows the mask coverage at the dot's centre, so the silhouette is a
            // field of dots that swell and shrink at its edge. Cells are counted per screen HEIGHT in both axes, so the dots are round.
            float StippleShape(float2 screenUv, int c)
            {
                float2 shift = _CopyParam[c].zw;
                float2 grid = float2(screenUv.x * _Aspect, screenUv.y) * _DotsPerHeight + shift;
                float2 cell = floor(grid);
                float2 inCell = grid - cell - 0.5;
                float2 centerUv = ((cell + 0.5 - shift) / _DotsPerHeight) / float2(_Aspect, 1.0);

                float coverage = Harden(MaskAt(centerUv, c));
                float radius = 0.62 * coverage;
                float edge1 = max(radius, 1e-4);
                float edge0 = radius * (1.0 - _DotSoftness);
                float dotShape = 1.0 - smoothstep(edge0, edge1, length(inCell));
                return dotShape * step(0.03, coverage);
            }

            // A thin line along the copy's mask edge. The distance to the 0.5 iso-line, in screen pixels, is (mask - 0.5) divided by
            // the mask's gradient per pixel (forward differences, so no derivative instructions): the width is the same in pixels
            // wherever the mask edge is soft or sharp. The mask is read clamped and the result is cut to the picture afterwards, so a
            // body that the camera frame cuts off does not get a line along the frame.
            float OutlineShape(float2 screenUv, int c)
            {
                float2 pixelUv = _ScreenParams.zw - 1.0;
                float2 uv0 = PictureUv(screenUv, c);
                float m0 = MaskInPicture(uv0, c);
                float mx = MaskInPicture(PictureUv(screenUv + float2(pixelUv.x, 0.0), c), c);
                float my = MaskInPicture(PictureUv(screenUv + float2(0.0, pixelUv.y), c), c);
                float slope = max(length(float2(mx - m0, my - m0)), 1e-3);
                float distancePx = (m0 - 0.5) / slope;
                float halfWidthPx = 0.5 * _OutlineWidth * _ScreenParams.y;
                return saturate(halfWidthPx + 0.5 - abs(distancePx)) * Inside(uv0);
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 accumulated = float4(0.0, 0.0, 0.0, 0.0);
                int count = (int)(_CopyCount + 0.5);

                [loop]
                for (int c = 0; c < MAX_COPIES; c++)
                {
                    if (c >= count)
                    {
                        break;
                    }
                    float4 colorOpacity = _CopyColor[c];
                    float shape;
                    if (_Look < 0.5)
                    {
                        shape = Harden(MaskAt(i.uv, c));
                    }
                    else if (_Look < 1.5)
                    {
                        shape = StippleShape(i.uv, c);
                    }
                    else
                    {
                        shape = OutlineShape(i.uv, c);
                    }

                    // Premultiplied "over": this copy on top of everything older that is already accumulated.
                    float alpha = saturate(shape * colorOpacity.a);
                    float4 contribution = float4(colorOpacity.rgb * alpha, alpha);
                    accumulated = contribution + accumulated * (1.0 - contribution.a);
                }
                return accumulated;
            }
            ENDHLSL
        }
    }
}

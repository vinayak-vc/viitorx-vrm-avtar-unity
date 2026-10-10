// Procedural chromatic rain on one full-screen quad. Four depth layers of hashed columns; each
// column carries streaks that fall continuously (frac of a scrolled coordinate, so there is no
// reset and no reshuffle), with per-streak colour, speed, length and width. Long streaks and short
// droplets are mixed. Rain is suppressed wherever the silhouette mask is set.
// Additive blend: light on near-black.
Shader "ChromaticRain/Rain"
{
    Properties
    {
        _Density ("Density", Range(0, 1)) = 0.6
        _StreakLength ("Streak length (screen heights)", Range(0.02, 1)) = 0.3
        _StreakWidth ("Streak width", Range(0.05, 1)) = 0.38
        _Intensity ("Intensity", Range(0, 3)) = 1
        _Wind ("Wind", Range(0, 0.3)) = 0.06
        _AmberChance ("Amber chance", Range(0, 0.2)) = 0.05
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend One One

        Pass
        {
            Name "Rain"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define LAYERS 4
            // The fall is driven by a phase that wraps at exactly 1 and is built so the pattern is
            // IDENTICAL at the wrap: every column's fall is a whole number of its own periods per
            // WRAP_SECONDS. Nothing grows without bound, so there is no precision loss after hours
            // and no visible reset. REF_FALL is the speed those whole numbers are rounded for; the
            // fall-speed setting only changes how fast the phase advances.
            #define WRAP_SECONDS 1200.0
            #define REF_FALL 0.85

            float _Density;
            float _StreakLength;
            float _StreakWidth;
            float _Intensity;
            float _Wind;
            float _AmberChance;
            float _Phase;       // 0..1, wraps; advanced on the CPU
            float _Aspect;      // width / height of the screen
            float4 _Palette[5]; // cyan, blue, violet, magenta, amber
            sampler2D _ChromaMaskTex;
            float _MaskInfluence;

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

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 PickColor(float h, float h2, float h3)
            {
                float3 c;
                if (h < _AmberChance)
                {
                    c = _Palette[4].rgb;
                }
                else
                {
                    float f = h2 * 3.999;
                    int i0 = (int)floor(f);
                    int i1 = (i0 >= 3) ? 0 : (i0 + 1);
                    c = lerp(_Palette[i0].rgb, _Palette[i1].rgb, h3 * 0.55);
                }
                return c;
            }

            float3 RainLayer(float2 P, int layer)
            {
                float depth = layer / (float)(LAYERS - 1);          // 0 far .. 1 near
                float cols = lerp(150.0, 46.0, depth);              // columns per screen height
                float speedMul = lerp(0.55, 1.35, depth);
                float lenMul = lerp(0.45, 1.0, depth);
                float bright = lerp(0.32, 1.0, depth);
                float widthMul = lerp(0.50, 1.0, depth);
                float seed = layer * 17.31;

                // Subtle wind: a slant that sways slowly. Phase-driven, so it never snaps.
                // Whole numbers of cycles per wrap, so the sway is continuous across it too.
                float sway = sin(6.2831853 * (_Phase * 40.0 + layer * 0.27))
                           + 0.5 * sin(6.2831853 * (_Phase * 97.0 + layer * 0.13));
                float slant = _Wind * (0.6 + depth) * sway;

                float x = (P.x + slant * P.y) * cols;
                float cell = floor(x);
                float fx = frac(x);

                float h1 = hash21(float2(cell, seed + 1.0));
                float h2 = hash21(float2(cell + 11.7, seed + 2.0));
                float h3 = hash21(float2(cell + 3.3, seed + 3.0));
                float h4 = hash21(float2(cell + 29.1, seed + 4.0));
                float h5 = hash21(float2(cell + 41.9, seed + 5.0));
                float h6 = hash21(float2(cell + 57.3, seed + 6.0));

                float colActive = step(h1, _Density);

                float period = lerp(0.85, 2.2, h3);
                float cycles = max(1.0, round(REF_FALL * speedMul * lerp(0.7, 1.3, h2) * WRAP_SECONDS / period));

                // Mostly long streaks, some short droplets.
                float isDrop = step(h4, 0.30);
                float len = lerp(_StreakLength * lenMul * lerp(0.35, 1.0, h4),
                                 lerp(0.012, 0.05, h6) * (0.5 + depth), isDrop);
                len = max(len, 0.01);

                float pos = P.y / period + _Phase * cycles + h5;
                float s = frac(pos) * period;          // distance above the bottom end of the streak
                float tail = saturate(1.0 - s / len);
                tail *= tail;
                float soft = min(len * 0.5, 0.012);
                float headSoft = smoothstep(0.0, soft, s);
                float glow = 1.0 + 1.6 * exp(-s * 55.0);
                float along = tail * headSoft * glow;

                float cx = 0.5 + (h2 - 0.5) * 0.30;
                float w = max(_StreakWidth * 0.5 * widthMul * lerp(0.7, 1.25, h3), fwidth(x) * 0.75);
                float dx = abs(fx - cx);
                float core = smoothstep(w, w * 0.35, dx);
                float halo = smoothstep(w * 2.6, 0.0, dx) * 0.22;

                float3 color = PickColor(h5, h6, h2);
                return color * (core + halo) * along * bright * colActive;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 P = float2(i.uv.x * _Aspect, i.uv.y);
                float3 col = 0;
                for (int l = 0; l < LAYERS; l++)
                {
                    col += RainLayer(P, l);
                }
                col *= _Intensity;
                // Soft clip: bright where streaks overlap, never a blown-out sheet.
                col = 1.0 - exp(-col * 1.2);

                float m = tex2D(_ChromaMaskTex, i.uv).r * _MaskInfluence;
                col *= (1.0 - saturate(m));
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}

// Showcase renderer for a FluXY container. It reads FluXY's own dye buffer (_MainTex, set by the container
// through its property block) and velocity buffer (_Velocity) through FluXY's public include and its
// helpers (TileToUV, tex2D_flowmap, SquareFalloff), so FluXY's files stay untouched. It draws the whole
// screen: a deep midnight background, then the dye as a luminous liquid sheet.
//
// _Enhance = 0 draws the plain dye (soft-clipped) and nothing else, so the simulation can be checked
// without any of the extras. _Enhance = 1 adds, all from the SAME two FluXY textures:
//   - layers: the dye thickness is read through a gradient ramp and faint iso-contours, so a smooth
//     density field shows as stacked translucent layers of liquid;
//   - surface detail: a noise texture advected by the FluXY velocity ripples those layers, so the
//     surface moves with the flow and distorts around a disturbance;
//   - relief: the thickness gradient gives crests a surface light and a thin specular line;
//   - streaks and a chromatic split along the flow, only where the fluid moves fast;
//   - motion glow: fast-moving dye brightens toward the accent colour.
Shader "FluidDisruption/Fluid"
{
    Properties
    {
        _Detail ("Surface detail (noise)", 2D) = "gray" {}
        _Ramp ("Thickness ramp", 2D) = "white" {}
        _Gain ("Dye gain", Range(0, 8)) = 2.4
        _Crest ("Crest strength", Range(0, 3)) = 0.7
        _Relief ("Relief", Range(0, 40)) = 8
        _Chroma ("Chromatic split", Range(0, 0.02)) = 0.006
        _StreakLength ("Streak length", Range(0, 0.2)) = 0.05
        _MotionGlow ("Motion glow", Range(0, 2)) = 0.6
        _Vignette ("Vignette", Range(0, 1)) = 0.35
        _DetailAmount ("Detail amount", Range(0, 1)) = 0.35
        _DetailScale ("Detail scale", Range(0.2, 8)) = 2.2
        _DetailAdvection ("Detail advection", Range(0, 4)) = 1.2
        _Layers ("Layer contours", Range(0, 24)) = 7
        _LayerDepth ("Layer depth", Range(0, 1)) = 0.28
        _RampGlow ("Ramp glow", Range(0, 2)) = 0.5
        _Enhance ("Enhancements (0/1)", Float) = 1
        _BgLow ("Background low", Color) = (0.01, 0.02, 0.075, 1)
        _BgHigh ("Background high", Color) = (0.018, 0.045, 0.14, 1)
        [HDR] _Accent ("Accent", Color) = (0.74, 0.64, 1, 1)
        _DyeTexel ("Dye texel (uv)", Float) = 0.002
        _Overscan ("Fluid area / screen", Float) = 1
        _Aspect ("Screen aspect", Float) = 1.78
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend Off

        Pass
        {
            Name "Fluid"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Assets/FluXY/Resources/Shaders/FluidUtils.hlsl"

            sampler2D _MainTex;      // FluXY dye buffer: premultiplied colour in rgb, thickness in a
            sampler2D _Detail;
            sampler2D _Ramp;
            float _Gain;
            float _Crest;
            float _Relief;
            float _Chroma;
            float _StreakLength;
            float _MotionGlow;
            float _Vignette;
            float _DetailAmount;
            float _DetailScale;
            float _DetailAdvection;
            float _Layers;
            float _LayerDepth;
            float _RampGlow;
            float _Enhance;
            float4 _BgLow;
            float4 _BgHigh;
            float4 _Accent;
            float _DyeTexel;
            float _Overscan;
            float _Aspect;

            // How much dye there is. The buffer's alpha is no use for this (it accumulates as alpha squared and
            // stays tiny), so the thickness is the strongest colour channel of the premultiplied dye.
            float Amount(float3 dye)
            {
                return max(dye.r, max(dye.g, dye.b));
            }

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

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = TileToUV(i.uv, _TileIndex);
                float2 vel = tex2D(_Velocity, uv).xy;
                float speed = length(vel);
                float2 dir = speed > 1e-4 ? vel / speed : float2(0, 0);

                float4 centre = tex2D(_MainTex, uv);
                float3 dye = centre.rgb;
                float shade = 1.0;
                float3 extra = 0;

                if (_Enhance > 0.5)
                {
                    // Streaks: the dye read back along the flow.
                    float reach = min(speed * _StreakLength, 0.08);
                    float4 sA = tex2D(_MainTex, uv - dir * reach * 0.5);
                    float4 sB = tex2D(_MainTex, uv - dir * reach);
                    float4 stretched = centre * 0.4 + sA * 0.3 + sB * 0.3;

                    // Chromatic split, only where the fluid really moves.
                    float split = _Chroma * smoothstep(0.08, 0.9, speed);
                    float3 d;
                    d.r = lerp(stretched.r, tex2D(_MainTex, uv + dir * split).r, 0.5);
                    d.g = stretched.g;
                    d.b = lerp(stretched.b, tex2D(_MainTex, uv - dir * split).b, 0.5);
                    dye = d;

                    // Surface detail: noise carried by the flow ripples the thickness.
                    float2 duv = i.uv * float2(_Aspect, 1.0) * _DetailScale;
                    float det = tex2D_flowmap(_Detail, duv, vel * 0.5, _DetailAdvection).r;
                    float h = Amount(stretched.rgb) * (1.0 - _DetailAmount + _DetailAmount * 2.0 * det);

                    // Layers: the thickness through the ramp, plus faint iso-contours.
                    float t = saturate(h * 2.4);
                    float3 ramp = tex2D(_Ramp, float2(t, 0.5)).rgb;
                    float contour = 0.5 + 0.5 * cos(h * _Layers * 6.2831853);
                    shade = (1.0 - _LayerDepth) + _LayerDepth * 2.0 * contour;
                    float thick = saturate(Amount(centre.rgb) * 4.0);
                    extra += ramp * smoothstep(0.25, 0.9, t) * _RampGlow * thick;

                    // Relief: a surface from the thickness gradient.
                    float tx = _DyeTexel * 1.5;
                    float hL = Amount(tex2D(_MainTex, uv + float2(-tx, 0)).rgb);
                    float hR = Amount(tex2D(_MainTex, uv + float2(tx, 0)).rgb);
                    float hB = Amount(tex2D(_MainTex, uv + float2(0, -tx)).rgb);
                    float hT = Amount(tex2D(_MainTex, uv + float2(0, tx)).rgb);
                    float2 grad = float2(hR - hL, hT - hB) * 0.5 / max(tx, 1e-5);
                    float3 n = normalize(float3(-grad * _Relief * 0.02, 1.0));
                    float3 L = normalize(float3(-0.45, 0.55, 0.7));
                    shade *= clamp(dot(n, L) / L.z, 0.5, 1.6);
                    float3 H = normalize(L + float3(0, 0, 1));
                    float sp = pow(saturate(dot(n, H)), 70.0);
                    extra += _Accent.rgb * sp * _Crest * thick;

                    // Fast fluid brightens toward the accent.
                    float moving = smoothstep(0.12, 1.4, speed);
                    extra += lerp(dye, _Accent.rgb * thick, 0.45) * moving * _MotionGlow;
                }

                float3 lit = dye * _Gain * shade + extra;

                // Soft edge so the sheet fades at the border instead of being cut off.
                float edge = SquareFalloff(i.uv, 0.06);
                float2 q = (i.uv - 0.5) * _Overscan;
                float vig = 1.0 - _Vignette * saturate(dot(q, q) * 2.2);
                lit *= edge;

                float3 bg = lerp(_BgLow.rgb, _BgHigh.rgb, i.uv.y) * vig;
                float3 col = 1.0 - (1.0 - bg) * exp(-lit * vig);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}

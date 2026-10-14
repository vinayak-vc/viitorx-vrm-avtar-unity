// Human Echo - draws a FluXY container as coloured smoke. It reads FluXY's own dye (_MainTex) and velocity (_Velocity) through FluXY's public
// include (the container's property block supplies _TileIndex, _TileData, _TileID, _MainTex and _Velocity), so FluXY's files stay untouched.
//
// The dye buffer holds colour in rgb (already weighted by how much dye there is). Its alpha accumulates as alpha squared (ADR-091), so the
// thickness is read from the rgb, never from alpha.
//   _Look 0  ink    smooth, soft-clipped colour
//   _Look 1  dots   the same dye drawn as a field of round dots whose radius follows the thickness (the dotted smoke of the reference clips);
//                   the dot grid is advected by the flow so the dots travel with the smoke instead of staying on the screen
Shader "HumanEcho/Smoke"
{
    Properties
    {
        _Gain ("Dye gain", Range(0, 8)) = 2.0
        _Opacity ("Opacity", Range(0, 1)) = 1.0
        [Enum(Ink,0,Dots,1)] _Look ("Look", Float) = 0
        _DotSize ("Dot grid size (dots per screen height)", Range(40, 400)) = 160
        _DotSoftness ("Dot edge softness", Range(0.02, 0.5)) = 0.18
        _DotFlow ("Dot grid follows the flow", Range(0, 1)) = 0.6
        _Aspect ("Aspect", Float) = 1.7778
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "Smoke"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Assets/FluXY/Resources/Shaders/FluidUtils.hlsl"

            sampler2D _MainTex;
            float _Gain;
            float _Opacity;
            float _Look;
            float _DotSize;
            float _DotSoftness;
            float _DotFlow;
            float _Aspect;

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

            static const float DotCycleSeconds = 1.5;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // One lattice of round dots, drifted along the flow by `phase` (0..1) of a cycle. A dot's radius follows the dye thickness.
            float DotLattice(float2 screenUv, float2 vel, float phase, float thickness)
            {
                float2 grid = screenUv * float2(_Aspect, 1.0) * _DotSize;
                grid -= vel * _DotFlow * _DotSize * 0.25 * DotCycleSeconds * phase;
                float2 cell = floor(grid);
                float2 local = frac(grid) - 0.5;
                float rnd = Hash21(cell);
                float radius = saturate(thickness) * 0.62 * (0.7 + 0.3 * rnd);
                float d = length(local);
                float dotShape = 1.0 - smoothstep(radius - _DotSoftness, radius + _DotSoftness * 0.2, d);
                return saturate(dotShape * saturate(thickness * 2.0));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = TileToUV(i.uv, _TileIndex);
                float4 dyeSample = tex2D(_MainTex, uv);
                float3 dye = dyeSample.rgb * _Gain;
                float thickness = max(dye.r, max(dye.g, dye.b));

                float3 color = 1.0 - exp(-dye * 1.2);
                float alpha = saturate(1.0 - exp(-thickness * 1.2));

                if (_Look > 0.5)
                {
                    // Dots that travel with the dye. A lattice dragged by the CURRENT velocity for ever would shear more the longer the scene
                    // runs, so two lattices each drift for one cycle and are cross-faded half a cycle apart (the flow-map trick FluXY's own
                    // renderer uses): nothing ever depends on the absolute scene time, only on the position inside a cycle.
                    float2 vel = tex2D(_Velocity, uv).rg;
                    float cycle = _Time.y / DotCycleSeconds;
                    float phase1 = frac(cycle);
                    float phase2 = frac(cycle + 0.5);
                    float blend = abs((phase1 - 0.5) * 2.0);
                    alpha = lerp(DotLattice(i.uv, vel, phase1, thickness), DotLattice(i.uv, vel, phase2, thickness), blend);
                }

                return float4(color * alpha * _Opacity, alpha * _Opacity);
            }
            ENDHLSL
        }
    }
}

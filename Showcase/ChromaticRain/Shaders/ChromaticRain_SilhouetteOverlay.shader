// The Dark Silhouette: a near-black FILLED body drawn over the rain and the fluid, with an optional thin luminous
// outline along its outer edge. The fill and the outline are separate: the fill is the mask itself (the rain and the
// fluid sample the same mask, so all three agree about where the body is); the outline is only added on top and is
// switched off by _RimIntensity = 0. Disabled (renderer off) in Negative Space mode.
//
// _DebugView inspects the mask without the rain in the way:
//   0 normal composition
//   1 the filled mask, white on black
//   2 the filled mask, dim grey on black (the landmarks and bones are drawn over it by the debug overlay)
//   3 the contour only
Shader "ChromaticRain/SilhouetteOverlay"
{
    Properties
    {
        _BodyColor ("Body colour", Color) = (0.018, 0.024, 0.040, 1)
        _RimColor ("Outline colour", Color) = (0.22, 0.5, 1, 1)
        _RimIntensity ("Outline intensity", Range(0, 2)) = 0.45
        _RimWidth ("Outline width (mask pixels)", Range(0.5, 4)) = 1.2
        _DebugView ("Debug view", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+2" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Overlay"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _BodyColor;
            float4 _RimColor;
            float _RimIntensity;
            float _RimWidth;
            float _DebugView;
            sampler2D _ChromaMaskTex;
            float4 _ChromaMaskTex_TexelSize;

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
                float m = tex2D(_ChromaMaskTex, i.uv).r;

                // Blurred coverage: the difference to the mask is positive just inside the edge and negative just
                // outside it, so it traces the contour of the filled shape at the chosen width.
                float2 t = _ChromaMaskTex_TexelSize.xy * _RimWidth;
                float blur = 0.25 * (tex2D(_ChromaMaskTex, i.uv + float2(t.x, 0)).r
                                   + tex2D(_ChromaMaskTex, i.uv - float2(t.x, 0)).r
                                   + tex2D(_ChromaMaskTex, i.uv + float2(0, t.y)).r
                                   + tex2D(_ChromaMaskTex, i.uv - float2(0, t.y)).r);

                if (_DebugView > 0.5)
                {
                    if (_DebugView < 1.5)
                        return float4(m, m, m, 1);
                    if (_DebugView < 2.5)
                        return float4(m * 0.30, m * 0.30, m * 0.34, 1);
                    float edge = saturate(abs(m - blur) * 5.0);
                    return float4(edge * 0.35, edge * 0.85, edge, 1);
                }

                if (m <= 0.002)
                    discard;

                float rim = saturate((m - blur) * 3.0) * m;
                float3 color = _BodyColor.rgb + _RimColor.rgb * rim * _RimIntensity;
                return float4(color, saturate(m));
            }
            ENDHLSL
        }
    }
}

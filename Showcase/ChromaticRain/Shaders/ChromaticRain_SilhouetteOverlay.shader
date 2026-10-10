// The Dark Silhouette: a near-black body drawn over the rain and the fluid, with a faint inner rim
// so the outline stays readable. It samples the same mask the rain and the fluid use, so all three
// agree about where the body is. Disabled (renderer off) in Negative Space mode.
Shader "ChromaticRain/SilhouetteOverlay"
{
    Properties
    {
        _BodyColor ("Body colour", Color) = (0.006, 0.008, 0.014, 1)
        _RimColor ("Rim colour", Color) = (0.22, 0.5, 1, 1)
        _RimIntensity ("Rim intensity", Range(0, 2)) = 0.45
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
                if (m <= 0.002)
                    discard;

                // Blurred coverage: the difference is positive just inside the edge.
                float2 t = _ChromaMaskTex_TexelSize.xy * 1.1;
                float blur = 0.25 * (tex2D(_ChromaMaskTex, i.uv + float2(t.x, 0)).r
                                   + tex2D(_ChromaMaskTex, i.uv - float2(t.x, 0)).r
                                   + tex2D(_ChromaMaskTex, i.uv + float2(0, t.y)).r
                                   + tex2D(_ChromaMaskTex, i.uv - float2(0, t.y)).r);
                float rim = saturate((m - blur) * 3.0) * m;

                float3 color = _BodyColor.rgb + _RimColor.rgb * rim * _RimIntensity;
                return float4(color, saturate(m));
            }
            ENDHLSL
        }
    }
}

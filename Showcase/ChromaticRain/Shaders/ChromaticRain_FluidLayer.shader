// Showcase-specific renderer for a FluXY container. It reads FluXY's own density (_MainTex, set by
// the container through its property block) and velocity (_Velocity) textures through FluXY's
// public include, so FluXY's files stay untouched. Dye colour comes from the targets' splat colours;
// moving fluid is tinted by its flow direction through a hue ring; the silhouette mask removes the
// fluid inside the body. Additive, soft-clipped, so it stays a coloured wake behind the rain.
Shader "ChromaticRain/FluidLayer"
{
    Properties
    {
        _Gradient ("Flow hue ring", 2D) = "white" {}
        _Opacity ("Opacity", Range(0, 1)) = 0.55
        _Gain ("Dye gain", Range(0, 6)) = 1.6
        _FlowTint ("Flow tint", Range(0, 2)) = 0.5
        _StreakLength ("Streak length", Range(0, 0.2)) = 0.05
        _Striation ("Striation", Range(0, 1)) = 0.45
        _Aspect ("Aspect", Float) = 1.78
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Blend One One

        Pass
        {
            Name "Fluid"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Assets/FluXY/Resources/Shaders/FluidUtils.hlsl"

            sampler2D _MainTex;      // FluXY density / dye buffer (premultiplied colour in rgb)
            sampler2D _Gradient;
            sampler2D _ChromaMaskTex;
            float _Opacity;
            float _Gain;
            float _FlowTint;
            float _StreakLength;
            float _Striation;
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

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = TileToUV(i.uv, _TileIndex);
                float4 vel = tex2D(_Velocity, uv);
                float speed = length(vel.xy);
                float2 dir = speed > 1e-4 ? vel.xy / speed : float2(0, 0);

                // Stretch the dye along the flow: three taps along the velocity read as streaks.
                float reach = min(speed * _StreakLength, 0.08);
                float4 state = tex2D(_MainTex, uv) * 0.4
                             + tex2D(_MainTex, uv - dir * reach * 0.5) * 0.3
                             + tex2D(_MainTex, uv - dir * reach) * 0.3;

                float3 dye = state.rgb * _Gain;

                // Fine striations across the flow: the stripes bend with the swirl, so moving dye
                // looks combed and marbled instead of like a soft cloud.
                float2 perp = float2(-dir.y, dir.x);
                float2 p = i.uv * float2(_Aspect, 1.0);
                float stripe = 0.5 + 0.5 * sin(dot(p, perp) * 110.0 + dot(p, dir) * 9.0);
                dye *= lerp(1.0, 0.45 + 0.55 * stripe, _Striation * smoothstep(0.04, 0.4, speed));

                // Velocity-driven chroma: hue follows the flow direction, strength follows speed.
                float angle = atan2(vel.y, vel.x) * 0.15915494 + 0.5;
                float3 hue = tex2D(_Gradient, float2(frac(angle + _Time.y * 0.02), 0.5)).rgb;
                // Only where there is dye: velocity alone must never paint a haze over the screen.
                float moving = smoothstep(0.08, 0.7, speed);
                float3 flow = hue * moving * _FlowTint * saturate(state.a * 8.0);

                float3 col = dye + flow;
                col = 1.0 - exp(-col * 1.1);

                float edge = SquareFalloff(i.uv, 0.12);
                float m = tex2D(_ChromaMaskTex, i.uv).r;
                col *= edge * (1.0 - saturate(m)) * _Opacity;
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}

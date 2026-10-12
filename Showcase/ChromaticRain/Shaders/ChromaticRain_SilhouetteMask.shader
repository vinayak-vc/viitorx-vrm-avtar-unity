// Draws the tracked body as one analytic, anti-aliased coverage mask (R channel) into a small
// render texture. Shapes: tapered capsules (limbs, torso, neck, hands, feet) and one ellipse
// (head), melted together with a smooth minimum. Run with Graphics.Blit; nothing here samples
// a texture, so the edge is exact at any mask resolution.
Shader "ChromaticRain/SilhouetteMask"
{
    Properties
    {
        // Blend state, set per draw by SilhouetteMaskRenderer. The defaults (Add, One, Zero) overwrite the target,
        // which is the single-person behaviour; Max (and One, One) lets several people share ONE mask, each pixel
        // taking the strongest coverage, so two silhouettes that touch merge instead of one erasing the other.
        [HideInInspector] _BlendOp("Blend op", Float) = 0
        [HideInInspector] _SrcBlend("Src blend", Float) = 1
        [HideInInspector] _DstBlend("Dst blend", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Mask"
            BlendOp [_BlendOp]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_CAPS 64

            float4 _Caps[MAX_CAPS];     // a.xy, b.xy (view metres)
            float4 _CapRadii[MAX_CAPS]; // radius a, radius b, weight, kind (0 capsule starting a group; -1 capsule continuing it; above 0.5 a rounded trapezoid, rounding = kind - 1)
            float4 _Head;               // centre.xy, radius.xy
            float4 _HeadDirection;      // up.xy, weight, unused
            float4 _View;               // width, height (metres), edge softness, blend
            float _Fade;
            int _CapCount;

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

            // Exact distance to an isosceles trapezoid, base at a and top at b with half-widths r1 and r2, corners rounded.
            float sdTrapezoid(float2 p, float2 a, float2 b, float r1, float r2, float rounding)
            {
                float2 axis = b - a;
                float len = length(axis);
                if (len < 1e-5)
                    return 1000.0;
                float2 up = axis / len;
                float2 lateral = float2(up.y, -up.x);
                float2 q = p - (a + b) * 0.5;
                float he = max(len * 0.5 - rounding, 0.002);
                float w1 = max(r1 - rounding, 0.002);
                float w2 = max(r2 - rounding, 0.002);
                float2 l = float2(abs(dot(q, lateral)), dot(q, up));
                float2 k1 = float2(w2, he);
                float2 k2 = float2(w2 - w1, 2.0 * he);
                float2 ca = float2(l.x - min(l.x, (l.y < 0.0) ? w1 : w2), abs(l.y) - he);
                float2 cb = l - k1 + k2 * clamp(dot(k1 - l, k2) / dot(k2, k2), 0.0, 1.0);
                float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
                return s * sqrt(min(dot(ca, ca), dot(cb, cb))) - rounding;
            }

            float smin(float a, float b, float k)
            {
                float h = max(k - abs(a - b), 0.0) / max(k, 1e-5);
                return min(a, b) - h * h * k * 0.25;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _View.xy;
                float k = _View.w;
                float d = 1000.0;

                if (_HeadDirection.z > 0.001)
                {
                    float w = saturate(_HeadDirection.z);
                    float2 up = _HeadDirection.xy;
                    float2 right = float2(up.y, -up.x);
                    float2 q = p - _Head.xy;
                    float2 radii = max(_Head.zw * w, 1e-4);
                    float2 l = float2(dot(q, right), dot(q, up)) / radii;
                    d = (length(l) - 1.0) * min(radii.x, radii.y);
                }

                float dg = 1000.0;
                bool grouped = false;
                for (int c = 0; c < _CapCount; c++)
                {
                    float w = _CapRadii[c].z;
                    if (w <= 0.001)
                        continue;
                    float2 a = _Caps[c].xy;
                    float2 b = _Caps[c].zw;
                    float dc;
                    if (_CapRadii[c].w > 0.5)
                    {
                        dc = sdTrapezoid(p, a, b, _CapRadii[c].x * w, _CapRadii[c].y * w, _CapRadii[c].w - 1.0);
                    }
                    else
                    {
                        float2 pa = p - a;
                        float2 ba = b - a;
                        float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                        float r = lerp(_CapRadii[c].x, _CapRadii[c].y, h) * w;
                        dc = length(pa - ba * h) - r;
                    }
                    // The pieces of one limb are united hard so its outline is clean; groups melt into each other.
                    if (_CapRadii[c].w < -0.5)
                    {
                        dg = min(dg, dc);
                    }
                    else
                    {
                        if (grouped)
                            d = smin(d, dg, k);
                        dg = dc;
                        grouped = true;
                    }
                }
                if (grouped)
                    d = smin(d, dg, k);

                float soft = max(_View.z, 1e-4);
                float m = 1.0 - smoothstep(-soft, soft, d);
                m *= _Fade;
                return float4(m, m, m, 1);
            }
            ENDHLSL
        }
    }
}

// Draws the tracked body as one analytic, anti-aliased coverage mask (R channel) into a small
// render texture. Shapes: tapered capsules (limbs, torso, neck, hands, feet) and one ellipse
// (head), melted together with a smooth minimum. Run with Graphics.Blit; nothing here samples
// a texture, so the edge is exact at any mask resolution.
Shader "ChromaticRain/SilhouetteMask"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Mask"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAX_CAPS 20

            float4 _Caps[MAX_CAPS];     // a.xy, b.xy (view metres)
            float4 _CapRadii[MAX_CAPS]; // radius a, radius b, weight, unused
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

                for (int c = 0; c < _CapCount; c++)
                {
                    float w = _CapRadii[c].z;
                    if (w <= 0.001)
                        continue;
                    float2 a = _Caps[c].xy;
                    float2 b = _Caps[c].zw;
                    float2 pa = p - a;
                    float2 ba = b - a;
                    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                    float r = lerp(_CapRadii[c].x, _CapRadii[c].y, h) * w;
                    float dc = length(pa - ba * h) - r;
                    d = smin(d, dc, k);
                }

                float soft = max(_View.z, 1e-4);
                float m = 1.0 - smoothstep(-soft, soft, d);
                m *= _Fade;
                return float4(m, m, m, 1);
            }
            ENDHLSL
        }
    }
}

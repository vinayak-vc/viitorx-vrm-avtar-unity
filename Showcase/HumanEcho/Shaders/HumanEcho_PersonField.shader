// Human Echo - the PERSON FIELD: turns the cutout stream's two textures (the sidecar's picture and mask, TOP row first) into the textures every
// effect layer reads, all in Unity's usual orientation (v = 1 is the top of the picture):
//
//   pass 0 Field      rgb = the picture, a = the person mask, mirrored when asked (full mask resolution)
//   pass 1 Down       the mask averaged down to the flow resolution (a 4x4 box), one channel
//   pass 2 Blur       separable box blur of whatever it is given (used on the small mask, then on the flow triple)
//   pass 3 FlowRaw    per pixel "normal flow" of the mask edge: how far the edge moved along its own normal between the previous and the
//                     current (blurred, small) mask, as (vx * w, vy * w, w) in flow pixels per frame, w = how trustworthy that pixel is
//   pass 4 FlowFinal  v = blurred (vx, vy) / blurred w, converted to PICTURE HEIGHTS PER SECOND (isotropic: x and y use the same unit);
//                     b = confidence, 0..1
//   pass 5 CopyMask   the current small mask into the "previous mask" texture
//
// Why the flow is computed on a small, blurred mask: the gradient method only holds while an edge moves less than its own width. A dancer's
// limb moves 10 to 30 mask pixels per mask frame, but the mask edge is only a few pixels wide; at a quarter of the resolution and blurred, the
// same movement is a few pixels over an edge several pixels wide.
//
// Unity does no segmentation here: the mask is what the Python sidecar sent. The flow is an estimate of how the silhouette moved, not a
// measurement of the person's limbs; it is only as good as the mask edge (see the S-01 notes on flicker).
Shader "HumanEcho/PersonField"
{
    Properties
    {
        // Graphics.Blit binds its source as _MainTex only when the shader declares the property.
        _MainTex ("Blit source", 2D) = "white" {}
        _ColorTex ("Picture (top row first)", 2D) = "white" {}
        _MaskTex ("Mask (top row first)", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend Off

        HLSLINCLUDE
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _ColorTex;
            sampler2D _MaskTex;
            sampler2D _MainTex;
            sampler2D _FieldTex;      // the full-resolution field (rgb picture, a mask)
            sampler2D _NowTex;        // the current small blurred mask (r)
            sampler2D _PrevMaskTex;   // the previous small blurred mask (r)
            float4 _SourceTexel;      // 1 / width, 1 / height, width, height of the full-resolution field
            float4 _FieldTexel;       // the same for the small flow textures
            float _Mirror;
            float _NoiseFloor;        // mask change below this is flicker, not motion
            float _GradEpsilon;       // keeps the normal-flow division finite on flat regions
            float _MaxPixelsPerFrame; // clamp of one pixel's flow, in flow pixels
            float2 _BlurAxis;         // (1, 0) or (0, 1)
            float _BlurRadius;        // taps either side, in the blurred texture's pixels (at most 12)
            float _FlowFps;           // frames per second between two mask updates
            float _ConfidenceGain;
        ENDHLSL

        Pass
        {
            Name "Field"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = float2(_Mirror > 0.5 ? 1.0 - i.uv.x : i.uv.x, 1.0 - i.uv.y);
                float3 picture = tex2D(_ColorTex, uv).rgb;
                float mask = tex2D(_MaskTex, uv).r;
                return float4(picture, mask);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Down"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float2 step = _SourceTexel.xy;
                float sum = 0.0;
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        float2 offset = (float2(x, y) - 1.5) * step;
                        sum += tex2D(_FieldTex, i.uv + offset).a;
                    }
                }
                float m = sum / 16.0;
                return float4(m, m, m, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blur"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float4 sum = float4(0, 0, 0, 0);
                float2 step = _BlurAxis * _FieldTexel.xy;
                float taps = 0.0;
                for (int k = -12; k <= 12; k++)
                {
                    if (abs((float)k) <= _BlurRadius)
                    {
                        sum += tex2D(_MainTex, i.uv + step * k);
                        taps += 1.0;
                    }
                }
                return sum / max(taps, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "FlowRaw"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float MaskAt(float2 uv)
            {
                return 0.5 * (tex2D(_NowTex, uv).r + tex2D(_PrevMaskTex, uv).r);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 t = _FieldTexel.xy;
                float now = tex2D(_NowTex, i.uv).r;
                float prev = tex2D(_PrevMaskTex, i.uv).r;
                float change = now - prev;
                float magnitude = max(abs(change) - _NoiseFloor, 0.0);
                change = sign(change) * magnitude;

                // Central differences over two pixels of the mean of both masks: the gradient the edge had while it moved.
                float gx = 0.5 * (MaskAt(i.uv + float2(t.x, 0.0)) - MaskAt(i.uv - float2(t.x, 0.0)));
                float gy = 0.5 * (MaskAt(i.uv + float2(0.0, t.y)) - MaskAt(i.uv - float2(0.0, t.y)));
                float2 g = float2(gx, gy);
                float g2 = dot(g, g);

                // Normal flow: the displacement along the gradient that explains the change. Positive change (mask appears) means the edge
                // moved toward lower mask values, i.e. against the gradient.
                float2 flow = -change * g / (g2 + _GradEpsilon);
                float len = length(flow);
                if (len > _MaxPixelsPerFrame)
                {
                    flow = flow * (_MaxPixelsPerFrame / len);
                }
                float weight = saturate(g2 * 16.0) * saturate(magnitude * 8.0);
                return float4(flow * weight, weight, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "FlowFinal"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float4 blurred = tex2D(_MainTex, i.uv);
                float2 pixelsPerFrame = blurred.xy / max(blurred.z, 1e-4);
                // Flow pixels per frame -> picture heights per second. The small texture covers the picture, whose height is _FieldTexel.w pixels.
                float2 perSecond = pixelsPerFrame * (_FlowFps / _FieldTexel.w);
                float confidence = saturate(blurred.z * _ConfidenceGain);
                return float4(perSecond * confidence, confidence, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "CopyMask"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float m = tex2D(_NowTex, i.uv).r;
                return float4(m, m, m, 1.0);
            }
            ENDHLSL
        }
    }
}

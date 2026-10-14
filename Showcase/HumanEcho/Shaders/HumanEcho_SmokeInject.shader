// Human Echo - what the FluXY smoke layer injects. Both outputs are the PATTERNS a FluxyTarget stamps into the fluid each frame:
//
//   pass 0 Dye       rgb = the dye colour (the picture's own colour mixed with a tint), a = the person mask. FluXY blends this into its dye
//                    buffer with the target's blend mode (SrcAlpha / OneMinusSrcAlpha), so a = 1 inside the body replaces what was there.
//   pass 1 Velocity  FluXY's velocity-texture packing: rg = velocity * 0.5 + 0.5 (so 0.5 = still), b = how strongly to apply it. The
//                    velocity is the person field's motion (screen heights per second) times _PackGain; FluXY unpacks it as (rg * 2 - 1).
Shader "HumanEcho/SmokeInject"
{
    Properties
    {
        _MainTex ("Person field", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend Off

        HLSLINCLUDE
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MotionTex;
            float4 _Tint;
            float _HueSpread;
            float _PictureMix;
            float _PackGain;
            float _VelocityConfidence;
        ENDHLSL

        Pass
        {
            Name "Dye"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            // Rotates the hue of a colour by an angle (radians) in YIQ space: a cheap hue shift that keeps the brightness.
            float3 HueRotate(float3 color, float angle)
            {
                float3 yiq = float3(dot(color, float3(0.299, 0.587, 0.114)),
                                    dot(color, float3(0.596, -0.274, -0.322)),
                                    dot(color, float3(0.211, -0.523, 0.312)));
                float s;
                float c;
                sincos(angle, s, c);
                yiq.yz = float2(yiq.y * c - yiq.z * s, yiq.y * s + yiq.z * c);
                return max(float3(dot(yiq, float3(1.0, 0.956, 0.621)),
                                  dot(yiq, float3(1.0, -0.272, -0.647)),
                                  dot(yiq, float3(1.0, -1.106, 1.703))), 0.0);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 field = tex2D(_MainTex, i.uv);
                // The hue also changes with the height in the picture, so one frame holds several colours (a rainbow body, not one flame).
                float3 tint = HueRotate(_Tint.rgb, _HueSpread * (i.uv.y - 0.5) * 6.2831853);
                float3 dye = lerp(tint, field.rgb, _PictureMix);
                return float4(dye, field.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Velocity"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            float4 frag(v2f_img i) : SV_Target
            {
                float4 motion = tex2D(_MotionTex, i.uv);
                float2 packed = saturate(0.5 + 0.5 * clamp(motion.xy * _PackGain, -1.0, 1.0));
                return float4(packed, saturate(motion.z * _VelocityConfidence), 1.0);
            }
            ENDHLSL
        }
    }
}

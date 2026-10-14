// Human Echo - draws the particle pool as camera-facing quads: one instance per pool slot, six vertices each, read straight from the structured
// buffer the compute shader fills (no mesh, no CPU upload). Dead slots collapse to nothing. A particle is either a soft flat dot or a lit
// sphere (a disc shaded as if it were a ball: diffuse, a small highlight, a darker rim). Output is premultiplied alpha, HDR (Bloom flares it).
Shader "HumanEcho/ParticleDraw"
{
    Properties
    {
        _Gain ("Gain (HDR)", Range(0.5, 6)) = 1.4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Particles"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Particle
            {
                float2 pos;
                float2 vel;
                float life;
                float maxLife;
                float size;
                float kind;
                float3 color;
                float state;
            };

            StructuredBuffer<Particle> _Particles;
            float _ViewHeight;   // world metres of one screen height
            float _Gain;
            float4 _LightDir;    // xyz towards the light

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float3 color : TEXCOORD1;
                float2 kindAlpha : TEXCOORD2;
            };

            static const float2 kCorners[6] = {
                float2(-1, -1), float2(-1, 1), float2(1, -1),
                float2(1, -1), float2(-1, 1), float2(1, 1)
            };

            Varyings vert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                Varyings o;
                Particle p = _Particles[instanceId];
                float2 corner = kCorners[vertexId];
                float alive = p.life > 0.0 ? 1.0 : 0.0;
                float age = p.maxLife - p.life;
                // Fade in over 0.12 s and out over the last quarter second.
                float alpha = saturate(age / 0.12) * saturate(p.life / 0.25) * alive;
                float radius = p.size * 0.5 * _ViewHeight * alive;
                float3 world = float3(p.pos * _ViewHeight + corner * radius, 0.0);
                o.positionCS = TransformWorldToHClip(world);
                o.corner = corner;
                o.color = p.color;
                o.kindAlpha = float2(p.kind, alpha);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                if (r2 > 1.0)
                {
                    discard;
                }
                float kind = i.kindAlpha.x;
                float alpha = i.kindAlpha.y;
                float3 color = i.color * _Gain;
                float coverage = 1.0 - smoothstep(0.7, 1.0, r2);
                if (kind > 0.5)
                {
                    float3 normal = float3(i.corner, sqrt(max(1.0 - r2, 0.0)));
                    float3 light = normalize(_LightDir.xyz);
                    float diffuse = saturate(dot(normal, light));
                    float3 reflected = reflect(-light, normal);
                    float specular = pow(saturate(reflected.z), 24.0);
                    float rim = pow(1.0 - normal.z, 2.0);
                    color = color * (0.18 + 0.95 * diffuse) * (1.0 - 0.45 * rim) + specular * 0.7;
                    coverage = 1.0 - smoothstep(0.92, 1.0, r2);
                }
                float a = coverage * alpha;
                return half4(color * a, a);
            }
            ENDHLSL
        }
    }
}

Shader "SwingingPaint/BucketFluid/GPUOutflowParticleInstanced"
{
    Properties
    {
        _ParticleSize ("Particle Size", Float) = 0.045
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct OutflowParticle
            {
                float3 positionWorld;
                float density;

                float3 previousPositionWorld;
                float nearDensity;

                float3 velocityWorld;
                float radius;

                float4 color;

                float amount;
                float wetness;
                float age;
                float lifetime;

                int active;
                int cellHash;
                int cellIndex;
                float padding;
            };

            StructuredBuffer<OutflowParticle> _OutflowParticles;
            float _ParticleSize;
            float3 _CameraRight;
            float3 _CameraUp;

            struct appdata
            {
                float4 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata input)
            {
                v2f output;
                OutflowParticle particle = _OutflowParticles[input.instanceID];
                float active = particle.active != 0 ? 1.0 : 0.0;
                float speed = length(particle.velocityWorld);
                float stretch = lerp(1.0, 2.6, saturate(speed / 10.0));
                float visualSize = max(_ParticleSize, particle.radius * 2.0);
                float3 offset =
                    _CameraRight * input.vertex.x * visualSize +
                    _CameraUp * input.vertex.y * visualSize * stretch;
                float3 worldPosition = particle.positionWorld + offset * active;

                if (active < 0.5)
                {
                    worldPosition = float3(0.0, -100000.0, 0.0);
                }

                output.vertex = UnityWorldToClipPos(worldPosition);
                output.color = fixed4(particle.color.rgb, particle.color.a * particle.wetness * active);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return input.color;
            }
            ENDCG
        }
    }
}

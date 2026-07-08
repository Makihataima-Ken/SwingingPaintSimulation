Shader "SwingingPaint/BucketFluid/GPUOutflowParticleInstanced"
{
    Properties
    {
        _ParticleSize ("Particle Size", Float) = 0.045
        _ParticleOpacityMultiplier ("Particle Opacity Multiplier", Float) = 0.95
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
                float sourceHoleIndex;
            };

            StructuredBuffer<OutflowParticle> _OutflowParticles;
            float _ParticleSize;
            float _ParticleOpacityMultiplier;
            float3 _CameraRight;
            float3 _CameraUp;
            float3 _CameraForward;

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
                float stretch = lerp(1.15, 3.3, saturate(speed / 10.0));
                float visualSize = max(_ParticleSize, particle.radius * 2.0);
                float3 longAxis = particle.velocityWorld - _CameraForward * dot(particle.velocityWorld, _CameraForward);

                if (dot(longAxis, longAxis) < 0.00001)
                {
                    longAxis = _CameraUp;
                }
                else
                {
                    longAxis = normalize(longAxis);
                }

                float3 sideAxis = cross(_CameraForward, longAxis);
                if (dot(sideAxis, sideAxis) < 0.00001)
                {
                    sideAxis = _CameraRight;
                }
                else
                {
                    sideAxis = normalize(sideAxis);
                }

                float3 offset =
                    sideAxis * input.vertex.x * visualSize +
                    longAxis * input.vertex.y * visualSize * stretch;
                float3 worldPosition = particle.positionWorld + offset * active;

                if (active < 0.5)
                {
                    worldPosition = float3(0.0, -100000.0, 0.0);
                }

                output.vertex = UnityWorldToClipPos(worldPosition);
                output.color = fixed4(particle.color.rgb, saturate(particle.color.a * particle.wetness * _ParticleOpacityMultiplier) * active);
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

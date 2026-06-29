Shader "SwingingPaint/BucketFluid/GPUOutflowStreamConnector"
{
    Properties
    {
        _StreamRadiusMultiplier ("Stream Radius Multiplier", Float) = 1.7
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
            float _StreamRadiusMultiplier;
            float3 _CameraRight;
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
                int neighborIndex = particle.cellIndex;
                float active = particle.active != 0 && neighborIndex >= 0 ? 1.0 : 0.0;

                OutflowParticle neighbor = _OutflowParticles[max(neighborIndex, 0)];
                active *= neighbor.active != 0 ? 1.0 : 0.0;

                float3 start = particle.positionWorld;
                float3 end = neighbor.positionWorld;
                float3 segment = end - start;
                float segmentLength = length(segment);
                float3 segmentDirection = segmentLength > 0.00001 ? segment / segmentLength : float3(0.0, -1.0, 0.0);
                float3 side = cross(segmentDirection, _CameraForward);

                if (dot(side, side) < 0.00001)
                {
                    side = _CameraRight;
                }
                else
                {
                    side = normalize(side);
                }

                float t = saturate(input.vertex.y + 0.5);
                float halfWidth = max(particle.radius, neighbor.radius) * max(0.1, _StreamRadiusMultiplier);
                float3 worldPosition = lerp(start, end, t) + side * input.vertex.x * halfWidth * active;

                if (active < 0.5)
                {
                    worldPosition = float3(0.0, -100000.0, 0.0);
                }

                float wetness = min(particle.wetness, neighbor.wetness);
                float densityAlpha = saturate((particle.density + neighbor.density) * 0.15);
                float alpha = particle.color.a * wetness * lerp(0.55, 1.0, densityAlpha) * active;

                output.vertex = UnityWorldToClipPos(worldPosition);
                output.color = fixed4(particle.color.rgb, alpha);
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

Shader "SwingingPaint/BucketFluid/GPUOutflowStreamConnector"
{
    Properties
    {
        _StreamRadiusMultiplier ("Stream Radius Multiplier", Float) = 1.7
        _TrailLengthMultiplier ("Trail Length Multiplier", Float) = 1.6
        _MinimumConnectorLength ("Minimum Connector Length", Float) = 0.035
        _ConnectorOpacityMultiplier ("Connector Opacity Multiplier", Float) = 1.1
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
            float _TrailLengthMultiplier;
            float _MinimumConnectorLength;
            float _ConnectorOpacityMultiplier;
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
                float active = particle.active != 0 ? 1.0 : 0.0;

                OutflowParticle neighbor = particle;
                float hasNeighbor = 0.0;
                if (neighborIndex >= 0)
                {
                    neighbor = _OutflowParticles[neighborIndex];
                    hasNeighbor = neighbor.active != 0 ? 1.0 : 0.0;
                }

                float3 start = hasNeighbor > 0.5 ? particle.positionWorld : particle.previousPositionWorld;
                float3 end = hasNeighbor > 0.5 ? neighbor.positionWorld : particle.positionWorld;
                float3 segment = end - start;
                float segmentLength = length(segment);
                float3 velocityDirection = length(particle.velocityWorld) > 0.00001
                    ? normalize(particle.velocityWorld)
                    : float3(0.0, -1.0, 0.0);

                if (hasNeighbor < 0.5)
                {
                    float isolatedTrailLength = max(
                        max(_MinimumConnectorLength, segmentLength) * max(0.1, _TrailLengthMultiplier) * 3.1,
                        particle.radius * max(10.0, _StreamRadiusMultiplier * 6.2));
                    start = particle.positionWorld - velocityDirection * isolatedTrailLength;
                    end = particle.positionWorld;
                    segment = end - start;
                    segmentLength = length(segment);
                }
                else if (segmentLength < _MinimumConnectorLength)
                {
                    float3 center = (start + end) * 0.5;
                    float lengthTarget = max(_MinimumConnectorLength, segmentLength) * max(0.1, _TrailLengthMultiplier);
                    start = center - velocityDirection * lengthTarget * 0.5;
                    end = center + velocityDirection * lengthTarget * 0.5;

                    segment = end - start;
                    segmentLength = length(segment);
                }

                float3 segmentDirection = segmentLength > 0.00001 ? segment / segmentLength : velocityDirection;
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
                float neighborRadius = hasNeighbor > 0.5 ? neighbor.radius : particle.radius;
                float speedWidth = lerp(1.0, 1.22, saturate(length(particle.velocityWorld) / 8.0));
                float halfWidth = max(particle.radius, neighborRadius) * max(0.1, _StreamRadiusMultiplier) * speedWidth;
                float3 worldPosition = lerp(start, end, t) + side * input.vertex.x * halfWidth * active;

                if (active < 0.5)
                {
                    worldPosition = float3(0.0, -100000.0, 0.0);
                }

                float wetness = hasNeighbor > 0.5 ? min(particle.wetness, neighbor.wetness) : particle.wetness;
                float neighborDensity = hasNeighbor > 0.5 ? neighbor.density : particle.density;
                float densityAlpha = saturate((particle.density + neighborDensity) * 0.15);
                float alpha = saturate(particle.color.a * wetness * lerp(0.72, 1.0, densityAlpha) * _ConnectorOpacityMultiplier) * active;

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

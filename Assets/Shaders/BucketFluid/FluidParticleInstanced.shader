Shader "SwingingPaint/BucketFluid/FluidParticleInstanced"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.08, 0.34, 0.95, 0.82)
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

            struct FluidParticle
            {
                float3 positionLocal;
                float density;

                float3 predictedPositionLocal;
                float nearDensity;

                float3 velocityLocal;
                float pressure;

                float3 deltaPosition;
                float nearPressure;

                int active;
                int cellHash;
                int cellIndex;
                float padding;
            };

            StructuredBuffer<FluidParticle> _Particles;
            float4x4 _BucketLocalToWorld;
            float _ParticleSize;
            fixed4 _BaseColor;

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
                FluidParticle particle = _Particles[input.instanceID];

                float active = particle.active != 0 ? 1.0 : 0.0;
                float3 particleVertexLocal = particle.positionLocal + input.vertex.xyz * _ParticleSize * active;
                float4 worldPosition = mul(_BucketLocalToWorld, float4(particleVertexLocal, 1.0));

                if (active < 0.5)
                {
                    worldPosition.xyz = float3(0.0, -100000.0, 0.0);
                }

                output.vertex = UnityWorldToClipPos(worldPosition.xyz);
                output.color = fixed4(_BaseColor.rgb, _BaseColor.a * active);
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

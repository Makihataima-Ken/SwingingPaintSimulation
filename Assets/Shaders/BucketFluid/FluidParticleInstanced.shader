Shader "SwingingPaint/BucketFluid/FluidParticleInstanced"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.08, 0.34, 0.95, 0.82)
        _ParticleSize ("Particle Size", Float) = 0.055
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
            ZWrite Off
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
                float2 maskPosition : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                FluidParticle particle = _Particles[input.instanceID];

                float active = particle.active != 0 ? 1.0 : 0.0;
                float4 centerWorld = mul(_BucketLocalToWorld, float4(particle.positionLocal, 1.0));
                float3 worldPosition = centerWorld.xyz +
                    (_CameraRight * input.vertex.x + _CameraUp * input.vertex.y) * _ParticleSize * active;

                if (active < 0.5)
                {
                    worldPosition = float3(0.0, -100000.0, 0.0);
                }

                output.vertex = UnityWorldToClipPos(worldPosition);
                output.color = fixed4(_BaseColor.rgb, _BaseColor.a * active);
                output.maskPosition = input.vertex.xy * 2.0;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float distanceSq = dot(input.maskPosition, input.maskPosition);
                float softCircle = 1.0 - smoothstep(0.55, 1.0, distanceSq);
                return fixed4(input.color.rgb, input.color.a * softCircle);
            }
            ENDCG
        }
    }
}

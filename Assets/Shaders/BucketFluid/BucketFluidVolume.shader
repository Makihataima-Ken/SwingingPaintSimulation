Shader "SwingingPaint/BucketFluid/BucketFluidVolume"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.05, 0.22, 0.95, 0.94)
        _TopColor ("Top Color", Color) = (0.18, 0.36, 1.0, 0.94)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-5"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            fixed4 _TopColor;
            float _Smoothness;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normalWorld : TEXCOORD0;
                float3 viewDirWorld : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata input)
            {
                v2f output;
                float4 worldPosition = mul(unity_ObjectToWorld, input.vertex);
                output.vertex = UnityWorldToClipPos(worldPosition.xyz);
                output.normalWorld = UnityObjectToWorldNormal(input.normal);
                output.viewDirWorld = _WorldSpaceCameraPos.xyz - worldPosition.xyz;
                output.color = input.color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 normalWorld = normalize(input.normalWorld);
                float3 viewDirWorld = normalize(input.viewDirWorld);
                float topFacing = saturate(normalWorld.y * 0.65 + 0.35);
                fixed4 color = lerp(_BaseColor, _TopColor, topFacing);
                color.a *= input.color.a;

                float fresnel = pow(1.0 - saturate(dot(normalWorld, viewDirWorld)), 3.0);
                float highlight = saturate(normalWorld.y) * 0.12 + fresnel * lerp(0.08, 0.22, _Smoothness);
                color.rgb = saturate(color.rgb + highlight);
                return color;
            }
            ENDCG
        }
    }
}

// Waermebild: Fahrzeug als leuchtende Flaeche (unbeleuchtet, HDR-Helligkeit = Temperatur).
// Liegt exakt ueber dem Modell (gleiches Mesh), gewinnt per Polygon-Offset den Tiefentest.
Shader "PanzerSim/Waerme"
{
    Properties
    {
        _Color ("Temperatur (HDR)", Color) = (3, 3, 3, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half shade : TEXCOORD0; };

            V vert(A i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                float3 v = normalize(GetWorldSpaceViewDir(ws));
                o.shade = 0.7h + 0.3h * saturate(abs(dot(n, v)));   // Raender etwas dunkler: Form bleibt erkennbar
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                return half4(_Color.rgb * i.shade, 1);
            }
            ENDHLSL
        }
    }
}

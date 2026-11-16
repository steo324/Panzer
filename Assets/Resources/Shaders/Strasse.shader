// Strassenbelag (Roads.cs): Band knapp ueber dem Gelaende. Alpha der Textur schneidet die Raender aus (ausgefranst).
// Gegen Durchstossen des Gelaendes (dessen Detailstufen in der Ferne) wird der Belag entlang des Sehstrahls
// leicht zur Kamera geschoben - am Bildschirm aendert sich nichts, nur die Tiefe.
Shader "PanzerSim/Strasse"
{
    Properties
    {
        _BaseMap ("Textur", 2D) = "white" {}
        _Tint ("Farbe", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha-Schwelle", Range(0, 1)) = 0.5
        _Gloss ("Glanz", Range(0, 1)) = 0.1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _Tint;
        half _Cutoff;
        half _Gloss;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float3 RoadWorld(float3 posOS)
    {
        float3 posWS = TransformObjectToWorld(posOS);
        float3 toCam = _WorldSpaceCameraPos - posWS;
        float dist = length(toCam);
        return posWS + toCam / max(dist, 0.01) * min(dist * 0.0012, 1.5);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 posWS : TEXCOORD1;
                float3 nWS : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(RoadWorld(v.positionOS.xyz));
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(c.a - _Cutoff);
                half3 n = normalize(i.nWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                half ndl = saturate(dot(n, L.direction));
                half3 v = normalize(_WorldSpaceCameraPos - i.posWS);
                half spec = pow(saturate(dot(n, normalize(L.direction + v))), 24.0) * _Gloss;
                half3 rgb = c.rgb * _Tint.rgb * (L.color * L.shadowAttenuation * ndl + SampleSH(n)) + L.color * L.shadowAttenuation * spec;
                return half4(MixFog(rgb, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformWorldToHClip(RoadWorld(v.positionOS.xyz));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return 0; }
            ENDHLSL
        }
    }
}

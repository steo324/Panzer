// Baeume aus Code-Geometrie (Forest.cs): ohne Textur, Farbe und Verdeckung aus den Eckpunkten.
// Vertexfarbe: rgb = Grundfarbe, a = Verdeckung (innen/unten in der Krone dunkler).
// UV0: x = Hoehe des Punkts 0..1, y = Biegsamkeit (Stamm 0 ... Laub 1), z = Baumhoehe in m.
// Jeder Baum bekommt aus seiner Lage eine eigene Farbnuance (heller/dunkler, gelblicher/blaeulicher).
Shader "PanzerSim/BaumMesh"
{
    Properties
    {
        _Tint ("Farbe", Color) = (1, 1, 1, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Vegetation.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _Tint;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        half4 color : COLOR;
        float3 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    float3 TreeWorld(Attributes v, out float3 nWS, out float3 rootWS)
    {
        float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
        nWS = TransformObjectToWorldNormal(v.normalOS);
        rootWS = TransformObjectToWorld(float3(0, 0, 0));
        float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
        float height = v.uv.z * scaleY;
        float flex = v.uv.y;
        posWS += VegetationOffset(rootWS, v.uv.x, height, 0.10 + 0.16 * flex);
        float ph = _Time.y * 5.0 + dot(posWS, float3(1.7, 2.3, 1.1));
        posWS += nWS * sin(ph) * 0.05 * flex * (0.5 + _VegWind.z * 8.0);
        return posWS;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

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
                float3 posWS : TEXCOORD0;
                float3 nWS : TEXCOORD1;
                half4 col : TEXCOORD2;     // rgb Farbe (mit Nuance), a Verdeckung
                half leaf : TEXCOORD3;
                float fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                float3 rootWS;
                o.posWS = TreeWorld(v, o.nWS, rootWS);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                // Farbnuance je Baum aus seiner Lage
                float h1 = frac(sin(dot(rootWS.xz, float2(12.9898, 78.233))) * 43758.5453);
                float h2 = frac(h1 * 13.71 + rootWS.x * 0.0137);
                half leaf = saturate(v.uv.y * 2.0 - 0.4);
                half3 tint = lerp(half3(0.80, 0.84, 0.86), half3(1.18, 1.12, 0.88), h1);   // dunkel-blaeulich ... hell-gelblich
                tint *= lerp(0.92, 1.08, h2);
                o.col.rgb = v.color.rgb * lerp(lerp(0.9, 1.1, h2), tint, leaf) * _Tint.rgb;
                o.col.a = v.color.a;
                o.leaf = leaf;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.nWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                half d = dot(n, L.direction);
                half ndl = lerp(saturate(d), saturate(d * 0.6 + 0.4), i.leaf);   // Laub: weiches Licht, Stamm: normal
                half3 amb = SampleSH(n);
                half3 rgb = i.col.rgb * (L.color * L.shadowAttenuation * ndl + amb * 0.9) * i.col.a;
                return half4(MixFog(rgb, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            float3 _LightDirection;
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                float3 nWS, rootWS;
                float3 posWS = TreeWorld(v, nWS, rootWS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                float3 nWS, rootWS;
                o.positionCS = TransformWorldToHClip(TreeWorld(v, nWS, rootWS));
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}

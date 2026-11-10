// Baeume: Rinde + Laub/Nadeln, wiegt im Wind und biegt sich bei Druckwellen.
// Vertexfarbe: r = Biegsamkeit (Stamm unten 0 ... Blattwerk 1), g = Hoehe 0..1, b = Baumhoehe / 32 m.
Shader "PanzerSim/Baum"
{
    Properties
    {
        _BaseMap ("Textur", 2D) = "white" {}
        _Cutoff ("Alpha-Schwelle", Range(0, 1)) = 0.5
        _Tint ("Farbe", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _Flutter ("Blattflattern", Float) = 1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Vegetation.hlsl"

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _Tint;
        half _Cutoff;
        float _Cull;
        float _Flutter;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
    };

    float3 TreeWorld(Attributes v, out float3 nWS)
    {
        float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
        nWS = TransformObjectToWorldNormal(v.normalOS);
        float3 root = TransformObjectToWorld(float3(0, 0, 0));
        float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
        float height = v.color.b * 32.0 * scaleY;
        float flex = v.color.r;
        // ganzer Baum wiegt (steif), Blattwerk zusaetzlich
        posWS += VegetationOffset(root, v.color.g, height, 0.12 + 0.18 * flex);
        // Blaetter flattern
        float ph = _Time.y * 6.0 + dot(posWS, float3(1.7, 2.3, 1.1));
        posWS += nWS * sin(ph) * 0.04 * flex * _Flutter * (0.5 + _VegWind.z * 8.0);
        return posWS;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull [_Cull]

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
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
                half ao : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posWS = TreeWorld(v, o.nWS);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.ao = lerp(0.55, 1.0, saturate(v.color.g * 1.3));   // unten in der Krone dunkler
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(c.a - _Cutoff);
                half3 n = normalize(i.nWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                half ndl = saturate(dot(n, L.direction) * 0.7 + 0.3);   // Blattwerk: weiches Licht
                half3 rgb = c.rgb * _Tint.rgb * (L.color * L.shadowAttenuation * ndl + SampleSH(n)) * i.ao;
                return half4(MixFog(rgb, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 nWS;
                float3 posWS = TreeWorld(v, nWS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return 0; }
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
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 nWS;
                o.positionCS = TransformWorldToHClip(TreeWorld(v, nWS));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return 0; }
            ENDHLSL
        }
    }
}

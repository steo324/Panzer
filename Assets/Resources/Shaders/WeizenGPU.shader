// Weizen-Pflanzen, komplett von der GPU gezeichnet (Indirect-Draw).
// Lage, Drehung und Biegung kommen aus einem Puffer; gebogen wird hier im Vertex-Shader (Kreisbogen + Kippen am Fuss).
Shader "PanzerSim/WeizenGPU"
{
    Properties
    {
        _BaseMap ("Textur", 2D) = "white" {}
        _Cutoff ("Alpha-Schwelle", Range(0, 1)) = 0.3
        _Height ("Pflanzenhoehe im Mesh", Float) = 1
        _Tint ("Farbe", Color) = (1, 1, 1, 1)
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
        float _Height;
    CBUFFER_END

    struct Plant
    {
        float3 pos;
        float scale;
        float yaw;
        float angle;
        float lodRank;
        float kindState;
    };
    StructuredBuffer<Plant> _Plants;
    StructuredBuffer<uint> _Visible;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
        uint instanceID : SV_InstanceID;
    };

    void PlantToWorld(Attributes v, out float3 posWS, out float3 nWS)
    {
        Plant p = _Plants[_Visible[v.instanceID]];
        float3 q = v.positionOS.xyz, n = v.normalOS;

        // Biegung nach +Z: unten gerade, oben immer staerker (max. 50 Grad), der Rest ist starres Kippen am Fuss
        float kap = min(p.angle * 0.55, 0.8727);
        float rigid = p.angle - kap;
        if (kap > 1e-4)
        {
            float k = kap / max(_Height, 0.1), R = 1.0 / k;
            float th = max(q.y, 0.0) * k, s = sin(th), c = cos(th), z = q.z;
            q = float3(q.x, (R - z) * s, R - (R - z) * c);
            n = float3(n.x, n.y * c - n.z * s, n.y * s + n.z * c);
        }
        float sr = sin(rigid), cr = cos(rigid);
        q = float3(q.x, q.y * cr - q.z * sr, q.y * sr + q.z * cr);
        n = float3(n.x, n.y * cr - n.z * sr, n.y * sr + n.z * cr);

        uint state = ((uint)(p.kindState + 0.5)) & 7;
        q *= float3(p.scale, p.scale * (state >= 2 ? 0.92 : 1.0), p.scale);

        float sy = sin(p.yaw), cy = cos(p.yaw);
        q = float3(q.x * cy + q.z * sy, q.y, -q.x * sy + q.z * cy);
        n = float3(n.x * cy + n.z * sy, n.y, -n.x * sy + n.z * cy);
        posWS = p.pos + q;
        // Wind und Druckwellen (plattgewalzte Halme bewegen sich nicht mehr)
        float stand = state < 2 ? cos(p.angle) : 0.0;
        posWS += VegetationOffset(p.pos, saturate(v.positionOS.y / max(_Height, 0.1)), p.scale * _Height, 1.0) * stand;
        nWS = normalize(n);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
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
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                PlantToWorld(v, o.posWS, o.nWS);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(c.a - _Cutoff);
                half3 n = normalize(front ? i.nWS : -i.nWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.posWS));
                half ndl = saturate(dot(n, L.direction));
                // duenne Blaetter: etwas Licht scheint auch von hinten durch
                half3 direct = L.color * L.shadowAttenuation * (ndl * 0.75 + 0.25);
                half3 rgb = c.rgb * _Tint.rgb * (direct + SampleSH(n));
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
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS, nWS;
                PlantToWorld(v, posWS, nWS);
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
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS, nWS;
                PlantToWorld(v, posWS, nWS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}

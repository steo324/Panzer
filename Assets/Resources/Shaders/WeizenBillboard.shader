// Ferner Weizen: ein Bild pro Stelle, das sich immer zur Kamera dreht (nur um die Hochachse).
// Lage kommt aus einem Puffer (GPU-Auswahl, Indirect-Draw).
// Unter _FadeStart Metern wird das Bild auf Groesse 0 geschrumpft (dort stehen die echten 3D-Halme).
Shader "PanzerSim/WeizenBillboard"
{
    Properties
    {
        _BaseMap ("Bild", 2D) = "white" {}
        _Cutoff ("Alpha-Schwelle", Range(0, 1)) = 0.45
        _Tint ("Farbe", Color) = (1, 1, 1, 1)
        _FadeStart ("Einblenden ab (m)", Float) = 90
        _FadeLen ("Einblendstrecke (m)", Float) = 12
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Vegetation.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _Cutoff;
                float _FadeStart;
                float _FadeLen;
            CBUFFER_END
            StructuredBuffer<float4> _Bills;
            StructuredBuffer<uint> _Visible;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 light : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float4 b = _Bills[_Visible[i.instanceID]];
                float3 origin = b.xyz;
                float3 toCam = _WorldSpaceCameraPos.xyz - origin;
                float dist = length(toCam);
                float3 flat = normalize(float3(toCam.x, 0.0, toCam.z) + float3(1e-4, 0.0, 0.0));
                float3 right = float3(flat.z, 0.0, -flat.x);
                float grow = saturate((dist - _FadeStart) / max(_FadeLen, 0.01));
                float3 world = origin + (right * (i.positionOS.x * b.w) + float3(0.0, i.positionOS.y * abs(b.w), 0.0)) * grow;
                world += VegetationOffset(origin, saturate(i.positionOS.y / 1.275), abs(b.w) * 1.275, 1.0) * grow;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);

                // Licht wie bei einer Pflanzendecke: Normale ueberwiegend nach oben
                half3 n = normalize(half3(0, 1, 0) + (half3)flat * 0.5);
                Light L = GetMainLight();
                o.light = L.color * (saturate(dot(n, L.direction)) * 0.85) + SampleSH(n);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(c.a - _Cutoff);
                half3 rgb = c.rgb * _Tint.rgb * i.light;
                rgb = MixFog(rgb, i.fog);
                return half4(rgb, 1);
            }
            ENDHLSL
        }
    }
}

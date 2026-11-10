// Einfacher Partikel-Shader fuer Rauch, Staub, Feuer und Funken (Farbe = Textur x Vertexfarbe).
// _Mode 0: normale Transparenz (Rauch/Staub, leicht vom Licht abhaengig), 1: additiv (Feuer, Funken, Blitz).
Shader "PanzerSim/Partikel"
{
    Properties
    {
        _BaseMap ("Textur", 2D) = "white" {}
        _SrcBlend ("Src", Float) = 5
        _DstBlend ("Dst", Float) = 10
        _Lit ("Lichtanteil", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _SrcBlend, _DstBlend, _Lit;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                half3 light = 1;
                if (_Lit > 0)
                {
                    Light L = GetMainLight();
                    half3 amb = SampleSH(half3(0, 1, 0));
                    light = lerp(half3(1, 1, 1), L.color * 0.75 + amb, _Lit);
                }
                o.color = half4(i.color.rgb * light, i.color.a);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * i.color;
                if (_DstBlend < 1.5)      // additiv: Nebel macht es dunkler statt grau
                    c.rgb *= c.a * (1 - saturate(1 - i.fog) * 0.8);
                else
                    c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }
    }
}

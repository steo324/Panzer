// Hoehennebel und Dunst (PanzerSim): pro Pixel die Nebelmenge entlang des Blickstrahls - dicht in Bodennaehe, nach oben duenner
// (exponentielle Hoehenverteilung, geschlossen integriert). Zur Sonne hin heller (Gegenlicht/Vorwaertsstreuung).
// Parameter kommen als globale Shader-Werte aus Visuals.cs.
Shader "Hidden/PanzerSim/HeightFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "HeightFog"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _PzFogColor;    // rgb Dunstfarbe
            float4 _PzSunColor;    // rgb Farbe zur Sonne hin, a Staerke
            float4 _PzFogParams;   // x Dichte am Boden (1/m), y Hoehenabfall (1/m), z Bodenhoehe (m), w max. Deckkraft
            float4 _PzSunDir;      // xyz Richtung zur Sonne
            float  _PzSkyDist;     // Entfernung fuer Himmelspixel (m)

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_PzFogParams.x <= 0) return col;

                float raw = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 wp = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = wp - cam;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-3);
                #if UNITY_REVERSED_Z
                    bool sky = raw <= 1e-7;
                #else
                    bool sky = raw >= 1 - 1e-7;
                #endif
                if (sky) dist = _PzSkyDist;

                // Integral der Dichte d(h) = d0 * exp(-k (h - h0)) entlang des Strahls
                float k = _PzFogParams.y;
                float dy = dir.y * dist;
                // Hoehe ueber dem Nebelboden begrenzen: weit darunter (z. B. Menue-Podest) wuerde exp() explodieren -> alles weiss
                float camH = clamp(cam.y - _PzFogParams.z, -60, 4000);
                float start = _PzFogParams.x * exp(-k * camH);
                float t = clamp(k * dy, -20, 80);   // gegen Ueberlauf bei weit nach unten gerichteten Strahlen
                float integral = abs(t) > 1e-4 ? start * dist * (1 - exp(-t)) / t : start * dist;
                float amount = saturate(1 - exp(-integral)) * _PzFogParams.w;

                float sunAmt = pow(saturate(dot(dir, _PzSunDir.xyz)), 6) * _PzSunColor.a;
                float3 fog = lerp(_PzFogColor.rgb, _PzSunColor.rgb, saturate(sunAmt));
                return half4(lerp(col.rgb, fog, amount), col.a);
            }
            ENDHLSL
        }
    }
}

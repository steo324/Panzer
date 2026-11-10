// Gemeinsam fuer Weizen, Gras und Baeume: Wind (mit Boeen, die als Wellen uebers Feld laufen) und Druckwellen.
#ifndef PANZERSIM_VEGETATION
#define PANZERSIM_VEGETATION

float4 _Blasts[8];       // xyz Ort, w Startzeit (Time.timeSinceLevelLoad)
float4 _BlastParams[8];  // x Radius, y Staerke
float4 _VegWind;         // xy Richtung, z Staerke

// rootWS: Fuss der Pflanze; h01: Hoehe des Punkts (0 Fuss .. 1 Spitze); height: Pflanzenhoehe in m; flex: wie biegsam (Gras 1, Baum klein)
float3 VegetationOffset(float3 rootWS, float h01, float height, float flex)
{
    float bendShape = h01 * h01;
    float3 off = 0;

    // Wind: Grundwiegen + Boeen, die in Windrichtung ueber das Feld wandern
    float2 wd = _VegWind.xy;
    float phase = dot(rootWS.xz, wd) * 0.12 - _Time.y * 1.4;
    float gust = sin(phase) * 0.5 + 0.5;
    gust *= gust;
    float flutter = sin(_Time.y * 2.7 + rootWS.x * 1.3 + rootWS.z * 1.7) * 0.25;
    off.xz += wd * (0.3 + gust + flutter) * _VegWind.z * bendShape * height * flex;

    // Druckwellen: Front laeuft mit ca. 120 m/s nach aussen, Pflanzen werden weggedrueckt und schwingen nach
    [unroll] for (int i = 0; i < 8; i++)
    {
        float r = _BlastParams[i].x;
        if (r <= 0) continue;
        float age = _Time.y - _Blasts[i].w;
        if (age < 0 || age > 5) continue;
        float2 d = rootWS.xz - _Blasts[i].xz;
        float dist = length(d);
        if (dist > r) continue;
        float t = age - dist / 120.0;
        if (t < 0) continue;
        float fall = 1.0 - dist / r;
        fall *= fall;
        float env = exp(-t * (2.0 + 2.0 * flex)) * cos(t * (6.0 + 6.0 * flex));
        float amp = _BlastParams[i].y * fall * env;
        off.xz += (d / max(dist, 0.05)) * amp * bendShape * height * 0.8 * flex;
    }
    // Laenge ungefaehr erhalten: wer weit zur Seite geht, kommt etwas tiefer
    off.y -= dot(off.xz, off.xz) * 0.5 / max(height, 0.1);
    return off;
}
#endif

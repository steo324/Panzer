"""
Holt eine echte Karte (5 x 5 km) und schreibt kompakte Spieldaten nach Assets/Resources/Karten/<Name>/:
  hoehe.bytes  - Gelaendehoehen (DGM1 NRW, auf 3,9 m Raster), uint16 + Kopf
  osm.txt      - Strassen, Gebaeude, Flaechen, Gewaesser, Ortsnamen aus OpenStreetMap (Meter, Mitte = 0,0; x Ost, z Nord)
Quellen: Geobasis NRW (DGM, Datenlizenz Deutschland - Zero 2.0), OpenStreetMap (ODbL, (c) OpenStreetMap-Mitwirkende).
Aufruf: blender -b --factory-startup --python fetch_map.py -- <Name> <lat> <lon> [groesse_m=5000] [raster=1281]
"""
import sys, os, json, math, struct, urllib.request, urllib.parse, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import geo

args = sys.argv[sys.argv.index("--") + 1:]
NAME, LAT, LON = args[0], float(args[1]), float(args[2])
SIZE = float(args[3]) if len(args) > 3 else 5000.0
RES = int(args[4]) if len(args) > 4 else 1281
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Karten", NAME)
CACHE = os.path.join(ROOT, "Temp", "RealMap", NAME)
os.makedirs(OUT, exist_ok=True); os.makedirs(CACHE, exist_ok=True)
UA = {"User-Agent": "BallistikTankFronts-MapTool/1.0"}
E0, N0 = geo.latlon_to_utm32(LAT, LON)
H = SIZE / 2
print(f"KARTE {NAME}: Mitte {LAT},{LON} = UTM {E0:.1f} {N0:.1f}, {SIZE:.0f} m, Raster {RES}")

def get(url, path, timeout=300, data=None):
    if os.path.exists(path) and os.path.getsize(path) > 1000: return open(path, "rb").read()
    for attempt in range(3):
        try:
            req = urllib.request.Request(url, data=data, headers=UA)
            b = urllib.request.urlopen(req, timeout=timeout).read()
            open(path, "wb").write(b); return b
        except Exception as e:
            print("  neuer Versuch:", e); time.sleep(5)
    raise RuntimeError("Download fehlgeschlagen: " + url[:120])

# ---------------- Hoehen (DGM NRW) ----------------
tif = os.path.join(CACHE, "dgm.tif")
cell = SIZE / (RES - 1)
url = ("https://www.wcs.nrw.de/geobasis/wcs_nw_dgm?SERVICE=WCS&VERSION=2.0.1&REQUEST=GetCoverage&COVERAGEID=nw_dgm&FORMAT=image/tiff"
       f"&SUBSET=x({E0 - H - cell / 2:.2f},{E0 + H + cell / 2:.2f})&SUBSET=y({N0 - H - cell / 2:.2f},{N0 + H + cell / 2:.2f})&SCALESIZE=x({RES}),y({RES})")
get(url, tif)
W, Hh, vals, comp = geo.read_tiff_float(tif)
print(f"HOEHE TIFF {W}x{Hh} (Kompression {comp}), {min(vals):.1f}..{max(vals):.1f} m")
assert W == RES and Hh == RES, "Rastergroesse passt nicht"
lo = min(vals); scale = 0.01   # 1 cm
with open(os.path.join(OUT, "hoehe.bytes"), "wb") as f:
    f.write(struct.pack("<iffff", RES, cell, lo, scale, SIZE))
    rows = []
    for iz in range(RES):                      # iz = 0: Sueden (TIFF-Zeile 0 ist Norden)
        src = (RES - 1 - iz) * RES
        rows.append(struct.pack("<" + "H" * RES, *[max(0, min(65535, int(round((vals[src + ix] - lo) / scale)))) for ix in range(RES)]))
    f.write(b"".join(rows))
print("HOEHE geschrieben")

# Umland als Hintergrund (15 x 15 km, grob): echte Hoehenzuege am Horizont statt Kartenrand
UM, URES = SIZE * 3, 513
ucell = UM / (URES - 1)
utif = os.path.join(CACHE, "umland.tif")
uurl = ("https://www.wcs.nrw.de/geobasis/wcs_nw_dgm?SERVICE=WCS&VERSION=2.0.1&REQUEST=GetCoverage&COVERAGEID=nw_dgm&FORMAT=image/tiff"
        f"&SUBSET=x({E0 - UM / 2 - ucell / 2:.2f},{E0 + UM / 2 + ucell / 2:.2f})&SUBSET=y({N0 - UM / 2 - ucell / 2:.2f},{N0 + UM / 2 + ucell / 2:.2f})&SCALESIZE=x({URES}),y({URES})")
get(uurl, utif)
UW, UH, uvals, _ = geo.read_tiff_float(utif)
uvals = [v if v > -100 else lo for v in uvals]   # Flaechen ausserhalb NRW (keine Daten) auf Kartenhoehe
with open(os.path.join(OUT, "umland.bytes"), "wb") as f:
    f.write(struct.pack("<iffff", URES, ucell, lo, 0.1, UM))
    for iz in range(URES):
        src = (URES - 1 - iz) * URES
        f.write(struct.pack("<" + "h" * URES, *[max(-32000, min(32000, int(round((uvals[src + ix] - lo) / 0.1)))) for ix in range(URES)]))
print(f"UMLAND {UW}x{UH}, {min(uvals):.0f}..{max(uvals):.0f} m")

# ---------------- OpenStreetMap ----------------
dlat = (H * 1.04) / 111320.0
dlon = (H * 1.04) / (111320.0 * math.cos(math.radians(LAT)))
bb = f"{LAT - dlat:.5f},{LON - dlon:.5f},{LAT + dlat:.5f},{LON + dlon:.5f}"
q = f"""[out:json][timeout:240];
(
 way["highway"]({bb});
 way["building"]({bb}); relation["building"]({bb});
 way["landuse"]({bb}); relation["landuse"]({bb});
 way["natural"]({bb}); relation["natural"]({bb});
 way["waterway"]({bb});
 way["railway"="rail"]({bb});
 way["leisure"]({bb}); relation["leisure"]({bb});
 way["amenity"="parking"]({bb});
 node["place"]({bb});
 node["natural"="peak"]({bb});
);
out geom;"""
osm_path = os.path.join(CACHE, "osm.json")
raw = get("https://overpass-api.de/api/interpreter", osm_path, 400, urllib.parse.urlencode({"data": q}).encode())
osm = json.loads(raw.decode("utf-8"))
els = osm["elements"]
print("OSM Elemente:", len(els))

def xy(lat, lon):
    e, n = geo.latlon_to_utm32(lat, lon)
    return (round(e - E0, 1), round(n - N0, 1))

def pts(geom):
    return [xy(g["lat"], g["lon"]) for g in geom if g]

def fmt(p): return ";".join(f"{x},{z}" for x, z in p)

def inside(p, m=60):
    return any(-H - m <= x <= H + m and -H - m <= z <= H + m for x, z in p)

ROADS = {"motorway": ("a", 12), "trunk": ("a", 10), "primary": ("a", 8.5), "secondary": ("a", 7.5), "tertiary": ("a", 6.5),
         "motorway_link": ("a", 6), "trunk_link": ("a", 6), "primary_link": ("a", 6), "secondary_link": ("a", 6), "tertiary_link": ("a", 5.5),
         "residential": ("a", 5.5), "unclassified": ("a", 5), "living_street": ("a", 4.5), "service": ("a", 3.5), "pedestrian": ("a", 4),
         "track": ("d", 3.2), "path": ("d", 1.4), "footway": ("d", 1.4), "bridleway": ("d", 2), "cycleway": ("a", 2), "steps": ("d", 1.5)}
AREA = {("natural", "wood"): "wald", ("landuse", "forest"): "wald", ("landuse", "farmland"): "acker", ("landuse", "meadow"): "wiese",
        ("landuse", "grass"): "wiese", ("natural", "grassland"): "wiese", ("landuse", "orchard"): "obst", ("landuse", "allotments"): "garten",
        ("natural", "water"): "wasser", ("landuse", "reservoir"): "wasser", ("landuse", "basin"): "wasser", ("waterway", "riverbank"): "wasser",
        ("landuse", "residential"): "wohnen", ("landuse", "commercial"): "gewerbe", ("landuse", "industrial"): "gewerbe", ("landuse", "retail"): "gewerbe",
        ("leisure", "park"): "park", ("leisure", "garden"): "park", ("leisure", "pitch"): "platz", ("leisure", "golf_course"): "wiese",
        ("natural", "scrub"): "gebuesch", ("natural", "heath"): "gebuesch", ("natural", "bare_rock"): "fels", ("natural", "wetland"): "wiese",
        ("landuse", "cemetery"): "park", ("amenity", "parking"): "parkplatz", ("landuse", "quarry"): "fels", ("landuse", "village_green"): "wiese",
        ("leisure", "nature_reserve"): None, ("landuse", "railway"): "gleis", ("landuse", "construction"): "erde", ("landuse", "farmyard"): "hof"}

def level_height(t):
    try:
        if "height" in t: return max(2.5, min(80.0, float(t["height"].replace("m", "").replace(",", ".").strip())))
    except: pass
    try:
        if "building:levels" in t: return max(3.0, float(t["building:levels"]) * 3.0 + 1.5)
    except: pass
    b = t.get("building", "yes")
    return {"garage": 3.0, "garages": 3.0, "shed": 3.0, "carport": 3.0, "hut": 3.0, "roof": 4.0, "industrial": 9.0, "warehouse": 9.0,
            "retail": 6.0, "commercial": 10.0, "apartments": 13.0, "church": 18.0, "chapel": 9.0, "school": 11.0, "hospital": 15.0,
            "farm_auxiliary": 7.0, "barn": 8.0, "hotel": 14.0, "office": 12.0}.get(b, 8.0)

def build_class(t):
    b = t.get("building", "yes")
    if b in ("church", "chapel", "cathedral") or t.get("amenity") == "place_of_worship": return "k"
    if b in ("industrial", "warehouse", "retail", "commercial", "office", "hospital", "school", "supermarket"): return "i"
    if b in ("garage", "garages", "shed", "carport", "hut", "roof"): return "n"
    if b in ("barn", "farm_auxiliary", "stable", "cowshed"): return "s"
    return "h"

lines = []
counts = {}
def add(kind, line):
    lines.append(line); counts[kind] = counts.get(kind, 0) + 1

for e in els:
    t = e.get("tags", {})
    if e["type"] == "node":
        if "place" in t and "name" in t:
            p = xy(e["lat"], e["lon"])
            if abs(p[0]) < H + 1500 and abs(p[1]) < H + 1500: add("N", f"N|{t['name']}|{t['place']}|{p[0]}|{p[1]}")
        elif t.get("natural") == "peak" and "name" in t:
            p = xy(e["lat"], e["lon"])
            if abs(p[0]) < H and abs(p[1]) < H: add("N", f"N|{t['name']}|peak|{p[0]}|{p[1]}")
        continue
    # Geometrie (Wege direkt, Relationen: aeussere Ringe)
    rings = []
    if e["type"] == "way" and "geometry" in e: rings = [pts(e["geometry"])]
    elif e["type"] == "relation":
        for m in e.get("members", []):
            if m.get("type") == "way" and m.get("role") in ("outer", "") and "geometry" in m: rings.append(pts(m["geometry"]))
    rings = [r for r in rings if len(r) >= 2 and inside(r)]
    if not rings: continue
    if "building" in t and t["building"] != "no":
        for r in rings:
            if len(r) >= 4: add("B", f"B|{level_height(t):.1f}|{build_class(t)}|{fmt(r[:-1] if r[0] == r[-1] else r)}")
        continue
    if "highway" in t and e["type"] == "way":
        k = ROADS.get(t["highway"])
        if k:
            kind, w = k
            if t.get("surface") in ("asphalt", "paved", "concrete", "paving_stones", "sett"): kind = "a"
            elif t.get("surface") in ("gravel", "dirt", "ground", "grass", "unpaved", "fine_gravel", "compacted"): kind = "d"
            if t.get("tunnel") == "yes": continue
            add("R", f"R|{kind}|{w}|{1 if t.get('bridge') == 'yes' else 0}|{fmt(rings[0])}")
        continue
    if t.get("railway") == "rail":
        if t.get("tunnel") != "yes": add("R", f"R|r|4.5|{1 if t.get('bridge') == 'yes' else 0}|{fmt(rings[0])}")
        continue
    if "waterway" in t and t["waterway"] in ("river", "stream", "canal", "ditch", "drain") and e["type"] == "way":
        w = {"river": 8, "canal": 8, "stream": 2.5, "ditch": 1.2, "drain": 1.2}[t["waterway"]]
        if t.get("tunnel") not in ("yes", "culvert"): add("R", f"R|w|{w}|0|{fmt(rings[0])}")
        continue
    cls = None
    for key in ("landuse", "natural", "leisure", "amenity", "waterway"):
        if key in t and (key, t[key]) in AREA: cls = AREA[(key, t[key])]; break
    if cls:
        for r in rings:
            if len(r) >= 4: add("P", f"P|{cls}|{fmt(r[:-1] if r[0] == r[-1] else r)}")

with open(os.path.join(OUT, "osm.txt"), "w", encoding="utf-8") as f:
    f.write(f"# {NAME}: Mitte {LAT},{LON} (UTM32 {E0:.1f},{N0:.1f}), Groesse {SIZE:.0f} m. Daten (c) OpenStreetMap-Mitwirkende (ODbL)\n")
    f.write("\n".join(lines))
print("OSM geschrieben:", counts, os.path.getsize(os.path.join(OUT, "osm.txt")) // 1024, "KB")

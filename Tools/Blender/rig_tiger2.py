"""
Riggt "Tank Tiger 2" (maximus0075550, sketchfab.com/maximus0075550, CC BY 4.0) fuer PanzerSim.
Original: Teile nach Material zusammengefasst, Kette als eigene Objekte. Vorne = -Y, links = -X, Meter (etwas zu klein).
  - Kette (Glieder Object_15/19, Bolzen Object_17/21) entfaellt: das Spiel baut eine Gliederkette
  - Raeder (Object_14/16 rechts, Object_18/20 links): Flaechen zur naechsten Radscheibe -> radL0..N / radR0..N (0 = hinten)
  - Turm: Inseln ueber dem Wannendeck im Turm-Grundriss; Rohr + Blende vor der Turmfront -> cannon
Aufruf: blender -b -P rig_tiger2.py -- <tank_tiger_2.glb> <out.glb> [vorschau.png]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
preview = args[2] if len(args) > 2 else None
SCALE = 1.12   # auf echte Groesse (Wanne 7,1 m, Breite 3,76 m, Hoehe 3,08 m)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in tc.meshes():
    if o.parent: mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

TRACK = {"Object_15", "Object_19", "Object_17", "Object_21"}
WHEELS = {"Object_14", "Object_16", "Object_18", "Object_20"}
for nm in TRACK:
    if nm in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[nm])

# ---- Radstationen aus den runden Scheiben ----
discs = []
for nm in WHEELS:
    me = bpy.data.objects[nm].data
    fi, lv, ls, lt = tc.islands(me)
    for mn, mx, n, m in tc.island_stats(me, fi, lv, ls, lt).values():
        s = mx - mn
        if s[1] > 0.45 and abs(s[1] - s[2]) < 0.06 * s[1]:
            discs.append(((mn + mx) / 2, s[1] / 2))
stations = []   # [seite, y, z, r]
for c, r in discs:
    side = -1 if c[0] < 0 else 1
    for st in stations:
        if st[0] == side and abs(st[1] - c[1]) < 0.08 and abs(st[2] - c[2]) < 0.08:
            st[3] = max(st[3], r); break
    else: stations.append([side, c[1], c[2], r])
for side in (-1, 1):
    ss = sorted([s for s in stations if s[0] == side], key=lambda s: -s[1])   # hinten (grosses y) zuerst
    print("T2 Seite", "L" if side < 0 else "R", [(round(s[1], 2), round(s[2], 2), round(s[3], 2)) for s in ss])
L = sorted([s for s in stations if s[0] < 0], key=lambda s: -s[1])
R = sorted([s for s in stations if s[0] > 0], key=lambda s: -s[1])

# ---- alles zusammenlegen, Flaechen einteilen ----
tc.select_only(tc.meshes()); bpy.ops.object.join()
big = bpy.context.view_layer.objects.active
me = big.data
fi, lv, ls, lt = tc.islands(me)
st = tc.island_stats(me, fi, lv, ls, lt)
fc = np.zeros(len(me.polygons) * 3); me.polygons.foreach_get("center", fc); fc = fc.reshape(-1, 3)
mi = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("material_index", mi)
wheel_mats = {k for k, m in enumerate(me.materials) if m and (m.name.endswith(".003") or m.name.endswith(".004"))}

island_group = {}
for i, (mn, mx, n, m) in st.items():
    c = (mn + mx) / 2
    if mx[1] < -1.15 and mn[2] > 1.72 and abs(c[0]) < 0.7:
        island_group[i] = "cannon"
    elif mn[2] >= 1.64 and mx[2] >= 1.80 and abs(mn[0]) <= 1.2 and abs(mx[0]) <= 1.2 and mn[1] >= -1.3 and mx[1] <= 2.1:
        island_group[i] = "turret"

names = ["hull", "turret", "cannon"] + [f"radL{k}" for k in range(len(L))] + [f"radR{k}" for k in range(len(R))] + ["weg"]
idx = {n: k for k, n in enumerate(names)}
# kleine Inseln der Rad-Objekte: nur nahe der Radmitte (Nabe, Bolzen) gehoeren zum Rad, sonst sind es Kettenteile
small_station = {}
for i, (mn, mx, n, m) in st.items():
    if m not in wheel_mats or max(mx - mn) >= 0.25: continue
    c = (mn + mx) / 2
    ss = L if c[0] < 0 else R
    small_station[i] = -1
    for k, s in enumerate(ss):
        if math.hypot(c[1] - s[1], c[2] - s[2]) < 0.75 * s[3]: small_station[i] = k; break
fg = np.zeros(len(fc), dtype=np.int64)
for f in range(len(fc)):
    g = island_group.get(fi[f])
    if g: fg[f] = idx[g]; continue
    if mi[f] in wheel_mats:
        x, y, z = fc[f]
        side = "L" if x < 0 else "R"
        if fi[f] in small_station:
            k = small_station[fi[f]]
            fg[f] = idx[f"rad{side}{k}"] if k >= 0 else idx["weg"]
            continue
        ss = L if x < 0 else R
        best, bd = -1, 1e9
        for k, s in enumerate(ss):
            d = math.hypot(y - s[1], z - s[2])
            if d <= s[3] + 0.015 and d / s[3] < bd: best, bd = k, d / s[3]
        fg[f] = idx[f"rad{side}{best}"] if best >= 0 and z > 0.13 else idx["weg"]   # grosse Kettenteile / Reste weg
from collections import Counter
print("T2 Flaechen:", {names[k]: v for k, v in sorted(Counter(fg.tolist()).items())})

objs = {}
for nm in names:
    if nm == "weg": continue
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if fg[f.index] != idx[nm]], context="FACES")
    nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
    for mat in me.materials: nme.materials.append(mat)
    o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o); objs[nm] = o
bpy.data.objects.remove(big)

def bbox(o):
    co = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    return Vector(co.min(0)), Vector(co.max(0))

if preview:
    for nm, o in objs.items():
        c = (0.9, 0.2, 0.1, 1) if nm == "turret" else (1, 0.9, 0.1, 1) if nm == "cannon" else (0.4, 0.4, 0.4, 1) if nm == "hull" else ((0.1, 0.5, 1, 1) if int(nm[4:]) % 2 == 0 else (0.1, 0.9, 0.4, 1))
        m = bpy.data.materials.new("v" + nm); m.diffuse_color = c; o.data.materials.clear(); o.data.materials.append(m)
    sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 800
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("seite", Vector((-14, -0.8, 1.3))), ("schraeg", Vector((-8, -8, 5)))):
        cam.location = pos; d = Vector((0, -0.8, 1.3)) - pos; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cd.type = "ORTHO" if name == "seite" else "PERSP"; cd.ortho_scale = 10; cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)
    print("T2 Vorschau fertig"); sys.exit(0)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p

# Drehpunkte: Turm = Mitte der Turmunterkante, Rohr = Schildzapfen auf der Rohrachse
tmn, tmx = bbox(objs["turret"]); cmn, cmx = bbox(objs["cannon"])
barrel_z = 2.025; barrel_x = -0.055
TPIV = Vector(((tmn.x + tmx.x) / 2, (tmn.y + tmx.y) / 2 - 0.25, tmn.z))   # Turm ist hinten lang (Staukasten): Ring etwas weiter vorn
GPIV = Vector((barrel_x, cmx.y - 0.05, barrel_z))
set_origin(objs["turret"], TPIV); set_origin(objs["cannon"], GPIV)
for side, ss in (("L", L), ("R", R)):
    for k, s in enumerate(ss):
        o = objs[f"rad{side}{k}"]; mn, mx = bbox(o)
        set_origin(o, Vector(((mn.x + mx.x) / 2, s[1], s[2])))
print("T2 Turm", tuple(round(v * SCALE, 3) for v in TPIV), "Rohr", tuple(round(v * SCALE, 3) for v in GPIV), "Muendung y", round(cmn.y * SCALE, 3))
print("T2 Raeder je Seite", len(L), len(R), "Radien", [round(s[3] * SCALE, 3) for s in L])

# Texturen verkleinern (Original ~88 MB, 13 Bilder): hoechstens 1024, sonst wird das Spiel ~125 MB groesser
for img in bpy.data.images:
    if img.size[0] > 1024: img.scale(1024, 1024 * img.size[1] // img.size[0])

for o in objs.values():
    o.data.transform(Matrix.Scale(SCALE, 4)); o.location = o.location * SCALE
bpy.context.view_layer.update()
objs["cannon"].parent = objs["turret"]; objs["cannon"].matrix_parent_inverse = objs["turret"].matrix_world.inverted()
root = bpy.data.objects.new("TigerII", None); bpy.context.scene.collection.objects.link(root)
for o in objs.values():
    if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
objs["hull"].name = "Wanne"; objs["hull"].data.name = "Wanne"   # keine automatische Zerlegung in Anbauteile
bpy.context.view_layer.update()
mn, mx = Vector((9, 9, 9)), Vector((-9, -9, -9))
for o in objs.values():
    a, b = bbox(o); a = o.matrix_world @ a; b = o.matrix_world @ b
    mn = Vector(map(min, mn, a, b)); mx = Vector(map(max, mx, a, b))
wmn, wmx = bbox(objs["hull"])
print(f"T2 Masse min {tuple(round(v, 2) for v in mn)} max {tuple(round(v, 2) for v in mx)}; Wanne y {wmn.y:.2f}..{wmx.y:.2f} z {wmn.z:.2f}..{wmx.z:.2f}")
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs.values())
print("T2 Dreiecke", tris)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=88)
print("T2 exportiert", out, os.path.getsize(out) // 1024, "KB")

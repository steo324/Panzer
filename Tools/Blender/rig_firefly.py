"""
Riggt "M4 Sherman Firefly" (barking_dogo, sketchfab.com/barking_dogo, CC BY 4.0) fuer PanzerSim.
Original: viele Blender-Teile, vorne = -Y, oben = Z, etwa halbe Groesse (Boden = Kettenunterkante bei z = -0,64).
  - Kette (Material M4_Firefly_Tracks) entfaellt: das Spiel baut eine Gliederkette
  - Laufwerk (Material M4_Firefly_Suspension): Inseln innerhalb der Radscheiben -> radL0..N / radR0..N (0 = hinten), Stuetzrollen rollL/rollR;
    Drehgestelle (VVSS) bleiben an der Wanne
  - Turm: Material M4_Firefly_Tower, MG und Munition auf dem Dach; Kanone = Teil "Cube.081" (17-Pfuender)
  - Texturen auf 512 verkleinert (27 Bilder)
Aufruf: blender -b -P rig_firefly.py -- <m4_sherman_firefly.glb> <out.glb> [vorschau.png]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
preview = args[2] if len(args) > 2 else None
SCALE = 2.0      # Breite 2,64 m, Wanne 6,2 m (Firefly VC auf M4A4)
GROUND = -0.64

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
gun_obj = {o.name for o in bpy.data.objects if o.type == "MESH" and o.parent and o.parent.name.startswith("Cube.081")}
for o in tc.meshes():
    if o.parent: mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(tc.meshes()):
    if o.data.materials and o.data.materials[0].name == "M4_Firefly_Tracks": bpy.data.objects.remove(o)
# Kanone markieren (eigenes Material-Slot-Duplikat waere umstaendlich): Flaechen ueber ein Attribut
for o in tc.meshes():
    a = o.data.attributes.new("istgun", "INT", "FACE")
    a.data.foreach_set("value", [1 if o.name in gun_obj else 0] * len(o.data.polygons))
tc.select_only(tc.meshes()); bpy.ops.object.join()
big = bpy.context.view_layer.objects.active; me = big.data
me.transform(Matrix.Translation(Vector((0, 0, -GROUND))))

fi, lv, ls, lt = tc.islands(me)
st = tc.island_stats(me, fi, lv, ls, lt)
mi = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("material_index", mi)
isgun = np.zeros(len(me.polygons), dtype=np.int64); me.attributes["istgun"].data.foreach_get("value", isgun)
mats = [m.name if m else "" for m in me.materials]
susp = {k for k, n in enumerate(mats) if n.startswith(("M4_Firefly_Suspension", "M4_Firefly_Body_2"))}   # Antriebs- und Leitrad haben das Wannenmaterial
turm = {k for k, n in enumerate(mats) if n.startswith(("M4_Firefly_Tower", "Bullet_Firefly", "MG_Firefly"))}

# ---- Radscheiben -> Stationen ----
discs = []
for i, (mn, mx, n, m) in st.items():
    s = mx - mn; c = (mn + mx) / 2
    if m in susp and s[1] > 0.1 and abs(s[1] - s[2]) < 0.08 * s[1] and s[0] < 0.2 and abs(c[0]) > 0.3:
        discs.append((c, s[1] / 2, mn[0], mx[0]))
stations = []
for c, r, x0, x1 in sorted(discs, key=lambda d: -d[1]):
    side = -1 if c[0] < 0 else 1
    for sta in stations:
        if sta[0] == side and math.hypot(sta[1] - c[1], sta[2] - c[2]) < 0.8 * sta[3]:
            sta[4] = min(sta[4], x0); sta[5] = max(sta[5], x1); break
    else: stations.append([side, c[1], c[2], r, x0, x1])
for s in sorted(stations, key=lambda s: (s[0], s[1])): print("FF Scheibe", s[0], round(s[1], 3), round(s[2], 3), "r", round(s[3], 3))
road_r = max(s[3] for s in stations if s[2] < 0.25)          # Laufrollen: unten
road = [s for s in stations if s[2] < 0.25 and abs(s[3] - road_r) < 0.02]
ry0, ry1 = min(s[1] for s in road), max(s[1] for s in road)
zroad = max(s[2] for s in road)
ends_f = [s for s in stations if s[1] < ry0 - 0.05 and s[3] > 0.08]
ends_r = [s for s in stations if s[1] > ry1 + 0.05 and s[3] > 0.08]
rollers = [s for s in stations if ry0 - 0.05 < s[1] < ry1 + 0.05 and zroad + 0.12 < s[2] < zroad + 0.4 and s[3] < 0.08]   # (r 0,1: Federgehaeuse der Drehgestelle)
W, R = {}, {}
for side, sn in ((-1, "L"), (1, "R")):
    ws = sorted([s for s in road if s[0] == side], key=lambda s: -s[1])
    rear = max([s for s in ends_r if s[0] == side], key=lambda s: s[3])
    front = max([s for s in ends_f if s[0] == side], key=lambda s: s[3])
    W[sn] = [rear] + ws + [front]
    R[sn] = sorted([s for s in rollers if s[0] == side], key=lambda s: -s[1])
    print(f"FF {sn} Raeder", [(round(s[1], 2), round(s[2], 2), round(s[3], 3)) for s in W[sn]], "Stuetzrollen", [(round(s[1], 2), round(s[2], 2), round(s[3], 3)) for s in R[sn]])

names = ["Wanne", "turret", "cannon"]; idx = {}
for sn in "LR":
    for k, s in enumerate(W[sn]): idx[id(s)] = len(names); names.append(f"rad{sn}{k}")
    for k, s in enumerate(R[sn]): idx[id(s)] = len(names); names.append(f"roll{sn}{k + 1}")
allst = [s for sn in "LR" for s in W[sn] + R[sn]]
island_to = {}
for i, (mn, mx, n, m) in st.items():
    if m not in susp: continue
    c = (mn + mx) / 2; ext = max(mx[1] - mn[1], mx[2] - mn[2]) / 2
    for s in allst:
        if s[4] - 0.05 <= c[0] <= s[5] + 0.05 and math.hypot(c[1] - s[1], c[2] - s[2]) + ext <= s[3] * 1.1 + 0.005:
            island_to[i] = idx[id(s)]; break
fg = np.zeros(len(me.polygons), dtype=np.int64)
for f in range(len(fg)):
    if isgun[f]: fg[f] = 2
    elif mi[f] in turm: fg[f] = 1
    else: fg[f] = island_to.get(fi[f], 0)
from collections import Counter
print("FF Flaechen:", {names[k]: v for k, v in sorted(Counter(fg.tolist()).items())})

objs = {}
for k, nm in enumerate(names):
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if fg[f.index] != k], context="FACES")
    nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
    for mat in me.materials: nme.materials.append(mat)
    if "istgun" in nme.attributes: nme.attributes.remove(nme.attributes["istgun"])
    o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o); objs[nm] = o
bpy.data.objects.remove(big)

def bbox(o):
    co = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    return Vector(co.min(0)), Vector(co.max(0)), co

if preview:
    for nm, o in objs.items():
        c = (0.9, 0.2, 0.1, 1) if nm == "turret" else (1, 0.9, 0.1, 1) if nm == "cannon" else (0.4, 0.4, 0.4, 1) if nm == "Wanne" else (0.9, 0.3, 0.9, 1) if nm.startswith("roll") else ((0.1, 0.5, 1, 1) if int(nm[4:]) % 2 == 0 else (0.1, 0.9, 0.4, 1))
        mat = bpy.data.materials.new("v" + nm); mat.diffuse_color = c; o.data.materials.clear(); o.data.materials.append(mat)
    sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 800
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("seite", Vector((-8, 0, 0.6))), ("schraeg", Vector((-4, -4, 2.5)))):
        cam.location = pos; d = Vector((0, 0, 0.6)) - pos; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cd.type = "ORTHO" if name == "seite" else "PERSP"; cd.ortho_scale = 4.5; cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)
    sys.exit(0)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p

# Rohrachse aus dem vorderen Rohrstueck, Schildzapfen an der Turmfront; Turmgelenk = Mitte des Turmrings (Teil Cylinder.668, z ~0,93)
cmn, cmx, cco = bbox(objs["cannon"]); tmn, tmx, tco = bbox(objs["turret"])
tip = cco[cco[:, 1] < cmn.y + 0.6]
bx, bz = float(np.median(tip[:, 0])), float((tip[:, 2].min() + tip[:, 2].max()) / 2)
ring = tco[np.abs(tco[:, 2] - (0.29 - GROUND)) < 0.01]
TPIV = Vector(((ring[:, 0].min() + ring[:, 0].max()) / 2, (ring[:, 1].min() + ring[:, 1].max()) / 2, ring[:, 2].min()))
front = tco[(np.abs(tco[:, 0] - bx) < 0.3) & (np.abs(tco[:, 2] - bz) < 0.2)]
GPIV = Vector((bx, float(front[:, 1].min()) + 0.12, bz))
set_origin(objs["turret"], TPIV); set_origin(objs["cannon"], GPIV)
for sn in "LR":
    for k, s in enumerate(W[sn]): set_origin(objs[f"rad{sn}{k}"], Vector(((s[4] + s[5]) / 2, s[1], s[2])))
    for k, s in enumerate(R[sn]): set_origin(objs[f"roll{sn}{k + 1}"], Vector(((s[4] + s[5]) / 2, s[1], s[2])))

for img in bpy.data.images:
    if img.size[0] > 512: img.scale(512, 512 * img.size[1] // img.size[0])   # 27 Bilder: sonst ~40 MB mehr im Spiel
# Original ~200 000 Dreiecke (MG, Granaten, Schrauben sehr fein): auf gut ein Drittel reduzieren
for nm, o in objs.items():
    md = o.modifiers.new("dec", "DECIMATE"); md.ratio = 0.6 if nm == "cannon" else 0.35; md.use_collapse_triangulate = True
    tc.select_only([o]); bpy.ops.object.modifier_apply(modifier="dec")
for o in objs.values():
    o.data.transform(Matrix.Scale(SCALE, 4)); o.location = o.location * SCALE
bpy.context.view_layer.update()
objs["cannon"].parent = objs["turret"]; objs["cannon"].matrix_parent_inverse = objs["turret"].matrix_world.inverted()
root = bpy.data.objects.new("Firefly", None); bpy.context.scene.collection.objects.link(root)
for o in objs.values():
    if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
bpy.context.view_layer.update()
wmn, wmx, _ = bbox(objs["Wanne"])
print(f"FF Wanne x {wmn.x:.2f}..{wmx.x:.2f} y {wmn.y:.2f}..{wmx.y:.2f} z {wmn.z:.2f}..{wmx.z:.2f}")
print("FF Turm", tuple(round(v * SCALE, 3) for v in TPIV), "Rohr", tuple(round(v * SCALE, 3) for v in GPIV), "Muendung y", round(cmn.y * SCALE, 3))
print("FF Raeder je Seite", len(W["L"]), "r", [round(s[3] * SCALE, 3) for s in W["L"]], "x", round((W["L"][1][4] + W["L"][1][5]) / 2 * SCALE, 3),
      "Stuetzrollen", len(R["L"]), [round(s[3] * SCALE, 3) for s in R["L"]])
print("FF Dreiecke", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs.values()))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=88)
print("FF exportiert", out, os.path.getsize(out) // 1024, "KB")

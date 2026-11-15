"""
Riggt "Jagdpanzer 38(t) Hetzer" (Pechacek, sketchfab.com/pechy.v, CC BY 4.0) fuer PanzerSim.
Original: Sketchfab-Export in cm, vorne = +X, um ~21 Grad um die Hochachse verdreht; 10 Teile mit 4 Materialien
(Hull, Machinegun, Tracks, Wheels), ~293 000 Dreiecke, 16 Texturen (4K/2K).
  - Kette (Material Tracks, unten) entfaellt: das Spiel baut eine Gliederkette. Ersatzglieder auf der Wanne bleiben.
  - Laufwerk (Material Wheels): Leitrad hinten (radL0), 4 grosse Laufrollen, Antriebsrad vorne (radL5)
  - Kein Turm: "turret" ist nur ein Drehpunkt (Kanone schwenkt begrenzt in der Kasematte),
    "cannon" = Rohr (Teil Object_4) + Saukopfblende
  - Texturen verkleinert (Wanne 2048, Rest 1024; Normalen halb so gross), Metall/Rauheit entfaellt (Spiel macht den Lack matt)
Konvention wie die anderen Rigs: vorne = -Y, oben = Z, Boden bei z = 0.
Aufruf: blender -b -P rig_hetzer.py -- <jagdpanzer_38t_hetzer.glb> <out.glb>
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
root0 = bpy.data.objects["Sketchfab_model"]
root0.rotation_mode = "XYZ"; e = root0.rotation_euler.copy(); e.z = 0.0; root0.rotation_euler = e   # Verdrehung um die Hochachse weg
bpy.context.view_layer.update()
for o in tc.meshes():
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
M = Matrix.Rotation(math.radians(-90), 4, "Z") @ Matrix.Scale(0.01, 4)      # cm -> m, vorne +X -> -Y
for o in tc.meshes():
    o.data.transform(M)
    a = o.data.attributes.new("teil", "INT", "FACE")
    a.data.foreach_set("value", [3 if o.name == "Object_4" else 0] * len(o.data.polygons))
tc.select_only(tc.meshes()); bpy.ops.object.join()
big = bpy.context.view_layer.objects.active; me = big.data
mats = [m.name if m else "" for m in me.materials]
mi = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("material_index", mi)
teil = np.zeros(len(me.polygons), dtype=np.int64); me.attributes["teil"].data.foreach_get("value", teil)
TRACK = mats.index("Tracks"); WHEEL = mats.index("Wheels")
co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
GROUND = float(co[:, 2].min())
me.transform(Matrix.Translation(Vector((0, 0, -GROUND)))); co[:, 2] -= GROUND
fc = np.zeros(len(me.polygons) * 3); me.polygons.foreach_get("center", fc); fc = fc.reshape(-1, 3)
fv = [tuple(p.vertices) for p in me.polygons]

# ---- Laufwerk: Mitten an den Radscheiben nachmessen (Startwerte aus der Seitenansicht) ----
wv = set()
for f in np.nonzero(mi == WHEEL)[0]: wv.update(fv[f])
wv = co[sorted(wv)]
START = [(2.07, 0.65, 0.29)] + [(y, 0.46, 0.405) for y in (1.523, 0.514, -0.495, -1.504)] + [(-2.08, 0.72, 0.30)]   # (y, z, r) - r fest
ST = {}
for sn, sgn in (("L", -1), ("R", 1)):
    side = wv[(wv[:, 0] * sgn) > 0.6]
    lst = []
    for y0, z0, r0 in START:
        for _ in range(3):
            d = np.hypot(side[:, 1] - y0, side[:, 2] - z0)
            v = side[d < r0 * 1.02]
            y0, z0 = (v[:, 1].min() + v[:, 1].max()) / 2, (v[:, 2].min() + v[:, 2].max()) / 2
        lst.append([float(y0), float(z0), float(r0), float(v[:, 0].min()), float(v[:, 0].max())])
    ST[sn] = lst
    print("HZ", sn, "Raeder", [(round(s[0], 3), round(s[1], 3), round(s[2], 3), round((s[3] + s[4]) / 2, 3)) for s in lst])

# ---- Rohrachse aus dem vorderen Rohr (Teil Object_4) ----
gv = set()
for f in np.nonzero(teil == 3)[0]: gv.update(fv[f])
gv = co[sorted(gv)]
tip = gv[gv[:, 1] < gv[:, 1].min() + 0.8]
AX, AZ = float((tip[:, 0].min() + tip[:, 0].max()) / 2), float((tip[:, 2].min() + tip[:, 2].max()) / 2)
print(f"HZ Rohrachse x {AX:.3f} z {AZ:.3f}, Muendung y {gv[:, 1].min():.2f}")

# ---- Saukopfblende: Wannen-Inseln ganz innerhalb eines Zylinders um die Rohrachse, vorne ----
fi, lv, ls, lt = tc.islands(me)
st = tc.island_stats(me, fi, lv, ls, lt)
fi_arr = np.array([fi[f] for f in range(len(fv))])
blende = set()
for i, (mn, mx, n, m) in st.items():
    if m != mats.index("Hull"): continue
    corners = [(x, z) for x in (mn[0], mx[0]) for z in (mn[2], mx[2])]
    if max(math.hypot(x - AX, z - AZ) for x, z in corners) < 0.40 and (mn[1] + mx[1]) / 2 < -1.35:
        blende.add(i)
isbl = np.isin(fi_arr, list(blende)) & (mi == mats.index("Hull"))
bv = set()
for f in np.nonzero(isbl & (teil != 3))[0]: bv.update(fv[f])
bv = co[sorted(bv)] if bv else gv
print(f"HZ Blende: {len(blende)} Teile, y {bv[:, 1].min():.2f}..{bv[:, 1].max():.2f}")

# ---- Flaechen verteilen ----
names = ["Wanne", "cannon"] + [f"rad{sn}{k}" for sn in "LR" for k in range(len(START))]
idx = {n: k for k, n in enumerate(names)}
fg = np.full(len(fv), -1, dtype=np.int64)
for f in range(len(fv)):
    x, y, z = fc[f]
    if mi[f] == TRACK:
        fg[f] = -1 if z < 1.1 else 0          # Kette weg, Ersatzglieder oben bleiben
    elif teil[f] == 3 or isbl[f]:
        fg[f] = 1
    elif mi[f] == WHEEL:
        sn = "L" if x < 0 else "R"; best = 0
        for k, (wy, wz, wr, x0, x1) in enumerate(ST[sn]):
            if x0 - 0.03 <= x <= x1 + 0.03 and math.hypot(y - wy, z - wz) <= wr * 1.06 + 0.01:
                best = idx[f"rad{sn}{k}"]; break
        fg[f] = best
    else:
        fg[f] = 0
from collections import Counter
print("HZ Flaechen:", {("weg" if k < 0 else names[k]): v for k, v in sorted(Counter(fg.tolist()).items())})

objs = {}
for k, nm in enumerate(names):
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if fg[f.index] != k], context="FACES")
    nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
    for mat in me.materials: nme.materials.append(mat)
    if "teil" in nme.attributes: nme.attributes.remove(nme.attributes["teil"])
    o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o); objs[nm] = o
bpy.data.objects.remove(big)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p

# Schildzapfen: Kugelteil der Blende (hinteres Drittel), auf der Rohrachse
GPIV = Vector((AX, float(bv[:, 1].max() - (bv[:, 1].max() - bv[:, 1].min()) * 0.3), AZ))
set_origin(objs["cannon"], GPIV)
for sn in "LR":
    for k, (wy, wz, wr, x0, x1) in enumerate(ST[sn]): set_origin(objs[f"rad{sn}{k}"], Vector(((x0 + x1) / 2, wy, wz)))
turret = bpy.data.objects.new("turret", None); bpy.context.scene.collection.objects.link(turret); turret.location = GPIV

# ---- Materialien undurchsichtig, Texturen verkleinern ----
for mat in bpy.data.materials:
    if not mat.use_nodes: continue
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type == "BSDF_PRINCIPLED":
            for inp in ("Alpha", "Metallic", "Roughness"):
                for l in list(n.inputs[inp].links): nt.links.remove(l)
            n.inputs["Alpha"].default_value = 1.0; n.inputs["Metallic"].default_value = 0.0; n.inputs["Roughness"].default_value = 0.85
    for n in list(nt.nodes):   # lose Bildknoten (Metall/Rauheit, Verdeckung) entfernen
        if n.type == "TEX_IMAGE" and not any(l.to_node for o in n.outputs for l in o.links if l.to_node.type in ("BSDF_PRINCIPLED", "NORMAL_MAP")):
            nt.nodes.remove(n)
    try: mat.surface_render_method = "DITHERED"
    except Exception: pass
    try: mat.blend_method = "OPAQUE"
    except Exception: pass
used = {}
for mat in bpy.data.materials:
    if not mat.use_nodes: continue
    for n in mat.node_tree.nodes:
        if n.type == "TEX_IMAGE" and n.image:
            isnormal = any(l.to_node.type == "NORMAL_MAP" for o in n.outputs for l in o.links)
            base = 2048 if mat.name == "Hull" else 1024
            used[n.image.name] = base // 2 if isnormal else base
for img in bpy.data.images:
    s = used.get(img.name)
    if s and img.size[0] > s: img.scale(s, s)
print("HZ Bilder:", {k: v for k, v in used.items()})

# ---- weniger Dreiecke ----
for nm, o in objs.items():
    md = o.modifiers.new("dec", "DECIMATE"); md.ratio = 0.6 if nm == "cannon" else 0.30 if nm.startswith("rad") else 0.40
    md.use_collapse_triangulate = True
    tc.select_only([o]); bpy.ops.object.modifier_apply(modifier="dec")

# ---- Hierarchie, Export ----
bpy.context.view_layer.update()
objs["cannon"].parent = turret; objs["cannon"].matrix_parent_inverse = turret.matrix_world.inverted()
root = bpy.data.objects.new("Hetzer", None); bpy.context.scene.collection.objects.link(root)
for o in list(objs.values()) + [turret]:
    if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
bpy.context.view_layer.update()
w = np.array([v.co[:] for v in objs["Wanne"].data.vertices])
print(f"HZ Wanne x {w[:, 0].min():.2f}..{w[:, 0].max():.2f} y {w[:, 1].min():.2f}..{w[:, 1].max():.2f} z {w[:, 2].min():.2f}..{w[:, 2].max():.2f}")
print("HZ Drehpunkt Kanone", tuple(round(v, 3) for v in GPIV))
print("HZ Dreiecke", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs.values()))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=88)
print("HZ exportiert", out, os.path.getsize(out) // 1024, "KB")

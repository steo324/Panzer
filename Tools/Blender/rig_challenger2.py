"""
Riggt "Challenger II" (buh, sketchfab.com/buh-late, CC BY 4.0) fuer PanzerSim.
Original: ein Netz fuer Wanne+Turm (zwei Materialien, nicht sauber getrennt) und die Kette; echte Groesse, vorne = -Y,
Boden (Kettenunterkante) bei z = -0,99.
  - Kette entfaellt: das Spiel baut eine Gliederkette
  - Turm: Inseln oberhalb des Wannendecks im Turm-Grundriss; Kanone: Rohr (Teile auf der Rohrachse) + Blende vor der Turmfront
  - Laufrollen: Flaechen innerhalb der Radscheibe (Reifen haengen am Schwingarm); Antriebsrad hinten, Leitrad vorne, 3 Stuetzrollen
  - Texturen auf 1024 verkleinert, Dreiecke etwas reduziert
Aufruf: blender -b -P rig_challenger2.py -- <challenger_ii.glb> <out.glb> [vorschau.png]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
preview = args[2] if len(args) > 2 else None
GROUND = -0.99
DECK = 0.60                 # Unterkante Turm (Original-z)
TURRET_Y = (-2.95, 2.45)    # Turm-Grundriss in y (Original)
TURRET_X = 1.62
MANTLET_Y = -2.55           # Inseln, die ganz vor dieser Linie liegen und nahe der Rohrachse sind: Kanone
AXIS_Z = 0.95
# Laufwerk (Original-Koordinaten, je Seite gespiegelt): y, z, r
# Schwingarm-Drehpunkte (Naben am Wannenrand); die Rollenmitte liegt 0,29 m dahinter und 0,16 m tiefer (gemessen)
ROAD = [(y + 0.287, z - 0.16) for y, z in [(-2.72, -0.40), (-1.76, -0.40), (-0.80, -0.40), (0.17, -0.40), (1.13, -0.40), (2.09, -0.40)]]
ROAD_R = 0.37   # Reifen aussen 0,352 m + Toleranz
SPROCKET = (3.18, -0.16, 0.30)   # hinten
IDLER = (-3.23, -0.10, 0.27)     # vorne
ROLLERS = [(1.83, 0.0), (-0.03, 0.0), (-1.89, 0.0)]
ROLLER_R = 0.14
WHEEL_X = (0.70, 1.75)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in tc.meshes():
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(tc.meshes()):
    if o.data.materials and o.data.materials[0].name == "truck": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.join()
big = bpy.context.view_layer.objects.active; me = big.data

fi, lv, ls, lt = tc.islands(me)
st = tc.island_stats(me, fi, lv, ls, lt)
co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)


# Stationen: (Name, y, z, r) je Seite; rad0 = hinten
def stations(sn):
    W = [(SPROCKET[0], SPROCKET[1], SPROCKET[2])] + [(y, z, ROAD_R) for y, z in sorted(ROAD, key=lambda s: -s[0])] + [(IDLER[0], IDLER[1], IDLER[2])]
    R = [(y, z, ROLLER_R) for y, z in sorted(ROLLERS, key=lambda s: -s[0])]
    return [(f"rad{sn}{k}", *w) for k, w in enumerate(W)] + [(f"roll{sn}{k + 1}", *r) for k, r in enumerate(R)]
STA = {"L": stations("L"), "R": stations("R")}
names = ["Wanne", "turret", "cannon"] + [s[0] for sn in "LR" for s in STA[sn]]
idx = {n: k for k, n in enumerate(names)}

island_group = {}
for i, (mn, mx, n, m) in st.items():
    c = (mn + mx) / 2
    near_axis = abs(c[0]) < 0.5 and abs(c[2] - AXIS_Z) < 0.45
    if mx[1] <= MANTLET_Y and near_axis: island_group[i] = "cannon"
    elif mn[2] >= DECK and TURRET_Y[0] <= mn[1] and mx[1] <= TURRET_Y[1] and abs(mn[0]) <= TURRET_X and abs(mx[0]) <= TURRET_X: island_group[i] = "turret"

fg = np.zeros(len(me.polygons), dtype=np.int64)
for f, p in enumerate(me.polygons):
    g = island_group.get(fi[f])
    if g: fg[f] = idx[g]; continue
    vs = co[list(p.vertices)]
    ax = np.abs(vs[:, 0])
    if ax.min() < WHEEL_X[0] or ax.max() > WHEEL_X[1]: continue
    sn = "L" if vs[:, 0].mean() < 0 else "R"
    for nm, y, z, r in STA[sn]:
        if (np.hypot(vs[:, 1] - y, vs[:, 2] - z) <= r).all(): fg[f] = idx[nm]; break
from collections import Counter
print("C2 Flaechen:", {names[k]: v for k, v in sorted(Counter(fg.tolist()).items())})

objs = {}
for k, nm in enumerate(names):
    if not (fg == k).any(): print("C2 FEHLT", nm); continue
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if fg[f.index] != k], context="FACES")
    nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
    for mat in me.materials: nme.materials.append(mat)
    o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o); objs[nm] = o
bpy.data.objects.remove(big)
for o in objs.values(): o.data.transform(Matrix.Translation(Vector((0, 0, -GROUND))))

if preview:
    for nm, o in objs.items():
        c = (0.9, 0.2, 0.1, 1) if nm == "turret" else (1, 0.9, 0.1, 1) if nm == "cannon" else (0.4, 0.4, 0.4, 1) if nm == "Wanne" else (0.9, 0.3, 0.9, 1) if nm.startswith("roll") else ((0.1, 0.5, 1, 1) if int(nm[4:]) % 2 == 0 else (0.1, 0.9, 0.4, 1))
        mat = bpy.data.materials.new("v" + nm); mat.diffuse_color = c; o.data.materials.clear(); o.data.materials.append(mat)
    sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 800
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("seite", Vector((-18, -1, 1.5))), ("schraeg", Vector((-9, -10, 6)))):
        cam.location = pos; d = Vector((0, -1, 1.5)) - pos; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cd.type = "ORTHO" if name == "seite" else "PERSP"; cd.ortho_scale = 13; cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)
    # Nahaufnahme einer Laufrolle (1 px = 1 mm, Bildmitte = Rollenmitte rad L3)
    y3, z3 = ROAD[2]
    cam.location = Vector((-6, y3, z3 - GROUND)); cam.rotation_euler = (math.pi / 2, 0, -math.pi / 2); cd.type = "ORTHO"; cd.ortho_scale = 1.2
    sc.render.resolution_x = 1200; sc.render.resolution_y = 1200
    sc.render.filepath = preview.replace(".png", "_nah.png"); bpy.ops.render.render(write_still=True)
    sys.exit(0)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p
def bbox_co(o):
    a = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", a); return a.reshape(-1, 3)

for sn in "LR":
    xs = -1 if sn == "L" else 1
    for nm, y, z, r in STA[sn]:
        if nm in objs:
            c = bbox_co(objs[nm]); set_origin(objs[nm], Vector(((c[:, 0].min() + c[:, 0].max()) / 2, y, z - GROUND)))
tco = bbox_co(objs["turret"]); low = tco[tco[:, 2] < tco[:, 2].min() + 0.08]
TPIV = Vector(((low[:, 0].min() + low[:, 0].max()) / 2, (low[:, 1].min() + low[:, 1].max()) / 2, tco[:, 2].min()))
gco = bbox_co(objs["cannon"]); tip = gco[gco[:, 1] < gco[:, 1].min() + 0.6]
bx, bz = float((tip[:, 0].min() + tip[:, 0].max()) / 2), float((tip[:, 2].min() + tip[:, 2].max()) / 2)
GPIV = Vector((bx, MANTLET_Y + 0.9, bz))      # Schildzapfen ~0,9 m hinter der Blendenfront
set_origin(objs["turret"], TPIV); set_origin(objs["cannon"], GPIV)

for img in bpy.data.images:
    if img.size[0] > 1024: img.scale(1024, 1024 * img.size[1] // img.size[0])
for nm, o in objs.items():
    if nm in ("Wanne", "turret"):
        md = o.modifiers.new("dec", "DECIMATE"); md.ratio = 0.6; md.use_collapse_triangulate = True
        tc.select_only([o]); bpy.ops.object.modifier_apply(modifier="dec")
bpy.context.view_layer.update()
objs["cannon"].parent = objs["turret"]; objs["cannon"].matrix_parent_inverse = objs["turret"].matrix_world.inverted()
root = bpy.data.objects.new("Challenger2", None); bpy.context.scene.collection.objects.link(root)
for o in objs.values():
    if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
bpy.context.view_layer.update()
wco = bbox_co(objs["Wanne"])
print(f"C2 Wanne min {wco.min(0).round(2)} max {wco.max(0).round(2)}; Turm {tuple(round(v, 3) for v in TPIV)} Rohr {tuple(round(v, 3) for v in GPIV)} Muendung y {gco[:, 1].min():.2f}")
r1 = bbox_co(objs["radL1"]); print("C2 Laufrolle x", round(float((r1[:, 0].min() + r1[:, 0].max()) / 2), 3))
print("C2 Dreiecke", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs.values()))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=88)
print("C2 exportiert", out, os.path.getsize(out) // 1024, "KB")

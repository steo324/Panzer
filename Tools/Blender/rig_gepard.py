"""
Riggt "Flakpanzer Gepard - Animated Low Poly" (xFORTUNAx, sketchfab.com/xFORTUNAx, CC BY 4.0) fuer PanzerSim.
Original: echte Masse (m), vorne = +Y, Teile mit Drehpunkten (PIV_turret > PIV_cradle / PIV_radar), Laufwerk und Kette in einem Stueck.
  - Wanne (body_hull, body_misc, body_slopes), Turm "turret" (turret_main + Antennen), "cannon" = beide 35-mm-Kanonen samt Gehaeusen
    (heben sich zusammen), "radar" = Suchradar hinten auf dem Turm (dreht sich im Spiel dauernd)
  - Laufwerk: Antriebsrad hinten (radL0), 7 Laufrollen, Leitrad vorne (radL8); Kette entfaellt (Gliederkette im Spiel)
  - Animation entfaellt (das Spiel bewegt Turm, Kanonen und Radar selbst)
Konvention wie die anderen Rigs: vorne = -Y, oben = Z, Boden bei z = 0.
Aufruf: blender -b -P rig_gepard.py -- <flakpanzer_gepard.glb> <out.glb>
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
bpy.context.view_layer.update()
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
# Gruppe je Mesh aus dem Eltern-Knoten
def group_of(o):
    p = o.parent
    while p is not None:
        n = p.name
        if n.startswith("cradle_guns") or n.startswith("PIV_cradle"): return "cannon"
        if n.startswith("radar_antenna") or n.startswith("PIV_radar"): return "radar"
        if n.startswith("turret_") or n.startswith("PIV_turret"): return "turret"
        if n.startswith("body_run"): return "run"
        if n.startswith("body_"): return "Wanne"
        p = p.parent
    return "Wanne"
PIV = {}
for o in bpy.data.objects:
    for key, nm in (("PIV_turret", "turret"), ("PIV_cradle", "cannon"), ("PIV_radar", "radar")):
        if o.name.startswith(key): PIV[nm] = o.matrix_world.translation.copy()
groups = {o.name: group_of(o) for o in tc.meshes()}
for o in tc.meshes():
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
R = Matrix.Rotation(math.pi, 4, "Z")   # vorne +Y -> -Y
for o in tc.meshes(): o.data.transform(R)
PIV = {k: R @ v for k, v in PIV.items()}
print("GP Drehpunkte", {k: tuple(round(c, 3) for c in v) for k, v in PIV.items()})

# Raeder (gemessen an den Radscheiben, nach der Drehung): (y, z, r); 0 = hinten (Antriebsrad), zuletzt Leitrad vorne
WHEELS = [(2.955, 0.708, 0.245 * 1.22)] + [(-y, 0.394, 0.339) for y in (-2.07, -1.399, -0.729, -0.058, 0.613, 1.284, 1.955)] + [(-2.67, 0.639, 0.285 * 1.08)]
XW = (1.04, 1.62)

def join(objs, name):
    tc.select_only(objs); bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name; return o
byg = {}
for o in tc.meshes(): byg.setdefault(groups[o.name], []).append(o)
objs = {g: join(lst, g) for g, lst in byg.items() if g != "run"}
run = join(byg["run"], "run")

# Laufwerk: Flaechen ganz innerhalb eines Rades -> Rad; der Rest (Kette, Kleinteile) entfaellt
me = run.data
co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
fg = np.full(len(me.polygons), -1, dtype=np.int64)
names = [f"rad{s}{k}" for s in "LR" for k in range(len(WHEELS))]
for f, p in enumerate(me.polygons):
    v = co[list(p.vertices)]; ax = np.abs(v[:, 0])
    if ax.min() < XW[0] or ax.max() > XW[1]: continue
    s = "L" if v[:, 0].mean() > 0 else "R"      # vorne -Y: links = +X
    for k, (wy, wz, wr) in enumerate(WHEELS):
        if np.hypot(v[:, 1] - wy, v[:, 2] - wz).max() <= wr * 1.03 + 0.01:
            fg[f] = names.index(f"rad{s}{k}"); break
from collections import Counter
print("GP Raeder:", {("weg" if k < 0 else names[k]): n for k, n in sorted(Counter(fg.tolist()).items())})
for k, nm in enumerate(names):
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if fg[f.index] != k], context="FACES")
    nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
    for mat in me.materials: nme.materials.append(mat)
    o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o); objs[nm] = o
bpy.data.objects.remove(run)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p
def bbox(o):
    c = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", c); c = c.reshape(-1, 3)
    return c.min(0), c.max(0)
for nm in ("turret", "cannon", "radar"): set_origin(objs[nm], PIV[nm])
for s in "LR":
    for k, (wy, wz, wr) in enumerate(WHEELS):
        o = objs[f"rad{s}{k}"]; mn, mx = bbox(o); set_origin(o, Vector(((mn[0] + mx[0]) / 2, wy, wz)))
# Rohre: Muendungen (fuer TankSpec)
cv = np.array([objs["cannon"].matrix_world @ v.co for v in objs["cannon"].data.vertices])
tip = cv[cv[:, 1] < cv[:, 1].min() + 0.05]
print(f"GP Muendung y {cv[:, 1].min():.3f}, x {tip[:, 0].min():.2f}..{tip[:, 0].max():.2f}, z {tip[:, 2].mean():.3f}")

# Hierarchie, Export
bpy.context.view_layer.update()
def parent(ch, pa): ch.parent = pa; ch.matrix_parent_inverse = pa.matrix_world.inverted()
parent(objs["cannon"], objs["turret"]); parent(objs["radar"], objs["turret"])
root = bpy.data.objects.new("Gepard", None); bpy.context.scene.collection.objects.link(root)
for o in objs.values():
    if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
w = np.array([v.co[:] for v in objs["Wanne"].data.vertices])
print(f"GP Wanne x {w[:, 0].min():.2f}..{w[:, 0].max():.2f} y {w[:, 1].min():.2f}..{w[:, 1].max():.2f} z {w[:, 2].min():.2f}..{w[:, 2].max():.2f}")
print("GP Dreiecke", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs.values()))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=90, export_animations=False)
print("GP exportiert", out, os.path.getsize(out) // 1024, "KB")

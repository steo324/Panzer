"""
Riggt "Leopard 2 A6" (buh, sketchfab.com/buh-late, CC BY 4.0) fuer PanzerSim.
Original: Teile einzeln (body, turret, gun, Raeder), echte Groesse, vorne = +Y, Boden (Kettenunterkante) bei z = -1,08.
  - gedreht auf vorne = -Y wie die anderen Modelle, Boden auf z = 0
  - Kette (track*) entfaellt: das Spiel baut eine Gliederkette
  - Laufrollen ("op wheel"): nur die runde Scheibe dreht; Schwingarm bleibt an der Wanne
  - Antriebsrad hinten ("w wheel") = radL0, 7 Laufrollen, Leitrad vorne ("n wheel") = radL8; Stuetzrollen ("p wheel") = rollL1..4
  - Texturen 4096 -> 1024 (der Autor empfiehlt selbst kleinere Texturen fuer Spiele)
Aufruf: blender -b -P rig_leopard2.py -- <leopard_2_a6.glb> <out.glb> [vorschau.png]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
preview = args[2] if len(args) > 2 else None
GROUND = -1.08

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
kind = {}
for o in tc.meshes():
    kind[o.name] = o.parent.name if o.parent else o.name
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
FIX = Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Translation(Vector((0, 0, -GROUND)))   # vorne -> -Y, Boden -> 0
for o in list(tc.meshes()):
    if kind[o.name].startswith("track"): bpy.data.objects.remove(o); continue
    o.data.transform(FIX)

def bbox_co(o):
    co = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    return co

def split_disc(o):
    """Laufrolle: runde Inseln (Scheibe, Reifen, Nabe) behalten, Schwingarm in die Wanne."""
    me = o.data
    fi, lv, ls, lt = tc.islands(me); st = tc.island_stats(me, fi, lv, ls, lt)
    discs = [(mn, mx) for mn, mx, n, m in st.values() if abs((mx - mn)[1] - (mx - mn)[2]) < 0.08 * (mx - mn)[1] and (mx - mn)[1] > 0.3]
    r = max((mx - mn)[1] for mn, mx in discs) / 2
    big = max(discs, key=lambda d: (d[1] - d[0])[1]); c = (big[0] + big[1]) / 2
    # Reifen haengen im Modell am Schwingarm (eine Insel): darum nach Flaechen trennen - alles innerhalb der Radscheibe dreht
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    inside = {f.index for f in bm.faces if all(math.hypot(v.co.y - c[1], v.co.z - c[2]) <= r * 1.03 for v in f.verts)}
    arm = bm.copy()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in inside], context="FACES")
    arm.faces.ensure_lookup_table()
    bmesh.ops.delete(arm, geom=[f for f in arm.faces if f.index in inside], context="FACES")
    bm.to_mesh(me); bm.free()
    ame = bpy.data.meshes.new(o.name + "_arm"); arm.to_mesh(ame); arm.free()
    for mat in me.materials: ame.materials.append(mat)
    ao = bpy.data.objects.new(o.name + "_arm", ame); bpy.context.scene.collection.objects.link(ao)
    return Vector(c), r, ao

objs = {}; hull_parts = []; wheels = {"L": [], "R": []}; rollers = {"L": [], "R": []}
for o in list(tc.meshes()):
    k = kind[o.name]
    if k == "body": hull_parts.append(o)
    elif k == "turret": objs["turret"] = o
    elif k == "gun": objs["cannon"] = o
    elif " wheel" in k:
        co = bbox_co(o); mn, mx = co.min(0), co.max(0); c = (mn + mx) / 2
        side = "L" if c[0] < 0 else "R"   # nach der Drehung: links = -X
        if " op wheel" in k:
            cen, r, arm = split_disc(o); hull_parts.append(arm)
            wheels[side].append((cen[1], cen, r, o, "road"))
        elif " p wheel" in k:
            rollers[side].append((c[1], Vector(c), (mx - mn)[1] / 2, o))
        else:
            wheels[side].append((c[1], Vector(c), (mx - mn)[1] / 2, o, "end"))
tc.select_only(hull_parts, hull_parts[0]); bpy.ops.object.join()
objs["Wanne"] = bpy.context.view_layer.objects.active
info = {}
for sn in "LR":
    ws = sorted(wheels[sn], key=lambda w: -w[0])            # hinten (grosses y) zuerst
    for k, w in enumerate(ws):
        w[3].name = f"rad{sn}{k}"; objs[w[3].name] = w[3]
    info[sn] = [(round(w[0], 2), round(w[1].z, 2), round(w[2], 3), w[4]) for w in ws]
    for k, ro in enumerate(sorted(rollers[sn], key=lambda r: -r[0])):
        ro[3].name = f"roll{sn}{k + 1}"; objs[ro[3].name] = ro[3]
    print(f"L2 {sn} Raeder", info[sn], "Stuetzrollen", [(round(r[0], 2), round(r[1].z, 2), round(r[2], 3)) for r in sorted(rollers[sn], key=lambda r: -r[0])])

if preview:
    for nm, o in objs.items():
        c = (0.9, 0.2, 0.1, 1) if nm == "turret" else (1, 0.9, 0.1, 1) if nm == "cannon" else (0.4, 0.4, 0.4, 1) if nm == "Wanne" else (0.9, 0.3, 0.9, 1) if nm.startswith("roll") else ((0.1, 0.5, 1, 1) if int(nm[4:]) % 2 == 0 else (0.1, 0.9, 0.4, 1))
        mat = bpy.data.materials.new("v" + nm); mat.diffuse_color = c; o.data.materials.clear(); o.data.materials.append(mat)
    sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 800
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("seite", Vector((-16, -1, 1.3))), ("schraeg", Vector((-9, -9, 6)))):
        cam.location = pos; d = Vector((0, -1, 1.3)) - pos; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cd.type = "ORTHO" if name == "seite" else "PERSP"; cd.ortho_scale = 12; cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)
    sys.exit(0)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p

for sn in "LR":
    for w in wheels[sn]:
        o = w[3]; co = bbox_co(o); set_origin(o, Vector(((co[:, 0].min() + co[:, 0].max()) / 2, w[1].y, w[1].z)))
    for ro in rollers[sn]: set_origin(ro[3], ro[1])
# Turmgelenk: Mitte der Turmunterseite; Rohrachse aus der Muendung; Schildzapfen 1,1 m hinter der Blendenfront
tco = bbox_co(objs["turret"]); low = tco[tco[:, 2] < tco[:, 2].min() + 0.06]
TPIV = Vector(((low[:, 0].min() + low[:, 0].max()) / 2, (low[:, 1].min() + low[:, 1].max()) / 2, tco[:, 2].min()))
gco = bbox_co(objs["cannon"]); tip = gco[gco[:, 1] < gco[:, 1].min() + 0.5]
bx, bz = float((tip[:, 0].min() + tip[:, 0].max()) / 2), float((tip[:, 2].min() + tip[:, 2].max()) / 2)
thick = gco[(np.abs(gco[:, 0] - bx) > 0.3)]           # Blende: breiter als das Rohr
mant_front = float(thick[:, 1].min()) if len(thick) else float(tco[:, 1].min())
GPIV = Vector((bx, mant_front + 1.1, bz))
set_origin(objs["turret"], TPIV); set_origin(objs["cannon"], GPIV)
for img in bpy.data.images:
    if img.size[0] > 1024: img.scale(1024, 1024 * img.size[1] // img.size[0])
bpy.context.view_layer.update()
objs["cannon"].parent = objs["turret"]; objs["cannon"].matrix_parent_inverse = objs["turret"].matrix_world.inverted()
root = bpy.data.objects.new("Leopard2A6", None); bpy.context.scene.collection.objects.link(root)
for o in objs.values():
    if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
for nm, o in objs.items(): o.name = nm; o.data.name = nm   # turret/cannon/Wanne: Namen, die das Spiel sucht
bpy.context.view_layer.update()
wco = bbox_co(objs["Wanne"])
print(f"L2 Wanne min {wco.min(0).round(2)} max {wco.max(0).round(2)}; Turm {tuple(round(v, 3) for v in TPIV)} Rohr {tuple(round(v, 3) for v in GPIV)} Blende y {mant_front:.2f} Muendung y {gco[:, 1].min():.2f}")
print("L2 Dreiecke", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs.values()))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=88)
print("L2 exportiert", out, os.path.getsize(out) // 1024, "KB")

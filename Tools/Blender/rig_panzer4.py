"""
Riggt das Sketchfab-Modell "panzer_iv.glb" (Panzer IV Ausf. F2) fuer PanzerSim (Aufbau wie T34_85.glb):
  hull, turret (Drehpunkt Turmring) > cannon (Drehpunkt Schildzapfen),
  radL0..radL9 / radR0..radR9 (0 = Leitrad hinten, 1-8 Laufrollen, 9 = Antriebsrad vorne),
  armL/armR = Laufrollenschwingen (federn mit), rollL1..4/rollR1..4 = Stuetzrollen (Kette laeuft oben darueber),
  armFest = Rollenhalter, Kotfluegel, Auspuff usw. (keine Panzerung).
Vorne = Blender -Y (in Unity +Z). Das Modell ist bereits in Metern.
Texturen landen neben der .glb (BaseColor, Normal, MetallicSmoothness), das Prefab baut Unity
(Editor/ModelPrefabs.cs, Menue "Panzer/Modell-Prefabs einrichten").
Aufruf: blender -b --python rig_panzer4.py -- <panzer_iv.glb> <out.glb>
"""
import bpy, bmesh, sys, os
import numpy as np
from mathutils import Vector, Matrix

src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

def meshes(): return [o for o in bpy.data.objects if o.type == "MESH"]
def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or (objs[0] if objs else None)
def bbox(o):
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    return (Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
            Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs))))
def set_origin(o, p):
    d = o.matrix_world.inverted() @ p
    o.data.transform(Matrix.Translation(-d))
    o.matrix_world = o.matrix_world @ Matrix.Translation(d)
def join(objs, name):
    objs = [o for o in objs if o is not None]
    if not objs: return None
    select_only(objs, objs[0]); bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    return o
def split_loose(obj):
    before = set(o.name for o in bpy.data.objects) - {obj.name}
    select_only([obj])
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
    return [o for o in meshes() if o.name not in before]
def obj(prefix):
    for o in meshes():
        if o.name.startswith(prefix): return o
    return None
def objs(prefix): return [o for o in meshes() if o.name.startswith(prefix)]

# ---- Texturen aus dem Original holen, bevor die Materialien umbenannt werden ----
outDir = os.path.dirname(out)
def save(img, path): img.filepath_raw = path; img.file_format = "PNG"; img.save()
def copy_scaled(img, size, name, noncolor):
    c = img.copy(); c.name = name
    if noncolor: c.colorspace_settings.name = "Non-Color"
    if max(c.size) > size: c.scale(size, size)
    return c
for mat in bpy.data.materials:
    if not mat.use_nodes: continue
    base = orm = nrm = None
    for n in mat.node_tree.nodes:
        if n.type != "TEX_IMAGE" or not n.image: continue
        to = [l.to_node.type + ":" + l.to_socket.name for l in n.outputs["Color"].links]
        if any("Base Color" in t for t in to): base = n.image
        elif any(t.startswith("NORMAL_MAP") for t in to): nrm = n.image
        elif any(t.startswith("SEPARATE_COLOR") or t.startswith("SEPRGB") for t in to): orm = n.image
    if not (base and orm and nrm): continue
    key = mat.name
    save(copy_scaled(base, 2048, key + "_bc", False), os.path.join(outDir, f"{key}_BaseColor.png"))
    save(copy_scaled(nrm, 2048, key + "_n", True), os.path.join(outDir, f"{key}_Normal.png"))
    o = copy_scaled(orm, 1024, key + "_orm", True)          # glTF: G = Roughness, B = Metallic
    w, h = o.size
    px = np.empty(w * h * 4, dtype=np.float32); o.pixels.foreach_get(px); px = px.reshape(-1, 4)
    ms = np.empty_like(px); ms[:, 0] = ms[:, 1] = ms[:, 2] = px[:, 2]; ms[:, 3] = 1.0 - px[:, 1]
    im = bpy.data.images.new(key + "_MS", w, h, alpha=True); im.colorspace_settings.name = "Non-Color"
    im.pixels.foreach_set(ms.ravel())
    save(im, os.path.join(outDir, f"{key}_MetallicSmoothness.png"))
    print(f"RIG textures {key} ok")

# ---- Transformationen anwenden, Leerobjekte weg, Kette weg ----
for o in bpy.data.objects:
    if o.parent is not None:
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
select_only(meshes())
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
groundZ = min(bbox(o)[0].z for o in objs("track_001"))
for o in objs("track_001"): bpy.data.objects.remove(o)
M = Matrix.Translation(Vector((0, 0, -groundZ)))   # Kettenunterkante = 0
for o in meshes(): o.data.transform(M); o.matrix_world = Matrix.Identity(4)

# ---- Raeder ----
def side_of(c): return 0 if c.x < 0 else 1
pieces = split_loose(obj("wheel_001"))
info = {p.name: bbox(p) for p in pieces}
discs = []
for p in pieces:
    mn, mx = info[p.name]; c = (mn + mx) / 2; d = mx - mn
    if d.y > 0.3 and abs(d.y - d.z) < 0.08 * d.y: discs.append((c, max(d.y, d.z) / 2))
stations = []   # [side, center, radius]
for c, r in discs:
    for s in stations:
        if s[0] == side_of(c) and abs(s[1].y - c.y) < 0.08 and abs(s[1].z - c.z) < 0.08:
            if r > s[2]: s[2] = r
            s[3].append(c); break
    else: stations.append([side_of(c), c, r, [c]])
for s in stations:   # Mitte der Station = Mittel aus inneren und aeusseren Scheiben
    s[1] = sum(s[3], Vector()) / len(s[3])
road = {sd: sorted([s for s in stations if s[0] == sd], key=lambda s: -s[1].y) for sd in (0, 1)}   # hinten zuerst
print("RIG road wheels per side:", len(road[0]), len(road[1]), "radius", round(max(s[2] for s in stations), 3))

# Antriebsrad (vorne) und Leitrad (hinten) je Seite trennen
def split_sides(o):
    parts = split_loose(o)
    L = [p for p in parts if side_of((bbox(p)[0] + bbox(p)[1]) / 2) == 0]
    R = [p for p in parts if p not in L]
    return L, R
spL, spR = split_sides(obj("driveSprocket_001"))
idL, idR = split_sides(obj("rearIdler_low"))
def wheel_center(ps):
    mn = Vector((99, 99, 99)); mx = -mn
    for p in ps:
        a, b = bbox(p); mn = Vector(map(min, mn, a)); mx = Vector(map(max, mx, b))
    return (mn + mx) / 2, max(mx.y - mn.y, mx.z - mn.z) / 2

for sd, (sp, idl) in enumerate(((spL, idL), (spR, idR))):
    L = "L" if sd == 0 else "R"
    ic, ir = wheel_center(idl)
    o = join(idl, f"rad{L}0"); set_origin(o, ic)
    print(f"RIG rad{L}0 Leitrad c=({ic.x:.3f},{ic.y:.3f},{ic.z:.3f}) r={ir:.3f}")
    for i, s in enumerate(road[sd]):
        names = [nm for nm, (a, b) in info.items() if side_of((a + b) / 2) == sd
                 and abs((a.y + b.y) / 2 - s[1].y) < s[2] * 0.9 and abs((a.z + b.z) / 2 - s[1].z) < s[2] * 0.9]
        for nm in names: del info[nm]
        o = join([bpy.data.objects[nm] for nm in names], f"rad{L}{i + 1}"); set_origin(o, s[1])
    sc, sr = wheel_center(sp)
    n = len(road[sd]) + 1
    o = join(sp, f"rad{L}{n}"); set_origin(o, sc)
    print(f"RIG rad{L}{n} Antriebsrad c=({sc.x:.3f},{sc.y:.3f},{sc.z:.3f}) r={sr:.3f}")
leftover = [bpy.data.objects[nm] for nm in info if nm in bpy.data.objects]
print("RIG wheel leftovers:", len(leftover))

# Schwingen/Drehgestelle: zur naechsten Laufrolle (federn mit)
susp = split_loose(obj("suspension_low")) + leftover
armOf = {}
for p in susp:
    mn, mx = bbox(p); c = (mn + mx) / 2; sd = side_of(c)
    rs = road[sd]
    j = min(range(len(rs)), key=lambda k: abs(rs[k][1].y - c.y))
    armOf[p.name] = (sd, j + 1)
for sd in (0, 1):
    L = "L" if sd == 0 else "R"
    for i, s in enumerate(road[sd]):
        a = join([bpy.data.objects[nm] for nm, k in armOf.items() if k == (sd, i + 1)], f"arm{L}{i + 1}")
        if a: set_origin(a, s[1])

# ---- Turm und Kanone ----
gunParts = [obj("barrel_low"), obj("mount_low"), obj("muzzle_low"), obj("gun_002")]
turParts = [obj("turret_low"), obj("commanderCupola"), obj("hatch_002"), obj("handleBracket"), obj("hinge_004"),
            obj("window_003"), obj("window_004")]
tmn, tmx = bbox(obj("turret_low"))
bmn, bmx = bbox(obj("barrel_low"))
zmn, zmx = bbox(obj("muzzle_low"))   # Rohrachse aus der Muendungsbremse (das Rohr-Mesh hat seitliche Anbauteile)
tpiv = Vector(((tmn.x + tmx.x) / 2, (tmn.y + tmx.y) / 2, tmn.z))
gpiv = Vector(((zmn.x + zmx.x) / 2, bmx.y + 0.12, (zmn.z + zmx.z) / 2))   # Schildzapfen knapp hinter der Blende
muzzleTip = zmn.y
turret = join(turParts, "turret"); set_origin(turret, tpiv)
cannon = join(gunParts, "cannon"); set_origin(cannon, gpiv)
cannon.parent = turret; cannon.matrix_parent_inverse = turret.matrix_world.inverted()
print(f"RIG turret pivot=({tpiv.x:.3f},{tpiv.y:.3f},{tpiv.z:.3f}) gun pivot=({gpiv.x:.3f},{gpiv.y:.3f},{gpiv.z:.3f}) muzzleTip={muzzleTip:.3f}")

# ---- Wanne (Panzerung) und Anbauteile (keine Panzerung) ----
hullParts = [obj("hull_low"), obj("hatch_001"), obj("machineGun_001"), obj("sideVent"),
             obj("window_001"), obj("window_002")] + objs("fuelDoor") + objs("hinge_")
# Stuetzrollen: je Seite einzeln (rollL1..4 / rollR1..4, Ursprung = Mitte), die Kette laeuft oben darueber
rollers = sorted(objs("roller_"), key=lambda o: o.name)
for k, ro in enumerate(rollers):
    rL, rR = split_sides(ro)
    for side, ps in (("L", rL), ("R", rR)):
        c, r = wheel_center(ps)
        o = join(ps, f"roll{side}{k + 1}"); set_origin(o, c)
        print(f"RIG roll{side}{k + 1} c=({c.x:.3f},{c.y:.3f},{c.z:.3f}) r={r:.3f}")
fixParts = [obj("fender_low"), obj("fenderStop"), obj("muffler"), obj("rearFrame"), obj("towBracket"),
            obj("driveSprocket_002"), obj("rearIdler_hub")] + objs("trackHolder") + objs("track_00") + objs("returnRoller")
hull = join(hullParts, "hull")
fix = join([o for o in fixParts if o is not None and o.name in bpy.data.objects and o not in (hull,)], "armFest")
hmn, hmx = bbox(hull)
print(f"RIG hull min=({hmn.x:.3f},{hmn.y:.3f},{hmn.z:.3f}) max=({hmx.x:.3f},{hmx.y:.3f},{hmx.z:.3f})")
rest = [o.name for o in meshes() if not (o.name in ("hull", "turret", "cannon", "armFest") or o.name.startswith(("rad", "arm", "roll")))]
print("RIG nicht zugeordnet:", rest)

# ---- Wurzel und Export (ohne Bilder) ----
root = bpy.data.objects.new("PanzerIV_F2", None); bpy.context.scene.collection.objects.link(root)
for o in meshes():
    if o.parent is None:
        mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
allmn = Vector((99, 99, 99)); allmx = -allmn
for o in meshes():
    a, b = bbox(o); allmn = Vector(map(min, allmn, a)); allmx = Vector(map(max, allmx, b))
print(f"RIG all min=({allmn.x:.3f},{allmn.y:.3f},{allmn.z:.3f}) max=({allmx.x:.3f},{allmx.y:.3f},{allmx.z:.3f})")
print("RIG objects:", sorted(o.name for o in meshes()))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_image_format="NONE", export_yup=True)
print("RIG exported", out, os.path.getsize(out) // 1024, "KB")

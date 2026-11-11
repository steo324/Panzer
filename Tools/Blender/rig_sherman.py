"""
Riggt das M4A1E9-Sherman-FBX fuer PanzerSim (gleicher Aufbau wie T34_85.glb):
  hull, turret (Drehpunkt Turmring) > cannon (Drehpunkt Schildzapfen),
  radL0..radLn / radR0..radRn (0 = Leitrad hinten, letztes = Antriebsrad vorne), armL/armR = Drehgestelle.
Vorne = Blender -Y (wird in Unity +Z). Masse in Metern (Wannenlaenge 5,84 m).
Aufruf: blender -b --python rig_sherman.py -- <fbx> <out.glb> <texturordner>
Die Texturen landen neben der .glb (BaseColor, Normal, MetallicSmoothness); das Prefab mit
URP-Materialien baut danach Unity-Menue "Panzer/M4A1E9-Modell einrichten" (Editor/M4A1E9Setup.cs).
"""
import bpy, bmesh, sys, os
from mathutils import Vector, Matrix

src, out, texdir = sys.argv[sys.argv.index("--") + 1:][:3]
REAL_HULL_LENGTH = 5.84

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
scene = bpy.context.scene

def meshes():
    return [o for o in bpy.data.objects if o.type == "MESH"]

def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or (objs[0] if objs else None)

def bbox(o):
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    return mn, mx

def set_origin(o, p):
    """Ursprung des Objekts auf Weltpunkt p setzen, Geometrie bleibt stehen."""
    d = o.matrix_world.inverted() @ p
    o.data.transform(Matrix.Translation(-d))
    o.matrix_world = o.matrix_world @ Matrix.Translation(d)

# ---- 1) Transformationen anwenden, Ketten weg ----
for o in bpy.data.objects:
    if o.parent is not None:
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
select_only(meshes())
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tracks = bpy.data.objects["Cube.001"]
tmn, tmx = bbox(tracks)
hull = bpy.data.objects["Hull"]; hull.name = "hull"
hmn, hmx = bbox(hull)
cx, cy = (hmn.x + hmx.x) / 2, (hmn.y + hmx.y) / 2
S = REAL_HULL_LENGTH / (hmx.y - hmn.y)
print(f"RIG scale={S:.4f}")
# Kettenbreite je Seite (aus der Modellkette), bevor sie geloescht wird
tv = [tracks.matrix_world @ v.co for v in tracks.data.vertices]
left = [v.x for v in tv if v.x < cx]; right = [v.x for v in tv if v.x >= cx]
trackW = ((max(left) - min(left)) + (max(right) - min(right))) / 2 * S
trackOuter = (max(right) - min(left)) * S
trackBottom = tmn.z
bpy.data.objects.remove(tracks)

# Alles verschieben (Mitte, Kettenunterkante = 0) und auf echte Groesse bringen
M = Matrix.Scale(S, 4) @ Matrix.Translation(Vector((-cx, -cy, -trackBottom)))
for o in meshes():
    o.data.transform(M); o.matrix_world = Matrix.Identity(4)

# ---- 2) Laufwerk in Einzelteile zerlegen ----
def split_loose(obj):
    before = set(o.name for o in bpy.data.objects) - {obj.name}
    select_only([obj])
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
    return [o for o in meshes() if o.name not in before]

pieces = split_loose(bpy.data.objects["Cylinder.004"])
print(f"RIG wheel pieces={len(pieces)}")
info = {}
for p in pieces:
    mn, mx = bbox(p); info[p.name] = (mn, mx, (mn + mx) / 2, mx - mn)

# Radscheiben: rund in der Y-Z-Ebene, schmal in X
discs = []
for p in pieces:
    mn, mx, c, d = info[p.name]
    if d.y > 0.18 and abs(d.y - d.z) < 0.09 * max(d.y, 0.1) and d.x < 0.8:   # Leitrad = Doppelrad, breiter
        discs.append((p, c, max(d.y, d.z) / 2))
groups = []   # [side, center(Vector), radius]
for p, c, r in discs:
    side = 0 if c.x < 0 else 1
    for g in groups:
        if g[0] == side and (Vector((0, c.y, c.z)) - Vector((0, g[1].y, g[1].z))).length < 0.06:
            if r > g[2]: g[2] = r; g[1] = c
            break
    else:
        groups.append([side, c, r])
for g in sorted(groups, key=lambda g: (g[0], g[1].y)):
    print(f"RIG group side={g[0]} c=({g[1].x:.3f},{g[1].y:.3f},{g[1].z:.3f}) r={g[2]:.3f}")
# grosse, nicht runde Teile an den Enden (Antriebsrad mit Zaehnen, Leitrad) zur Kontrolle
for p in pieces:
    mn, mx, c, d = info[p.name]
    if max(d.y, d.z) > 0.35 and (c.y > 2.0 or c.y < -2.0):
        print(f"RIG endpiece {p.name} n={len(p.data.vertices)} c=({c.x:.3f},{c.y:.3f},{c.z:.3f}) d=({d.x:.3f},{d.y:.3f},{d.z:.3f})")
minZ = min(g[1].z for g in groups)
road = [g for g in groups if g[1].z < minZ + 0.06]
other = [g for g in groups if g not in road and g[2] > 0.18]
wheelsBySide = {}
for side in (0, 1):
    rs = sorted([g for g in road if g[0] == side], key=lambda g: -g[1].y)   # hinten (gross y) zuerst
    os_ = [g for g in other if g[0] == side]
    idler = max(os_, key=lambda g: g[1].y)       # hinten
    sprocket = min(os_, key=lambda g: g[1].y)    # vorne
    assert idler is not sprocket and idler[1].y > 1.5 and sprocket[1].y < -1.5, "Leitrad/Antriebsrad nicht gefunden"
    wheelsBySide[side] = [idler] + rs + [sprocket]

# Teile den Raedern zuordnen (rund oder klein und innerhalb des Radkreises)
assign = {}
for p in pieces:
    mn, mx, c, d = info[p.name]
    side = 0 if c.x < 0 else 1
    big = max(d.y, d.z)
    roundish = abs(d.y - d.z) < 0.25 * big or big < 0.12
    best = None
    for i, g in enumerate(wheelsBySide[side]):
        dist = (Vector((0, c.y, c.z)) - Vector((0, g[1].y, g[1].z))).length
        if roundish and dist < g[2] * 0.9 and big <= 2 * g[2] * 1.06 and abs(c.x - g[1].x) < 0.35:
            if best is None or dist < best[1]: best = (i, dist)
    if best: assign[p.name] = ("rad", side, best[0])
    else:
        # Drehgestell-Teile: zum naechsten Laufrad (federn mit), hohe Teile (Stuetzrollen) bleiben fest
        rs = wheelsBySide[side][1:-1]
        if c.z < minZ + 0.45 and rs:
            j = min(range(len(rs)), key=lambda k: abs(rs[k][1].y - c.y))
            assign[p.name] = ("arm", side, j + 1)
        else:
            assign[p.name] = ("static", side, 0)

def join(objs, name):
    if not objs: return None
    select_only(objs, objs[0]); bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    return o

for side in (0, 1):
    L = "L" if side == 0 else "R"
    for i, g in enumerate(wheelsBySide[side]):
        objs = [bpy.data.objects[n] for n, a in assign.items() if a == ("rad", side, i)]
        o = join(objs, f"rad{L}{i}")
        if o: set_origin(o, g[1]); print(f"RIG rad{L}{i} c=({g[1].x:.3f},{g[1].y:.3f},{g[1].z:.3f}) r={g[2]:.3f} parts={len(objs)}")
        arms = [bpy.data.objects[n] for n, a in assign.items() if a == ("arm", side, i)]
        a = join(arms, f"arm{L}{i}")
        if a: set_origin(a, g[1])
statics = [bpy.data.objects[n] for n, a in assign.items() if a[0] == "static"]
st = join(statics, "armFest")   # Stuetzrollen usw.: "arm..." -> zaehlt nicht als Panzerung, bewegt sich nicht

# ---- 3) Turm und Kanone trennen ----
tparts = split_loose(bpy.data.objects["Turret.002"])
tinfo = {o.name: bbox(o) for o in tparts}
barrel = max(tparts, key=lambda o: (tinfo[o.name][1].y - tinfo[o.name][0].y) / max(tinfo[o.name][1].x - tinfo[o.name][0].x, 1e-3))
bmn, bmx = tinfo[barrel.name]
gunX, gunZ = (bmn.x + bmx.x) / 2, (bmn.z + bmx.z) / 2
def vol(o):
    mn, mx = tinfo[o.name]; d = mx - mn
    return d.x * d.y * d.z
body = max(tparts, key=vol)   # Turmschale = groesstes Volumen (nicht die Blende mit den meisten Punkten)
dmn, dmx = tinfo[body.name]
for o in sorted(tparts, key=vol, reverse=True)[:6]:
    mn, mx = tinfo[o.name]; c = (mn + mx) / 2; d = mx - mn
    print(f"RIG turretpart n={len(o.data.vertices)} c=({c.x:.3f},{c.y:.3f},{c.z:.3f}) d=({d.x:.3f},{d.y:.3f},{d.z:.3f})")
frontY = dmn.y   # Vorderkante Turmkoerper (vorne = -Y)
gun = []
for o in tparts:
    mn, mx = tinfo[o.name]; c = (mn + mx) / 2
    if o == barrel or (o != body and c.y < frontY + 0.25 and abs(c.x - gunX) < 0.65 and abs(c.z - gunZ) < 0.45):
        gun.append(o)
rest = [o for o in tparts if o not in gun]
turret = join(rest, "turret")
cannon = join(gun, "cannon")
tpiv = Vector(((dmn.x + dmx.x) / 2, (dmn.y + dmx.y) / 2 + 0.1, dmn.z))
gpiv = Vector((gunX, frontY + 0.22, gunZ))
set_origin(turret, tpiv)
set_origin(cannon, gpiv)
cannon.parent = turret
cannon.matrix_parent_inverse = turret.matrix_world.inverted()
print(f"RIG turret pivot=({tpiv.x:.3f},{tpiv.y:.3f},{tpiv.z:.3f}) gun pivot=({gpiv.x:.3f},{gpiv.y:.3f},{gpiv.z:.3f}) barrelTip={bmn.y:.3f} gunParts={len(gun)}")

# ---- 4) Texturen fuer Unity aufbereiten (nicht in die .glb einbetten: Unity wuerde sie unkomprimiert laden) ----
# Je Material: BaseColor (2048), Normal (2048), MetallicSmoothness (1024, RGB = Metallic, A = 1 - Roughness)
import numpy as np
TEX = {
    "Frame":  ("M4A1E9_Sherman_Frame_BaseColor", "M4A1E9_Sherman_Frame_Metallic", "M4A1E9_Sherman_Frame_Roughness", "M4A1E9_Sherman_Frame_Normal"),
    "Turret": ("Turret_Base_color", "Turret_Metallic", "Turret_Roughness", "Turret_Normal_OpenGL"),
    "Wheels": ("M4A1E9_Sherman_Wheels_BaseColor", "M4A1E9_Sherman_Wheels_Metallic", "M4A1E9_Sherman_Wheels_Roughness", "M4A1E9_Sherman_Wheels_Normal"),
}
outTex = os.path.dirname(out)
def load(name, size, color):
    i = bpy.data.images.load(os.path.join(texdir, name + ".png"))
    if not color: i.colorspace_settings.name = "Non-Color"
    if max(i.size) > size: i.scale(size, size)
    return i
def save(i, path):
    i.filepath_raw = path; i.file_format = "PNG"; i.save()
for key, (bc, me, ro, no) in TEX.items():
    save(load(bc, 2048, True), os.path.join(outTex, f"{key}_BaseColor.png"))
    save(load(no, 2048, False), os.path.join(outTex, f"{key}_Normal.png"))
    m = load(me, 1024, False); r = load(ro, 1024, False)
    w, h = m.size
    pm = np.empty(w * h * 4, dtype=np.float32); m.pixels.foreach_get(pm)
    pr = np.empty(w * h * 4, dtype=np.float32); r.pixels.foreach_get(pr)
    pm = pm.reshape(-1, 4); pr = pr.reshape(-1, 4)
    pm[:, 3] = 1.0 - pr[:, 0]
    ms = bpy.data.images.new(f"{key}_MS", w, h, alpha=True, float_buffer=False)
    ms.colorspace_settings.name = "Non-Color"
    ms.pixels.foreach_set(pm.ravel())
    save(ms, os.path.join(outTex, f"{key}_MetallicSmoothness.png"))
    print(f"RIG textures {key} ok")
for mat in bpy.data.materials:
    key = mat.name.split(".")[0]
    if key in TEX: mat.name = key   # Unity ordnet die Materialien ueber diese Namen zu

# ---- 5) Wurzel und Export ----
root = bpy.data.objects.new("M4A1E9", None); scene.collection.objects.link(root)
for o in meshes():
    if o.parent is None:
        mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw

hmn, hmx = bbox(bpy.data.objects["hull"])
print(f"RIG hull min=({hmn.x:.3f},{hmn.y:.3f},{hmn.z:.3f}) max=({hmx.x:.3f},{hmx.y:.3f},{hmx.z:.3f})")
print(f"RIG trackWidth={trackW:.3f} trackOuter={trackOuter:.3f}")
allmn = Vector((9, 9, 9)); allmx = Vector((-9, -9, -9))
for o in meshes():
    mn, mx = bbox(o)
    allmn = Vector(map(min, allmn, mn)); allmx = Vector(map(max, allmx, mx))
print(f"RIG all min=({allmn.x:.3f},{allmn.y:.3f},{allmn.z:.3f}) max=({allmx.x:.3f},{allmx.y:.3f},{allmx.z:.3f})")
print("RIG objects:", sorted(o.name for o in meshes()))

bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_image_format="NONE",
                          export_yup=True, export_apply=False)
print("RIG exported", out, os.path.getsize(out) // 1024, "KB")

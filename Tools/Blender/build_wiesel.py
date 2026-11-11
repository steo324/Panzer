"""
Baut den Wiesel 1 TOW (Bundeswehr, Luftlande-Waffentraeger) fuer PanzerSim komplett im Code nach Risszeichnungen:
Laenge 3,55 m (MK20 mit Rohr; Wanne 3,30 m), Breite 1,82 m, Hoehe ~1,8 m (mit TOW-Werfer).
Teile wie bei den anderen Modellen:
  hull, turret (Drehpunkt Werfersaeule) > cannon (Drehpunkt Werfer-Schildzapfen),
  radL0..radL4 / radR0..radR4 (0 = grosses Leitrad hinten, 1-3 Laufrollen, 4 = Antriebsrad vorne),
  rollL1 / rollR1 = Stuetzrolle (Kette laeuft oben darueber), armFest = Anbauteile.
Vorne = Blender -Y (in Unity +Z), links = Blender -X. Masse in Metern. Tarnanstrich als Textur (in der .glb).
Aufruf: blender -b --python build_wiesel.py -- <out.glb>
"""
import bpy, bmesh, sys, math, os
import numpy as np
from mathutils import Vector, Matrix

out = sys.argv[sys.argv.index("--") + 1:][0]
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------------- Materialien ----------------
def make_camo(size=512, seed=7):
    """Flecktarn-artiger Anstrich: Grundton Oliv, Flecken in Gruen, Braun, Schwarz (kachelbar)."""
    rng = np.random.default_rng(seed)
    img = np.zeros((size, size, 4), np.float32)
    base = np.array([0.30, 0.33, 0.22]); greens = np.array([0.20, 0.26, 0.14]); brown = np.array([0.33, 0.25, 0.16]); black = np.array([0.08, 0.08, 0.07])
    img[..., :3] = base; img[..., 3] = 1
    yy, xx = np.mgrid[0:size, 0:size]
    for col, n, rmin, rmax in ((greens, 70, 10, 26), (brown, 55, 8, 20), (black, 45, 4, 11)):
        for _ in range(n):
            cx, cy = rng.integers(0, size, 2); r = rng.uniform(rmin, rmax)
            for k in range(rng.integers(2, 5)):   # Fleck aus mehreren Kreisen (unregelmaessig)
                ox, oy = rng.normal(0, r * 0.6, 2); rr = r * rng.uniform(0.5, 1.0)
                dx = (xx - cx - ox + size / 2) % size - size / 2; dy = (yy - cy - oy + size / 2) % size - size / 2
                img[(dx * dx + dy * dy) < rr * rr, :3] = col
    img[..., :3] *= rng.uniform(0.93, 1.05, (size, size, 1))   # leichte Koernung
    im = bpy.data.images.new("wiesel_tarn", size, size, alpha=True)
    im.pixels = img.ravel().tolist()
    im.pack()
    return im

def material(name, color, rough=0.75, metal=0.0, tex=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1); b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    if tex is not None:
        t = m.node_tree.nodes.new("ShaderNodeTexImage"); t.image = tex
        m.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
    return m

camo = make_camo()
M_BODY = material("wiesel_tarn", (1, 1, 1), 0.8, 0.0, camo)
M_DARK = material("wiesel_dunkel", (0.06, 0.06, 0.055), 0.6, 0.3)
M_RUBBER = material("wiesel_gummi", (0.05, 0.05, 0.05), 0.9)
M_WHEEL = material("wiesel_rad", (0.22, 0.25, 0.17), 0.7, 0.1)
M_GLASS = material("wiesel_glas", (0.55, 0.6, 0.62), 0.15, 0.0)
M_TOW = material("wiesel_tow", (0.26, 0.29, 0.20), 0.65, 0.1)

# ---------------- Hilfen ----------------
# Koordinaten: z_s = nach vorne (m, 0 = Wannenmitte), y_s = Hoehe ueber Boden, x_s = rechts
def P(x, z, y): return Vector((x, -z, y))       # Spiel -> Blender

def mesh_obj(name, verts, faces, mat):
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces); me.update()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    o.data.materials.append(mat)
    return o

def box(name, x, z, y, sx, sz, sy, mat, rot=None):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * sx, v.co.y * sz, v.co.z * sy))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    o.data.materials.append(mat); o.location = P(x, z, y)
    if rot: o.rotation_euler = rot
    return o

def cyl(name, x, z, y, r, depth, mat, axis="X", verts=24):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r, depth=depth)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    o.data.materials.append(mat); o.location = P(x, z, y)
    if axis == "X": o.rotation_euler = (0, math.radians(90), 0)
    elif axis == "Z": o.rotation_euler = (math.radians(90), 0, 0)    # laengs (Spiel-z)
    return o

def apply_all(o):
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

def join(objs, name):
    objs = [o for o in objs if o is not None]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    return o

def set_origin(o, p):
    d = o.matrix_world.inverted() @ p
    o.data.transform(Matrix.Translation(-d))
    o.matrix_world = o.matrix_world @ Matrix.Translation(d)

def uv_box(o, scale=1.2):
    """Wuerfel-Projektion fuer den Tarnanstrich (Weltmasse -> Textur, ca. 1,2 m pro Kachel)."""
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=scale, correct_aspect=False, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")

# ---------------- Wanne ----------------
HALF_LEN = 1.65
# untere Wanne zwischen den Ketten (schmal), vorne schraeg nach unten
lo_w = 0.66
lower = [(HALF_LEN, 0.80), (1.30, 0.30), (-HALF_LEN + 0.05, 0.30), (-HALF_LEN, 0.42), (-HALF_LEN, 0.80)]
# Oberwanne ueber den Ketten (breit): Bug, lange Bugschraege bis zum Dach, Dach, Heck leicht schraeg
upper = [(HALF_LEN + 0.02, 0.80), (0.82, 1.08), (-1.52, 1.08), (-HALF_LEN, 0.96), (-HALF_LEN, 0.80)]

def prism(name, prof, half_w_bottom, half_w_top, ybot, ytop, mat):
    """Profil (z, y) quer extrudiert; die Seiten werden nach oben schmaler (schraege Seitenwaende)."""
    verts = []
    n = len(prof)
    for side in (-1, 1):
        for z, y in prof:
            t = (y - ybot) / max(ytop - ybot, 1e-4)
            hw = half_w_bottom + (half_w_top - half_w_bottom) * t
            verts.append(P(side * hw, z, y))
    faces = [list(range(n))[::-1], list(range(n, 2 * n))]
    for i in range(n):
        j = (i + 1) % n
        faces.append([i, j, n + j, n + i])
    return mesh_obj(name, verts, faces, mat)

hull_lo = prism("hull_lo", lower, lo_w, lo_w, 0.30, 0.80, M_BODY)
hull_up = prism("hull_up", upper, 0.91, 0.80, 0.80, 1.08, M_BODY)
for o in (hull_lo, hull_up):
    bpy.ops.object.select_all(action="DESELECT"); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode="OBJECT")

details = []
# Lueftungsgitter seitlich (Motor rechts vorne, wie in der Zeichnung: zwei Gitterfelder an der Seite)
for side in (-1, 1):
    details.append(box(f"gitter{side}a", side * 0.865, 0.55, 0.93, 0.02, 0.85, 0.14, M_DARK, (0, 0, 0)))
    details.append(box(f"gitter{side}b", side * 0.865, -0.45, 0.93, 0.02, 0.95, 0.14, M_DARK))
    # Rueckstrahler/Scheinwerfer vorne, Ruecklicht hinten
    details.append(cyl(f"licht{side}", side * 0.72, HALF_LEN - 0.10, 0.92, 0.07, 0.10, M_GLASS, "Z", 16))
    details.append(box(f"licht_halter{side}", side * 0.72, HALF_LEN - 0.18, 0.92, 0.18, 0.08, 0.18, M_BODY))
    details.append(box(f"ruecklicht{side}", side * 0.70, -HALF_LEN - 0.01, 0.88, 0.14, 0.04, 0.08, M_DARK))
    # Abschleppoesen
    details.append(box(f"oese{side}", side * 0.45, HALF_LEN + 0.03, 0.78, 0.06, 0.06, 0.10, M_DARK))
# Fahrerluke vorne links, Kommandantenluke, Motordeckel rechts
details.append(cyl("luke_fahrer", -0.42, 0.70, 1.10, 0.24, 0.06, M_BODY, "Y_UP"))
details.append(cyl("periskop", -0.42, 0.92, 1.03, 0.05, 0.10, M_GLASS, "Y_UP", 12))
details.append(box("motordeckel", 0.40, 0.55, 1.095, 0.62, 0.70, 0.03, M_BODY))
details.append(box("gitter_dach", 0.40, 0.55, 1.115, 0.45, 0.50, 0.01, M_DARK))
details.append(cyl("luke_kdt", -0.38, -0.85, 1.11, 0.25, 0.06, M_BODY, "Y_UP"))
# Heck: Kraftstoffkanister, Haltegriffe, Antenne
details.append(box("heckkasten", 0.30, -HALF_LEN - 0.10, 0.82, 0.55, 0.18, 0.22, M_BODY))
details.append(cyl("antenne_fuss", 0.70, -1.40, 1.12, 0.03, 0.08, M_DARK, "Y_UP", 8))
details.append(cyl("antenne", 0.70, -1.40, 1.75, 0.006, 1.25, M_DARK, "Y_UP", 6))
for o in details:
    if o.name.startswith(("luke", "periskop", "antenne")): o.rotation_euler = (0, 0, 0)   # stehend (Blender Z = oben)
onHull = [d for d in details if d.name.startswith(("gitter", "motordeckel", "luke", "periskop"))]
attach = [d for d in details if d not in onHull]
hull = join([hull_lo, hull_up] + onHull, "hull")
armFest = join(attach, "armFest")

# ---------------- Laufwerk ----------------
TRACK_X = 0.79          # Kettenmitte
WHEEL_W = 0.15
road_r = 0.19
stations = [  # Name-Index, z, Hoehe der Mitte, Radius
    (0, -0.93, 0.27, 0.235),   # grosses Leitrad hinten (laeuft mit am Boden)
    (1, -0.30, 0.23, road_r),
    (2, 0.34, 0.23, road_r),
    (3, 0.98, 0.23, road_r),
    (4, 1.42, 0.62, 0.14),     # Antriebsrad vorne, hochgesetzt
]

def wheel(name, x, z, y, r, spokes=5, sprocket=False):
    parts = [cyl(name + "_rim", x, z, y, r, WHEEL_W, M_WHEEL, "X", 28)]
    parts.append(cyl(name + "_tyre", x, z, y, r * 1.0, WHEEL_W * 0.55, M_RUBBER if not sprocket else M_WHEEL, "X", 28))
    parts.append(cyl(name + "_hub", x + (0.08 if x > 0 else -0.08), z, y, r * 0.28, 0.04, M_DARK, "X", 12))
    # Speichenloecher/Rippen als dunkle Kloetzchen (wie die Kleeblatt-Raeder des Wiesel)
    for k in range(spokes):
        a = 2 * math.pi * k / spokes
        bx = box(name + f"_sp{k}", x + (0.075 if x > 0 else -0.075), z + math.cos(a) * r * 0.58, y + math.sin(a) * r * 0.58,
                 0.012, r * 0.32, r * 0.32, M_DARK)
        parts.append(bx)
    if sprocket:
        for k in range(12):
            a = 2 * math.pi * k / 12
            parts.append(box(name + f"_z{k}", x, z + math.cos(a) * (r + 0.025), y + math.sin(a) * (r + 0.025), WHEEL_W * 0.5, 0.035, 0.035, M_WHEEL,
                             (a, 0, 0)))
    for p in parts: apply_all(p)
    o = join(parts, name)
    set_origin(o, P(x, z, y))
    return o

for side, letter in ((-1, "L"), (1, "R")):
    for idx, z, y, r in stations:
        wheel(f"rad{letter}{idx}", side * TRACK_X, z, y, r, 5, sprocket=(idx == 4))
    roller = cyl(f"roll{letter}1", side * TRACK_X, 0.45, 0.62, 0.065, 0.10, M_WHEEL, "X", 16)
    apply_all(roller); set_origin(roller, P(side * TRACK_X, 0.45, 0.62))
    # Schwingarme der Laufrollen (federn mit)
    for idx, z, y, r in stations[1:4]:
        arm = box(f"arm{letter}{idx}", side * (TRACK_X - 0.12), z - 0.12, y + 0.04, 0.05, 0.30, 0.06, M_DARK, (math.radians(-18), 0, 0))
        apply_all(arm); set_origin(arm, P(side * TRACK_X, z, y))
    # Kettenabdeckung/Kotfluegel ueber dem Laufwerk
    fender = box(f"kotfluegel{letter}", side * 0.86, 0.10, 0.80, 0.14, 3.15, 0.025, M_BODY)
    apply_all(fender)
    armFest = join([armFest, fender], "armFest")

# ---------------- TOW-Werfer ----------------
TX, TZ = 0.12, -0.40            # Saeule auf dem Dach hinten
ped = cyl("saeule", TX, TZ, 1.08 + 0.20, 0.13, 0.40, M_TOW, "Y_UP", 20); ped.rotation_euler = (0, 0, 0)
yoke = box("gabel", TX, TZ, 1.52, 0.38, 0.22, 0.10, M_TOW)
ring = cyl("drehkranz", TX, TZ, 1.10, 0.22, 0.05, M_DARK, "Y_UP", 24); ring.rotation_euler = (0, 0, 0)
for p in (ped, yoke, ring): apply_all(p)
turret = join([ped, yoke, ring], "turret")
set_origin(turret, P(TX, TZ, 1.08))

GY = 1.62                        # Hoehe der Werferachse
tube = cyl("rohr", TX + 0.10, TZ + 0.25, GY, 0.11, 1.45, M_TOW, "Z", 24)
tube_end = cyl("rohr_hinten", TX + 0.10, TZ - 0.50, GY, 0.135, 0.12, M_DARK, "Z", 24)
tube_front = cyl("rohr_vorne", TX + 0.10, TZ + 0.97, GY, 0.12, 0.05, M_DARK, "Z", 24)
sight = box("visier", TX - 0.16, TZ + 0.05, GY + 0.02, 0.20, 0.42, 0.24, M_TOW)
sight_glass = box("visier_glas", TX - 0.16, TZ + 0.27, GY + 0.05, 0.12, 0.02, 0.09, M_GLASS)
guide = box("lenkgeraet", TX - 0.02, TZ - 0.15, GY - 0.16, 0.30, 0.30, 0.16, M_TOW)
for p in (tube, tube_end, tube_front, sight, sight_glass, guide): apply_all(p)
cannon = join([tube, tube_end, tube_front, sight, sight_glass, guide], "cannon")
set_origin(cannon, P(TX, TZ, GY))
cannon.parent = turret; cannon.matrix_parent_inverse = turret.matrix_world.inverted()

# ---------------- Tarnanstrich-Koordinaten, Wurzel, Export ----------------
for o in [hull, armFest, turret, cannon]: uv_box(o)
root = bpy.data.objects.new("Wiesel", None); bpy.context.collection.objects.link(root)
for o in list(bpy.data.objects):
    if o is root or o.parent is not None: continue
    mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
for o in bpy.data.objects:
    if o.type == "MESH":
        for p in o.data.polygons: p.use_smooth = False

tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.data.objects if o.type == "MESH")
print("WIESEL Objekte:", sorted(o.name for o in bpy.data.objects if o.type == "MESH"))
print("WIESEL Dreiecke:", tris)
os.makedirs(os.path.dirname(out), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_apply=False)
print("WIESEL exportiert", out, os.path.getsize(out) // 1024, "KB")

"""
Baut einen einfachen Platzhalter-Jet (eigene Form, frei verwendbar) fuer den Testflug in PanzerSim.
Groesse ungefaehr wie ein leichter zweistrahliger Trainer: 13 m lang, 9 m Spannweite, Schulterdecker.
Konvention: vorne = -Y, oben = Z, Ursprung im Schwerpunkt (Rumpfachse, etwa Fluegelmitte).
Aufruf: blender -b -P build_jet.py -- <out.glb> [vorschau.png]
"""
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
out = args[0]
preview = args[1] if len(args) > 1 else None

bpy.ops.wm.read_factory_settings(use_empty=True)

def lin(r, g, b): return tuple(((c / 255) ** 2.2) for c in (r, g, b)) + (1.0,)
def mat(name, rgba, rough=0.7, metal=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = rgba; b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    return m
M_BODY = mat("Lack", lin(92, 100, 84))
M_BELLY = mat("Unterseite", lin(150, 156, 152))
M_GLASS = mat("Glas", lin(20, 28, 34), 0.15)
M_DARK = mat("Duesen", lin(40, 38, 36), 0.5, 0.4)

def obj(name, bm, m):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    return o

def body_of_revolution(name, profile, m, seg=20, cx=0.0, cz=0.0, flat=1.0):
    """profile: Liste (y, r) von vorne nach hinten; flat < 1 macht den Querschnitt flacher (hoeher als breit > 1)."""
    bm = bmesh.new(); rings = []
    for y, r in profile:
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            ring.append(bm.verts.new((cx + r * math.cos(a), y, cz + r * math.sin(a) * flat)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for k in range(seg):
            a, b = rings[i][k], rings[i][(k + 1) % seg]
            c, d = rings[i + 1][(k + 1) % seg], rings[i + 1][k]
            bm.faces.new((a, b, c, d))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj(name, bm, m)

def slab(name, outline, z, thick, m, dihedral=0.0, side=1):
    """Fluegel/Leitwerk: Umriss (x, y) als Flaeche, Dicke thick, nach aussen um dihedral Grad geneigt."""
    bm = bmesh.new()
    t = math.tan(math.radians(dihedral))
    top = [bm.verts.new((x * side, y, z + abs(x) * t + thick / 2)) for x, y in outline]
    bot = [bm.verts.new((x * side, y, z + abs(x) * t - thick / 2)) for x, y in outline]
    bm.faces.new(top); bm.faces.new(list(reversed(bot)))
    n = len(outline)
    for i in range(n):
        bm.faces.new((top[i], top[(i + 1) % n], bot[(i + 1) % n], bot[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = obj(name, bm, m)
    for p in o.data.polygons: p.use_smooth = False
    return o

parts = []
# Rumpf: spitze Nase, Cockpit-Bereich, Heck zur Duese hin schmaler (etwas hoeher als breit)
parts.append(body_of_revolution("Rumpf", [(-6.6, 0.02), (-6.2, 0.18), (-5.5, 0.38), (-4.5, 0.58), (-3.2, 0.72), (-1.5, 0.78),
                                          (0.5, 0.78), (2.5, 0.70), (4.2, 0.55), (5.6, 0.40), (6.4, 0.30)], M_BODY, flat=1.12))
# Kabine (Tandem): lange Haube oben
parts.append(body_of_revolution("Haube", [(-4.4, 0.05), (-4.0, 0.30), (-3.0, 0.46), (-1.6, 0.48), (-0.6, 0.42), (0.2, 0.25), (0.6, 0.05)],
                                M_GLASS, seg=16, cz=0.78, flat=0.9))
# Triebwerksgondeln seitlich am Rumpf, Einlaeufe vorne, Duesen hinten
for s in (-1, 1):
    parts.append(body_of_revolution(f"Gondel{'LR'[s > 0]}", [(-1.6, 0.30), (-1.4, 0.46), (0.0, 0.50), (2.6, 0.48), (3.6, 0.40)], M_BELLY,
                                    seg=16, cx=0.95 * s, cz=-0.15))
    parts.append(body_of_revolution(f"Duese{'LR'[s > 0]}", [(3.55, 0.36), (3.9, 0.33)], M_DARK, seg=16, cx=0.95 * s, cz=-0.15))
# Schulterfluegel mit Pfeilung und leichter negativer V-Form
wing = [(0.70, -0.9), (4.55, 1.3), (4.55, 2.35), (0.70, 1.75)]
for s in (-1, 1):
    parts.append(slab(f"Fluegel{'LR'[s > 0]}", wing if s > 0 else list(reversed(wing)), 0.62, 0.16, M_BODY, dihedral=-5, side=s))
# Hoehenleitwerk und Seitenleitwerk
tail = [(0.3, 4.4), (2.15, 5.6), (2.15, 6.2), (0.3, 5.9)]
for s in (-1, 1):
    parts.append(slab(f"Hoehenruder{'LR'[s > 0]}", tail if s > 0 else list(reversed(tail)), 0.55, 0.10, M_BODY, dihedral=-3, side=s))
fin = bmesh.new()
pts = [(3.6, 0.5), (5.6, 2.8), (6.4, 2.8), (6.4, 0.5)]   # (y, z)
L = [fin.verts.new((-0.07, y, z)) for y, z in pts]; R = [fin.verts.new((0.07, y, z)) for y, z in pts]
fin.faces.new(L); fin.faces.new(list(reversed(R)))
for i in range(4): fin.faces.new((L[i], L[(i + 1) % 4], R[(i + 1) % 4], R[i]))
bmesh.ops.recalc_face_normals(fin, faces=fin.faces)
f = obj("Seitenleitwerk", fin, M_BODY)
for p in f.data.polygons: p.use_smooth = False
parts.append(f)
# Bauch (helle Unterseite): flacher Streifen unter dem Rumpf
parts.append(body_of_revolution("Bauch", [(-5.0, 0.2), (-3.5, 0.55), (2.5, 0.55), (5.0, 0.30)], M_BELLY, seg=12, cz=-0.42, flat=0.35))

root = bpy.data.objects.new("Jet", None); bpy.context.scene.collection.objects.link(root)
for o in parts: o.parent = root

if preview:
    sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 900
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("schraeg", Vector((-12, -12, 7))), ("seite", Vector((-20, 0, 1))), ("oben", Vector((0.01, 0, 25)))):
        cam.location = pos; cam.rotation_euler = (Vector((0, 0, 0.3)) - pos).to_track_quat("-Z", "Y").to_euler(); cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)

tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in parts)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True)
print("JET Dreiecke", tris, "exportiert", out)

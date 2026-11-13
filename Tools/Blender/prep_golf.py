"""
Golf IV R32 als Deko (Easter Egg auf der Karte Horn-Bad Meinberg).
Modell: "2002 Volkswagen Golf R32 Mk4" von ddiaz-design (sketchfab.com/ddiaz-design), CC BY-NC-SA 4.0.
Aenderungen: auf echte Groesse skaliert (4,17 m), Lack silbern statt blau, Texturen verkleinert.
Die erzeugte Datei (GolfR32.glb) steht ebenfalls unter CC BY-NC-SA 4.0.
Aufruf: blender -b --python prep_golf.py -- <FINAL_MODEL_IV_R32.fbx> <out.glb>
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
for o in meshes:
    if o.parent: mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
bpy.ops.object.select_all(action="SELECT"); bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def bounds():
    mn = Vector((1e9,) * 3); mx = -mn
    for o in meshes:
        for v in o.data.vertices: mn = Vector(map(min, mn, v.co)); mx = Vector(map(max, mx, v.co))
    return mn, mx
mn, mx = bounds()
s = mx - mn
long_axis_y = s.y >= s.x
k = 4.17 / max(s.x, s.y)                      # Golf IV: 4,17 m lang
M = Matrix.Scale(k, 4) @ Matrix.Translation(-Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z)))
if not long_axis_y: M = Matrix.Rotation(math.radians(90), 4, "Z") @ M   # Laengsachse auf Blender-Y
for o in meshes: o.data.transform(M)
mn, mx = bounds()
print(f"GOLF Masse {mx.x - mn.x:.2f} x {mx.y - mn.y:.2f} x {mx.z - mn.z:.2f} m")

# Lack: Reflexsilber metallic (statt Blau)
for m in bpy.data.materials:
    if not m.use_nodes: continue
    b = m.node_tree.nodes.get("Principled BSDF")
    if b is None: continue
    if m.name.startswith("carPaint"):
        b.inputs["Base Color"].default_value = (0.58, 0.60, 0.62, 1.0)
        b.inputs["Metallic"].default_value = 0.85
        b.inputs["Roughness"].default_value = 0.28
    elif m.name.startswith("glass"):
        b.inputs["Base Color"].default_value = (0.02, 0.025, 0.03, 1.0)
        b.inputs["Roughness"].default_value = 0.05
for img in bpy.data.images:
    if img.size[0] > 1024: img.scale(1024, 1024 * img.size[1] // img.size[0])
root = bpy.data.objects.new("GolfR32", None); bpy.context.scene.collection.objects.link(root)
for o in meshes: o.parent = root
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=88)
print("GOLF exportiert", out, os.path.getsize(out) // 1024, "KB,", tris, "Dreiecke,", len(bpy.data.images), "Bilder")

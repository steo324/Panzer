"""
Leopard 2 mit 140-mm-Kanone (nach dem Versuchstraeger mit der Rheinmetall NPzK 140): aus dem gerigten Leopard 2A6
(Assets/Resources/Modelle/Leopard2A6.glb, Modell von buh, CC BY 4.0) - Rohr und Blende dicker und laenger.
Aufruf: blender -b -P make_leopard140.py -- <Leopard2A6.glb> <Leopard2_140.glb>
"""
import bpy, sys
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=args[0])
gun = bpy.data.objects["cannon"]
# Ursprung = Schildzapfen; vorne = -Y: 140/120 dicker, Rohr ~1,15-fach laenger (L/57 statt L/55 bei groesserem Kaliber)
gun.data.transform(Matrix.Diagonal((1.17, 1.15, 1.17, 1.0)))
for o in bpy.data.objects:
    if o.type == "EMPTY" and o.parent is None: o.name = "Leopard2_140"
bpy.ops.export_scene.gltf(filepath=args[1], export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=88)
print("L140 exportiert", args[1])

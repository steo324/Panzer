"""
Riggt "Panzer Vi Tiger I Heavy Tank - Toshueyi" (Joanthan To, sketchfab.com/jonathanto99, CC BY 4.0) fuer PanzerSim.
Das Original ist eine Plattensuppe (~23 000 lose Flaechenstuecke), darum wird nach Lage sortiert:
  hull (Wanne, Panzerung), turret (Drehpunkt Turmmitte) > cannon (Drehpunkt Schildzapfen),
  radL0..radL9 / radR0..radR9 (0 = Leitrad hinten, 1-8 Laufrollen-Achsen, 9 = Antriebsrad vorne).
Kette des Modells entfaellt (das Spiel baut eine Gliederkette). Bemalung: tiger_paint.py.
Vorne = Blender -Y (in Unity +Z), links = Blender -X.
Aufruf: blender -b -P rig_tiger.py -- <toshueyi.glb> <out.glb> [vorschau.png]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
preview = args[2] if len(args) > 2 else None
FINAL = 0.89 / 0.84          # Endmassstab: Wanne 6,28 m, Hoehe 3,1 m, Laenge mit Rohr 8,0 m

big = tc.load(src)
me = big.data
fi, lv, ls, lt = tc.islands(me)
st = tc.island_stats(me, fi, lv, ls, lt)

# ---- Laufwerk: 8 Achsen (verschachtelte Rollen), Antriebsrad vorne, Leitrad hinten ----
# (gemessen an den runden Teilen; Massstab 0,84, vor FINAL)
WHEELS = [(1.96, 0.55, 0.34)]                                    # Index 0: Leitrad hinten (y, z, r)
WHEELS += [(y, 0.48, 0.34) for y in (1.35, 0.81, 0.27, -0.27, -0.81, -1.35, -1.89, -2.43)]   # 1-8: Laufrollen-Achsen
WHEELS += [(-3.13, 0.71, 0.43)]                                  # 9: Antriebsrad vorne

island_group = {}   # Insel -> Turm/Rohr
for i, (mn, mx, n, m) in st.items():
    c = (mn + mx) / 2
    if mx[1] < -1.70 and abs(c[0]) < 0.82 and mn[2] > 1.85:
        island_group[i] = "cannon"                                 # Rohr und Blende
    elif mn[2] >= 1.805 and abs(mn[0]) <= 1.17 and abs(mx[0]) <= 1.17 and mn[1] >= -1.83 and mx[1] <= 1.20 and (mx[1] <= 0.77 or mn[2] >= 1.89):
        island_group[i] = "turret"                                 # ueber dem Wannendach im Turm-Grundriss (Staukasten erst ab 1,9 m)

# Flaechen einzeln: Laufwerk nach der naechsten Radmitte (die verschachtelten Rollen ueberlappen sich)
fc = np.zeros(len(me.polygons) * 3); me.polygons.foreach_get("center", fc); fc = fc.reshape(-1, 3)
fa = np.zeros(len(me.polygons)); me.polygons.foreach_get("area", fa)
names = ["hull", "turret", "cannon"] + [f"rad{s}{k}" for s in "LR" for k in range(10)]
idx = {nm: k for k, nm in enumerate(names)}
face_group = np.zeros(len(fc), dtype=np.int64)
for f in range(len(fc)):
    g = island_group.get(fi[f])
    if g: face_group[f] = idx[g]; continue
    x, y, z = fc[f]
    if not (0.90 < abs(x) < 1.80 and z < 1.2) or fa[f] > 0.06: continue   # grosse Flaechen = Wannenseite/Bugplatte
    best, bd = -1, 1e9
    for k, (wy, wz, wr) in enumerate(WHEELS):
        d = math.hypot(y - wy, z - wz)
        if d <= wr + 0.03 and d / wr < bd: best, bd = k, d / wr
    if best >= 0: face_group[f] = idx[f"rad{'L' if x < 0 else 'R'}{best}"]
from collections import Counter
print("TIGER Flaechen je Teil:", {names[k]: v for k, v in sorted(Counter(face_group.tolist()).items())})
objs = {}
for nm in names:
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    kill = [f for f in bm.faces if face_group[f.index] != idx[nm]]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
    for mat in me.materials: nme.materials.append(mat)
    o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o)
    objs[nm] = o
bpy.data.objects.remove(big)
print("TIGER Teile:", {k: len(v.data.polygons) for k, v in objs.items()})

def bbox(o):
    co = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    return Vector(co.min(0)), Vector(co.max(0))

if preview:
    cols = {"turret": (0.9, 0.2, 0.1, 1), "cannon": (1.0, 0.9, 0.1, 1), "hull": (0.4, 0.4, 0.4, 1)}
    for nm, o in objs.items():
        c = cols.get(nm, (0.1, 0.5, 1.0, 1) if nm[-1] in "02468" else (0.1, 0.9, 0.4, 1))
        if nm.endswith("0") or nm.endswith("9"): c = (0.8, 0.2, 0.9, 1)
        m = bpy.data.materials.new("v_" + nm); m.diffuse_color = c
        o.data.materials.clear(); o.data.materials.append(m)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 800
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("seite", Vector((-15, -0.8, 1.2))), ("schraeg", Vector((-9, -9, 6))), ("hinten", Vector((7, 9, 5)))):
        cam.location = pos; d = Vector((0, -0.8, 1.2)) - pos; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cd.type = "ORTHO" if name == "seite" else "PERSP"; cd.ortho_scale = 10; cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)
    print("TIGER Vorschau fertig"); sys.exit(0)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p

# ---- Laufrollen: weniger Dreiecke (sind keine Panzerung, viele Rundungen) ----
for nm, o in objs.items():
    if nm.startswith("rad"):
        mod = o.modifiers.new("dec", "DECIMATE"); mod.ratio = 0.4
        tc.select_only([o]); bpy.ops.object.modifier_apply(modifier="dec")

# ---- Drehpunkte ----
TPIV = Vector((0.0, -0.42, 1.81))      # Turmmitte auf dem Wannendach (Mitte der Turmbodenplatte)
GPIV = Vector((0.015, -1.55, 2.215))   # Schildzapfen hinter der Blende, auf der Rohrachse
set_origin(objs["turret"], TPIV)
set_origin(objs["cannon"], GPIV)
for s in "LR":
    for k, (wy, wz, wr) in enumerate(WHEELS):
        o = objs[f"rad{s}{k}"]; mn, mx = bbox(o)
        set_origin(o, Vector(((mn.x + mx.x) / 2, wy, wz)))
print("TIGER Radien (vor Endmassstab):", [w[2] for w in WHEELS])

# ---- Bemalung: Dunkelgelb mit Olivgruen/Rotbraun-Tarnung, Schmutz in Ecken, Schlamm unten -> in eine Textur backen ----
SIZE = int(os.environ.get("TIGER_TEX", "4096"))
img = bpy.data.images.new("Tiger_Farbe", SIZE, SIZE, alpha=False)
allobj = list(objs.values())
tc.select_only(allobj)
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.002, area_weight=0.0, scale_to_bounds=True)
bpy.ops.object.mode_set(mode="OBJECT")
print("TIGER UV fertig")

def lin(r, g, b): return tuple(((c / 255) ** 2.2) for c in (r, g, b)) + (1.0,)
PAINT = {"TIGbody": "tarn", "TIGdec4": "tarn", "TIGdec9": "tarn",
         "TIGscre": lin(28, 27, 26), "TIGblack": lin(40, 38, 35), "TIGstrin": lin(58, 48, 40), "TIGtub": lin(92, 52, 30),
         "TIGwood": lin(120, 88, 56), "TIGglass": lin(12, 16, 16), "TIGcross": lin(18, 18, 18)}
for mat in list(me.materials):
    if mat is None: continue
    mat.use_nodes = True
    nt = mat.node_tree; nt.nodes.clear()
    N = nt.nodes.new; L = nt.links.new
    outn = N("ShaderNodeOutputMaterial"); bsdf = N("ShaderNodeBsdfDiffuse"); L(bsdf.outputs[0], outn.inputs[0])
    tex = N("ShaderNodeTexImage"); tex.image = img; nt.nodes.active = tex
    geo = N("ShaderNodeNewGeometry")
    key = next((k for k in PAINT if mat.name.startswith(k)), "TIGbody")
    p = PAINT[key]
    if p == "tarn":
        # Tarnflecken in Weltkoordinaten (laufen ueber Wanne, Turm und Rohr durch)
        def noise(scale, stretch, seed, lo, hi):
            mp = N("ShaderNodeMapping"); mp.inputs["Scale"].default_value = stretch; mp.inputs["Location"].default_value = (seed, seed * 2, 0)
            L(geo.outputs["Position"], mp.inputs[0])
            nz = N("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = scale; nz.inputs["Detail"].default_value = 3; nz.inputs["Roughness"].default_value = 0.55
            L(mp.outputs[0], nz.inputs["Vector"])
            ramp = N("ShaderNodeValToRGB"); ramp.color_ramp.interpolation = "EASE"
            ramp.color_ramp.elements[0].position = lo; ramp.color_ramp.elements[1].position = hi
            L(nz.outputs["Fac"], ramp.inputs[0]); return ramp
        g = noise(0.5, (0.7, 1.0, 1.6), 3.1, 0.52, 0.545); b = noise(0.55, (0.8, 1.0, 1.4), 7.7, 0.55, 0.575)
        mix1 = N("ShaderNodeMix"); mix1.data_type = "RGBA"
        mix1.inputs[6].default_value = lin(176, 152, 104); mix1.inputs[7].default_value = lin(70, 84, 48)     # Dunkelgelb -> Olivgruen
        L(g.outputs[0], mix1.inputs[0])
        mix2 = N("ShaderNodeMix"); mix2.data_type = "RGBA"; mix2.inputs[7].default_value = lin(92, 54, 38)   # -> Rotbraun
        L(mix1.outputs[2], mix2.inputs[6]); L(b.outputs[0], mix2.inputs[0])
        base = mix2.outputs[2]
    else:
        rgb = N("ShaderNodeRGB"); rgb.outputs[0].default_value = p; base = rgb.outputs[0]
    # leichte Fleckigkeit der Farbe
    nz2 = N("ShaderNodeTexNoise"); nz2.inputs["Scale"].default_value = 9; nz2.inputs["Detail"].default_value = 6
    L(geo.outputs["Position"], nz2.inputs["Vector"])
    mot = N("ShaderNodeMix"); mot.data_type = "RGBA"; mot.blend_type = "MULTIPLY"; mot.inputs[0].default_value = 0.25
    L(base, mot.inputs[6]); L(nz2.outputs["Color"], mot.inputs[7])
    # Schmutz in Ecken und Fugen (Umgebungsverdeckung)
    ao = N("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.35; ao.samples = 12
    inv = N("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0; L(ao.outputs["AO"], inv.inputs[1])
    dirtk = N("ShaderNodeMath"); dirtk.operation = "MULTIPLY"; dirtk.inputs[1].default_value = 0.85; L(inv.outputs[0], dirtk.inputs[0])
    dirt = N("ShaderNodeMix"); dirt.data_type = "RGBA"; dirt.inputs[7].default_value = lin(48, 40, 30)
    L(mot.outputs[2], dirt.inputs[6]); L(dirtk.outputs[0], dirt.inputs[0])
    # Schlamm und Staub unten (Laufwerk, untere Wanne), mit Rauschen ausgefranst
    sep = N("ShaderNodeSeparateXYZ"); L(geo.outputs["Position"], sep.inputs[0])
    nz3 = N("ShaderNodeTexNoise"); nz3.inputs["Scale"].default_value = 3.5; nz3.inputs["Detail"].default_value = 5
    L(geo.outputs["Position"], nz3.inputs["Vector"])
    zz = N("ShaderNodeMath"); zz.operation = "MULTIPLY_ADD"; zz.inputs[1].default_value = 0.5; L(nz3.outputs["Fac"], zz.inputs[0]); L(sep.outputs["Z"], zz.inputs[2])
    mr = N("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 1.45; mr.inputs["From Max"].default_value = 0.75
    L(zz.outputs[0], mr.inputs["Value"])
    mud = N("ShaderNodeMix"); mud.data_type = "RGBA"; mud.inputs[7].default_value = lin(96, 80, 58)
    L(dirt.outputs[2], mud.inputs[6]); L(mr.outputs["Result"], mud.inputs[0])
    L(mud.outputs[2], bsdf.inputs["Color"])

sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.samples = 16; sc.cycles.device = "CPU"
sc.render.bake.margin = 6; sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False; sc.render.bake.use_pass_color = True
tc.select_only(allobj)
bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True, margin=6)
print("TIGER gebacken")

# ---- ein Material mit der gebackenen Farbe ----
fin = bpy.data.materials.new("Tiger"); fin.use_nodes = True
b = fin.node_tree.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.78; b.inputs["Metallic"].default_value = 0.0
t = fin.node_tree.nodes.new("ShaderNodeTexImage"); t.image = img
fin.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
for o in allobj:
    o.data.materials.clear(); o.data.materials.append(fin)

# ---- Hierarchie, Endmassstab, Export ----
objs["hull"].name = "Wanne"; objs["hull"].data.name = "Wanne"   # nicht "hull": keine Zerlegung in Anbauteile (Plattensuppe)
for o in allobj:
    o.data.transform(Matrix.Scale(FINAL, 4)); o.location = o.location * FINAL
bpy.context.view_layer.update()
cannon, turret = objs["cannon"], objs["turret"]
cannon.parent = turret; cannon.matrix_parent_inverse = turret.matrix_world.inverted()
root = bpy.data.objects.new("Tiger", None); bpy.context.scene.collection.objects.link(root)
for o in allobj:
    if o.parent is None:
        mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
bpy.context.view_layer.update()
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in allobj)
mn, mx = Vector((9, 9, 9)), Vector((-9, -9, -9))
for o in allobj:
    a, c = bbox(o); a = o.matrix_world @ a; c = o.matrix_world @ c
    mn = Vector(map(min, mn, a, c)); mx = Vector(map(max, mx, a, c))
print(f"TIGER Dreiecke {tris}, Masse min {tuple(round(v, 2) for v in mn)} max {tuple(round(v, 2) for v in mx)}")
print("TIGER Drehpunkte: Turm", tuple(round(v * FINAL, 3) for v in TPIV), "Rohr", tuple(round(v * FINAL, 3) for v in GPIV),
      "Raeder r", [round(w[2] * FINAL, 3) for w in WHEELS[:2]], round(WHEELS[9][2] * FINAL, 3))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=90)
print("TIGER exportiert", out, os.path.getsize(out) // 1024, "KB")

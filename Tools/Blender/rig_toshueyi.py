"""
Riggt und bemalt Panzer-Modelle von Joanthan To (sketchfab.com/jonathanto99, CC BY 4.0) fuer PanzerSim:
"Panzer III Medium Tank - Toshueyi" und "Panzer V Panther Medium Tank".
Die Originale sind ohne Texturen und nach Material zusammengefasst; sortiert wird nach Lage (Einstellungen je Panzer unten):
  hull (-> "Wanne"), turret (Drehpunkt Turmmitte) > cannon (Schildzapfen), radL/radR0..N (0 = hinten), rollL/rollR (Stuetzrollen).
Kette entfaellt (das Spiel baut eine Gliederkette). Bemalung: Dunkelgelb mit Tarnung, Schmutz, Schlamm -> eine Textur.
Aufruf: blender -b -P rig_toshueyi.py -- <panzer3|panther> <in.glb> <out.glb> [vorschau.png]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

CFG = {
    "panzer3": dict(
        S=0.000757, ground=-0.64, remove=("P3TRACKS",),
        idler=(1.87, 0.14, 0.37), road=[(y, -0.31, 0.26) for y in (1.12, 0.57, 0.02, -0.54, -1.09, -1.64)], sprocket=(-2.32, 0.06, 0.42),
        rollers=[(0.71, 0.37, 0.16), (-0.26, 0.37, 0.16), (-1.22, 0.37, 0.16)],
        wheel_x=(0.97, 1.50), wheel_zmax=0.55,
        deck=0.99, rise=0.0, axis_filter=False, turret_x=1.2, turret_y=(-1.45, 1.30), gun_front=-1.05, gun_x=0.4,
        paint={"P3HULL": "tarn", "P3GUN": "tarn", "P3TIRES": (25, 24, 23), "P3WOOD": (120, 88, 56)}),
    "panther": dict(
        S=0.001577, ground=-0.96, remove=("CHAIN",),
        idler=(2.51, -0.26, 0.29), road=[(y, -0.505, 0.41) for y in (1.865, 1.34, 0.83, 0.29, -0.23, -0.77, -1.315, -1.84)], sprocket=(-2.65, -0.20, 0.41),
        rollers=[],
        wheel_x=(0.95, 1.53), wheel_zmax=0.25,
        deck=0.92, rise=1.35, axis_filter=True, decimate=0.55, turret_x=1.22, turret_y=(-1.65, 1.75), gun_front=-1.10, gun_x=0.6,
        paint={"cream": "tarn", "zimerit": "tarn", "rubber": (24, 23, 22), "RUBBER": (24, 23, 22), "metal": (55, 52, 48), "cable": (60, 50, 42),
               "ESPIP": (95, 55, 32), "MG34": (30, 30, 30), "GLASS": (12, 16, 16), "grille": (45, 43, 40), "WOOD": (120, 88, 56),
               "pantcross": (20, 20, 20), "pantdecal": (20, 20, 20), "MAIN": "tarn"}),
}

args = sys.argv[sys.argv.index("--") + 1:]
NAME, src, out = args[0], args[1], args[2]
preview = args[3] if len(args) > 3 else None
C = CFG[NAME]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in tc.meshes():
    if o.parent: mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in tc.meshes(): o.data.transform(Matrix.Scale(C["S"], 4))
tc.select_only(tc.meshes()); bpy.ops.object.join()
big = bpy.context.view_layer.objects.active; me = big.data
mats = [m.name if m else "" for m in me.materials]
fi, lv, ls, lt = tc.islands(me)
st = tc.island_stats(me, fi, lv, ls, lt)
fc = np.zeros(len(me.polygons) * 3); me.polygons.foreach_get("center", fc); fc = fc.reshape(-1, 3)
mi = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("material_index", mi)
remove_m = {k for k, n in enumerate(mats) if any(n.startswith(r) for r in C["remove"])}

WHEELS = [C["idler"]] + C["road"] + [C["sprocket"]]
nW, nR = len(WHEELS), len(C["rollers"])
names = ["hull", "turret", "cannon", "weg"] + [f"rad{s}{k}" for s in "LR" for k in range(nW)] + [f"roll{s}{k}" for s in "LR" for k in range(nR)]
idx = {n: k for k, n in enumerate(names)}

island_group = {}
# Rohrachse aus den vordersten Teilen (Muendung)
ymin = min(s[0][1] for s in st.values())
front = [((s[0] + s[1]) / 2) for s in st.values() if s[0][1] < ymin + 0.3]
AX, AZ = float(np.mean([f[0] for f in front])), float(np.mean([f[2] for f in front]))
print(f"RIG Rohrachse x {AX:.2f} z {AZ:.2f}")
for i, (mn, mx, n, m) in st.items():
    c = (mn + mx) / 2
    if m in remove_m: island_group[i] = "weg"; continue
    on_axis = not C["axis_filter"] or math.hypot(c[0] - AX, c[2] - AZ) < 0.18 or mn[1] > C["gun_front"] - 0.8   # Rohr auf der Achse, Blende direkt vor dem Turm (keine Rohrstuetze auf dem Bug)
    if mx[1] < C["gun_front"] and abs(c[0]) < C["gun_x"] and mn[2] > C["deck"] - 0.15 and mx[2] > C["deck"] + 0.08 and on_axis:   # flache Teile auf Dachhoehe gehoeren zur Wanne
        island_group[i] = "cannon"
    elif mn[2] >= C["deck"] and mx[2] >= C["rise"] and abs(mn[0]) <= C["turret_x"] and abs(mx[0]) <= C["turret_x"] and mn[1] >= C["turret_y"][0] and mx[1] <= C["turret_y"][1]:
        island_group[i] = "turret"

fg = np.zeros(len(fc), dtype=np.int64)
x0, x1 = C["wheel_x"]
for f in range(len(fc)):
    g = island_group.get(fi[f])
    if g: fg[f] = idx[g]; continue
    x, y, z = fc[f]
    if not (x0 < abs(x) < x1 and z < C["wheel_zmax"]): continue
    side = "L" if x < 0 else "R"
    best, bd, kind = -1, 1e9, "rad"
    for k, (wy, wz, wr) in enumerate(WHEELS):
        d = math.hypot(y - wy, z - wz)
        if d <= wr + 0.02 and d / wr < bd: best, bd, kind = k, d / wr, "rad"
    for k, (wy, wz, wr) in enumerate(C["rollers"]):
        d = math.hypot(y - wy, z - wz)
        if d <= wr + 0.02 and d / wr < bd: best, bd, kind = k, d / wr, "roll"
    if best >= 0: fg[f] = idx[f"{kind}{side}{best}"]
from collections import Counter
print("RIG Flaechen:", {names[k]: v for k, v in sorted(Counter(fg.tolist()).items())})

objs = {}
for nm in names:
    if nm == "weg": continue
    sel = fg == idx[nm]
    if not sel.any(): continue
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if fg[f.index] != idx[nm]], context="FACES")
    nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
    for mat in me.materials: nme.materials.append(mat)
    o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o); objs[nm] = o
bpy.data.objects.remove(big)

def bbox(o):
    co = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    return Vector(co.min(0)), Vector(co.max(0))

if preview:
    for nm, o in objs.items():
        c = (0.9, 0.2, 0.1, 1) if nm == "turret" else (1, 0.9, 0.1, 1) if nm == "cannon" else (0.4, 0.4, 0.4, 1) if nm == "hull" else (0.8, 0.2, 0.9, 1) if nm.startswith("roll") else ((0.1, 0.5, 1, 1) if int(nm[4:]) % 2 == 0 else (0.1, 0.9, 0.4, 1))
        m = bpy.data.materials.new("v" + nm); m.diffuse_color = c; o.data.materials.clear(); o.data.materials.append(m)
    sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 800
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("seite", Vector((-14, -0.8, 0.8))), ("schraeg", Vector((-8, -8, 5)))):
        cam.location = pos; d = Vector((0, -0.8, 0.8)) - pos; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cd.type = "ORTHO" if name == "seite" else "PERSP"; cd.ortho_scale = 10; cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)
    print("RIG Vorschau fertig"); sys.exit(0)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p

# ---- Drehpunkte ----
tmn, tmx = bbox(objs["turret"])
TPIV = Vector(((tmn.x + tmx.x) / 2, (tmn.y + tmx.y) / 2, tmn.z))
cmn, cmx = bbox(objs["cannon"])
# Rohrachse: Gerade durch die vorderen 60 % des Rohrs (Mitte je 10-cm-Scheibe); manche Modelle haben das Rohr angehoben
cv = np.array([v.co[:] for v in objs["cannon"].data.vertices])
barrel = cv[cv[:, 1] < cmn.y + (cmx.y - cmn.y) * 0.6]
ys, cx, cz = [], [], []
for y0 in np.arange(barrel[:, 1].min(), barrel[:, 1].max(), 0.1):
    sl = barrel[(barrel[:, 1] >= y0) & (barrel[:, 1] < y0 + 0.1)]
    if len(sl) < 3: continue
    ys.append(y0 + 0.05); cx.append((sl[:, 0].min() + sl[:, 0].max()) / 2); cz.append((sl[:, 2].min() + sl[:, 2].max()) / 2)
sz, iz = np.polyfit(ys, cz, 1); sx, ix = np.polyfit(ys, cx, 1)
yr = cmx.y - 0.1
GPIV = Vector((sx * yr + ix, yr, sz * yr + iz))
elev = math.degrees(math.atan(-sz))
print(f"RIG Rohr im Modell um {elev:.1f} Grad angehoben")
if abs(elev) > 0.5:   # waagerecht drehen (um den Schildzapfen), sonst zielt das Spiel daneben
    R = Matrix.Translation(GPIV) @ Matrix.Rotation(math.radians(elev), 4, "X") @ Matrix.Translation(-GPIV)
    objs["cannon"].data.transform(R)
    cmn, cmx = bbox(objs["cannon"])
set_origin(objs["turret"], TPIV); set_origin(objs["cannon"], GPIV)
for side in "LR":
    for k, (wy, wz, wr) in enumerate(WHEELS):
        o = objs.get(f"rad{side}{k}")
        if o: mn, mx = bbox(o); set_origin(o, Vector(((mn.x + mx.x) / 2, wy, wz)))
    for k, (wy, wz, wr) in enumerate(C["rollers"]):
        o = objs.get(f"roll{side}{k}")
        if o: mn, mx = bbox(o); set_origin(o, Vector(((mn.x + mx.x) / 2, wy, wz)))

# ---- weniger Dreiecke (sehr feine Modelle): Wanne und Turm vereinfachen ----
if C.get("decimate"):
    for nm in ("hull", "turret"):
        o = objs[nm]; mod = o.modifiers.new("dec", "DECIMATE"); mod.ratio = C["decimate"]
        tc.select_only([o]); bpy.ops.object.modifier_apply(modifier="dec")

# ---- Bemalung backen (wie Tiger I: Dunkelgelb mit Olivgruen/Rotbraun, Schmutz in Ecken, Schlamm unten) ----
SIZE = int(os.environ.get("TEX", "2048"))
img = bpy.data.images.new(NAME + "_Farbe", SIZE, SIZE, alpha=False)
allobj = list(objs.values())
tc.select_only(allobj)
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.002, area_weight=0.0, scale_to_bounds=True)
bpy.ops.object.mode_set(mode="OBJECT")
def lin(r, g, b): return tuple(((c / 255) ** 2.2) for c in (r, g, b)) + (1.0,)
G0 = C["ground"]
for mat in list(me.materials):
    if mat is None: continue
    key = next((k for k in C["paint"] if mat.name.startswith(k)), None)
    p = C["paint"].get(key, "tarn") if key else "tarn"
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear(); N = nt.nodes.new; L = nt.links.new
    outn = N("ShaderNodeOutputMaterial"); bsdf = N("ShaderNodeBsdfDiffuse"); L(bsdf.outputs[0], outn.inputs[0])
    tex = N("ShaderNodeTexImage"); tex.image = img; nt.nodes.active = tex
    geo = N("ShaderNodeNewGeometry")
    if p == "tarn":
        def noise(scale, stretch, seed, lo, hi):
            mp = N("ShaderNodeMapping"); mp.inputs["Scale"].default_value = stretch; mp.inputs["Location"].default_value = (seed, seed * 2, 0)
            L(geo.outputs["Position"], mp.inputs[0])
            nz = N("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = scale; nz.inputs["Detail"].default_value = 3; nz.inputs["Roughness"].default_value = 0.55
            L(mp.outputs[0], nz.inputs["Vector"])
            ramp = N("ShaderNodeValToRGB"); ramp.color_ramp.interpolation = "EASE"
            ramp.color_ramp.elements[0].position = lo; ramp.color_ramp.elements[1].position = hi
            L(nz.outputs["Fac"], ramp.inputs[0]); return ramp
        g = noise(0.55, (0.7, 1.0, 1.6), 5.3, 0.52, 0.545); b = noise(0.6, (0.8, 1.0, 1.4), 9.1, 0.55, 0.575)
        m1 = N("ShaderNodeMix"); m1.data_type = "RGBA"; m1.inputs[6].default_value = lin(176, 152, 104); m1.inputs[7].default_value = lin(70, 84, 48)
        L(g.outputs[0], m1.inputs[0])
        m2 = N("ShaderNodeMix"); m2.data_type = "RGBA"; m2.inputs[7].default_value = lin(92, 54, 38)
        L(m1.outputs[2], m2.inputs[6]); L(b.outputs[0], m2.inputs[0]); base = m2.outputs[2]
    else:
        rgb = N("ShaderNodeRGB"); rgb.outputs[0].default_value = lin(*p); base = rgb.outputs[0]
    nz2 = N("ShaderNodeTexNoise"); nz2.inputs["Scale"].default_value = 9; nz2.inputs["Detail"].default_value = 6; L(geo.outputs["Position"], nz2.inputs["Vector"])
    mot = N("ShaderNodeMix"); mot.data_type = "RGBA"; mot.blend_type = "MULTIPLY"; mot.inputs[0].default_value = 0.22
    L(base, mot.inputs[6]); L(nz2.outputs["Color"], mot.inputs[7])
    ao = N("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.3; ao.samples = 12
    inv = N("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0; L(ao.outputs["AO"], inv.inputs[1])
    dk = N("ShaderNodeMath"); dk.operation = "MULTIPLY"; dk.inputs[1].default_value = 0.85; L(inv.outputs[0], dk.inputs[0])
    dirt = N("ShaderNodeMix"); dirt.data_type = "RGBA"; dirt.inputs[7].default_value = lin(48, 40, 30); L(mot.outputs[2], dirt.inputs[6]); L(dk.outputs[0], dirt.inputs[0])
    sep = N("ShaderNodeSeparateXYZ"); L(geo.outputs["Position"], sep.inputs[0])
    nz3 = N("ShaderNodeTexNoise"); nz3.inputs["Scale"].default_value = 3.5; nz3.inputs["Detail"].default_value = 5; L(geo.outputs["Position"], nz3.inputs["Vector"])
    zz = N("ShaderNodeMath"); zz.operation = "MULTIPLY_ADD"; zz.inputs[1].default_value = 0.5; L(nz3.outputs["Fac"], zz.inputs[0]); L(sep.outputs["Z"], zz.inputs[2])
    mr = N("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = G0 + 1.25; mr.inputs["From Max"].default_value = G0 + 0.55; L(zz.outputs[0], mr.inputs["Value"])
    mud = N("ShaderNodeMix"); mud.data_type = "RGBA"; mud.inputs[7].default_value = lin(96, 80, 58); L(dirt.outputs[2], mud.inputs[6]); L(mr.outputs["Result"], mud.inputs[0])
    L(mud.outputs[2], bsdf.inputs["Color"])
sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.samples = 16; sc.cycles.device = "CPU"
sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False; sc.render.bake.use_pass_color = True
tc.select_only(allobj)
bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True, margin=6)
fin = bpy.data.materials.new(NAME); fin.use_nodes = True
bp = fin.node_tree.nodes["Principled BSDF"]; bp.inputs["Roughness"].default_value = 0.78
t = fin.node_tree.nodes.new("ShaderNodeTexImage"); t.image = img; fin.node_tree.links.new(t.outputs["Color"], bp.inputs["Base Color"])
for o in allobj: o.data.materials.clear(); o.data.materials.append(fin)

# ---- Boden auf 0, Hierarchie, Export ----
for o in allobj: o.location = o.location + Vector((0, 0, -G0))   # nur verschieben (Netze liegen relativ zum Ursprung)
bpy.context.view_layer.update()
objs["cannon"].parent = objs["turret"]; objs["cannon"].matrix_parent_inverse = objs["turret"].matrix_world.inverted()
root = bpy.data.objects.new(NAME, None); bpy.context.scene.collection.objects.link(root)
for o in allobj:
    if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
objs["hull"].name = "Wanne"; objs["hull"].data.name = "Wanne"
bpy.context.view_layer.update()
mn, mx = Vector((9, 9, 9)), Vector((-9, -9, -9))
for o in allobj:
    a, b = bbox(o); a = o.matrix_world @ a; b = o.matrix_world @ b
    mn = Vector(map(min, mn, a, b)); mx = Vector(map(max, mx, a, b))
wmn, wmx = bbox(objs["hull"])
print(f"RIG Masse min {tuple(round(v, 2) for v in mn)} max {tuple(round(v, 2) for v in mx)}; Wanne y {wmn.y:.2f}..{wmx.y:.2f} z {wmn.z - G0:.2f}..{wmx.z - G0:.2f} x {wmn.x:.2f}..{wmx.x:.2f}")
print(f"RIG Turm {tuple(round(v, 3) for v in (TPIV.x, TPIV.y, TPIV.z - G0))} Rohr {tuple(round(v, 3) for v in (GPIV.x, GPIV.y, GPIV.z - G0))} Muendung y {cmn.y:.2f}")
print("RIG Raeder", [(round(w[1] - G0, 3), w[2]) for w in WHEELS], "Rollen", [(round(r[1] - G0, 3), r[2]) for r in C["rollers"]])
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in allobj)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=90)
print("RIG Dreiecke", tris, "exportiert", out, os.path.getsize(out) // 1024, "KB")

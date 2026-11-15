"""
Riggt "M3 Grant Medium Tank - Toshueyi" (Joanthan To, sketchfab.com/Toshueyi, CC BY 4.0) fuer PanzerSim.
Original: ohne Texturen, 67 Teile nach Material (Tower, 75mmCan, Track ...), Einheiten ~0,69 mm, vorne = +Y.
  - Hauptwaffe: 75 mm M3 im Erker rechts (schwenkt begrenzt): "turret" ist nur ein Drehpunkt, "cannon" = Rohr + Kugelblende
  - Turm mit 37-mm-Kanone: Lafette "turm37" (dreht rundum zum Zielpunkt) > "kanone37" (Hoehe), traegt das MG
  - Kette entfaellt (Gliederkette im Spiel); Laufwerk VVSS: Leitrad hinten (radL0), 6 Laufrollen, Antriebsrad vorne (radL7),
    3 Stuetzrollen je Seite (rollL0..2, 0 = hinten)
  - Bemalung: britisches Wuestenbraun (Light Stone) mit Schmutz und Schlamm, in eine Textur gebacken
Konvention wie die anderen Rigs: vorne = -Y, oben = Z, Boden bei z = 0.
Aufruf: blender -b -P rig_grant.py -- <m3_grant.glb> <out.glb> [vorschau.png]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
preview = args[2] if len(args) > 2 else None
S = 0.00069

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
bpy.context.view_layer.update()
for o in tc.meshes():
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
tc.select_only(tc.meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
M = Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Scale(S, 4)    # vorne +Y -> -Y
TEIL = {"75mmCan": 1, "Tower": 2, "Track": 9, "Grey": 9}   # Grey = Kettenzaehne/-bolzen
for o in tc.meshes():
    o.data.transform(M)
    t = TEIL.get(o.data.materials[0].name if o.data.materials else "", 0)
    a = o.data.attributes.new("teil", "INT", "FACE"); a.data.foreach_set("value", [t] * len(o.data.polygons))
tc.select_only(tc.meshes()); bpy.ops.object.join()
big = bpy.context.view_layer.objects.active; me = big.data
co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
shift = Vector((-(co[:, 0].min() + co[:, 0].max()) / 2, 0, -co[:, 2].min()))
me.transform(Matrix.Translation(shift)); co += np.array(shift)
mats = [m.name if m else "" for m in me.materials]
teil = np.zeros(len(me.polygons), dtype=np.int64); me.attributes["teil"].data.foreach_get("value", teil)
fc = np.zeros(len(me.polygons) * 3); me.polygons.foreach_get("center", fc); fc = fc.reshape(-1, 3)
fv = [tuple(p.vertices) for p in me.polygons]
print(f"GR Masse {co.min(0).round(2)} .. {co.max(0).round(2)}")

# ---- Laufwerk nachmessen (Startwerte aus der Seitenansicht) ----
START = [(2.76, 0.65, 0.27)] + [(y, 0.33, 0.255) for y in (2.006, 1.221, 0.613, -0.176, -0.780, -1.579)] + [(-1.97, 0.65, 0.31)]
ROLL0 = [(1.625, 0.88, 0.09), (0.232, 0.88, 0.09), (-1.184, 0.88, 0.09)]
nontrack = set()
for f in np.nonzero(teil != 9)[0]: nontrack.update(fv[f])
nv = co[sorted(nontrack)]
def measure(lst, sgn, xmin):
    side = nv[(nv[:, 0] * sgn > xmin) & (nv[:, 2] < 1.2)]
    res = []
    for y0, z0, r0 in lst:
        for _ in range(4):
            d = np.hypot(side[:, 1] - y0, side[:, 2] - z0)
            v = side[d < r0 * 1.02]
            if len(v) < 10: break
            y0, z0 = (v[:, 1].min() + v[:, 1].max()) / 2, (v[:, 2].min() + v[:, 2].max()) / 2
        v = side[np.hypot(side[:, 1] - y0, side[:, 2] - z0) < r0 * 0.98]
        # Radbreite: aeusserer Teil (Lauffläche); innen sitzen die Federtraeger
        res.append([float(y0), float(z0), float(r0), float(np.abs(v[:, 0]).min()), float(np.abs(v[:, 0]).max())])
    return res
ST = {"L": measure(START, 1, 0.6), "R": measure(START, -1, 0.6)}      # vorne -Y: links = +X
RO = {"L": measure(ROLL0, 1, 0.6), "R": measure(ROLL0, -1, 0.6)}
for s in "LR":
    print("GR", s, "Raeder", [(round(w[0], 3), round(w[1], 3), round(w[3], 2), round(w[4], 2)) for w in ST[s]])
    print("GR", s, "Rollen", [(round(w[0], 3), round(w[1], 3), round(w[3], 2), round(w[4], 2)) for w in RO[s]])

# ---- 75 mm: Rohrachse aus der Muendung ----
gv = set()
for f in np.nonzero(teil == 1)[0]: gv.update(fv[f])
gv = co[sorted(gv)]
tip = gv[gv[:, 1] < gv[:, 1].min() + 0.5]
AX, AZ = float((tip[:, 0].min() + tip[:, 0].max()) / 2), float((tip[:, 2].min() + tip[:, 2].max()) / 2)
body = gv[np.hypot(gv[:, 0] - AX, gv[:, 2] - AZ) > 0.09]       # Kugelblende ohne Rohr
GPIV = Vector((AX, float(body[:, 1].max() - (body[:, 1].max() - body[:, 1].min()) * 0.45), AZ))
print(f"GR 75 mm Achse x {AX:.3f} z {AZ:.3f}, Muendung y {gv[:, 1].min():.2f}, Blende y {body[:, 1].min():.2f}..{body[:, 1].max():.2f}")

# ---- Turm (37 mm) ----
tw = set()
for f in np.nonzero(teil == 2)[0]: tw.update(fv[f])
tw = co[sorted(tw)]
TZ0 = float(tw[:, 2].min())
low = tw[tw[:, 2] < TZ0 + 0.25]
TPIV = Vector(((low[:, 0].min() + low[:, 0].max()) / 2, (low[:, 1].min() + low[:, 1].max()) / 2, TZ0))
TY0 = float(tw[:, 1].min())
TX = (float(tw[:, 0].min()), float(tw[:, 0].max())); TYR = (float(tw[:, 1].min()), float(tw[:, 1].max()))
# 37-mm-Rohr: duenne Teile vor dem Turm in Rohrhoehe
fr = (fc[:, 1] < TY0 - 0.05) & (fc[:, 2] > 2.3) & (fc[:, 2] < 2.8) & (teil != 1)
bv = set()
for f in np.nonzero(fr)[0]: bv.update(fv[f])
bv = co[sorted(bv)]
tip2 = bv[bv[:, 1] < bv[:, 1].min() + 0.4]
AX2, AZ2 = float((tip2[:, 0].min() + tip2[:, 0].max()) / 2), float((tip2[:, 2].min() + tip2[:, 2].max()) / 2)
G2PIV = Vector((AX2, TY0 + 0.18, AZ2))
print(f"GR Turm Drehpunkt {tuple(round(v, 3) for v in TPIV)}, Front y {TY0:.2f}; 37 mm Achse x {AX2:.3f} z {AZ2:.3f}, Muendung y {bv[:, 1].min():.2f}")

# ---- Flaechen verteilen ----
nW, nR = len(START), len(ROLL0)
names = ["Wanne", "cannon", "turm37", "kanone37"] + [f"rad{s}{k}" for s in "LR" for k in range(nW)] + [f"roll{s}{k}" for s in "LR" for k in range(nR)]
idx = {n: k for k, n in enumerate(names)}
fg = np.zeros(len(fv), dtype=np.int64)
XB = {"rad": [(0.85, 1.22)] + [(0.80, 1.25)] * 6 + [(0.90, 1.36)]}   # Radbreiten (innen sitzen Federtraeger und Achsantrieb)
cx, cy = float(TPIV.x), float(TPIV.y)
rx, ry = (TX[1] - TX[0]) / 2 + 0.03, (TYR[1] - TYR[0]) / 2 + 0.03
def on_track(p, s):
    """Liegt das Flaechenstueck ganz im Band der (entfernten) Kette? Unten, oben, um Leitrad und Antriebsrad."""
    ax = np.abs(p[:, 0])
    if ax.min() < 0.70 or ax.max() > 1.27: return False
    if p[:, 2].max() < 0.235: return True   # Fuehrungszaehne ragen zwischen die Doppelrollen
    iy, iz, ir = ST[s][0][:3]; sy, sz, sr = ST[s][-1][:3]
    if (p[:, 2].min() > 0.92) and (p[:, 2].max() < 1.12) and p[:, 1].min() > sy and p[:, 1].max() < iy: return True
    # schraeg ansteigende Stuecke: Leitrad -> erste Laufrolle, letzte Laufrolle -> Antriebsrad (Unterkanten verbinden)
    for a, b in ((ST[s][0], ST[s][1]), (ST[s][-2], ST[s][-1])):
        ya, za, yb, zb = a[0], a[1] - a[2], b[0], b[1] - b[2]
        lo, hi = min(ya, yb), max(ya, yb)
        if p[:, 1].min() >= lo and p[:, 1].max() <= hi:
            zl = za + (p[:, 1] - ya) * (zb - za) / (yb - ya)
            if ((p[:, 2] - zl).min() > -0.10) and ((p[:, 2] - zl).max() < 0.27): return True
    for cy_, cz_, r_, out in ((iy, iz, ir, 1), (sy, sz, sr, -1)):
        d = np.hypot(p[:, 1] - cy_, p[:, 2] - cz_)
        if d.min() > r_ * 0.80 and d.max() < r_ + 0.16 and ((p[:, 1] - cy_) * out).min() > -0.05: return True
    return False

for f in range(len(fv)):
    x, y, z = fc[f]; t = teil[f]
    if t == 9: fg[f] = -1; continue
    if t == 1: fg[f] = idx["cannon"]; continue
    near37 = abs(x - AX2) < 0.30 and 2.28 < z < 2.85
    if (y < TY0 - 0.05 and near37) or (t == 2 and y < TY0 + 0.16 and near37):
        fg[f] = idx["kanone37"]; continue
    in_turret = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 < 1.0
    if t == 2 or (in_turret and z > TZ0 + 0.12):
        fg[f] = idx["turm37"]; continue
    if z < 1.2 and abs(x) > 0.6:
        s = "L" if x > 0 else "R"; done = False
        pv_ = co[list(fv[f])]
        for lst, kind in ((ST[s], "rad"), (RO[s], "roll")):
            for k, (wy, wz, wr, x0, x1) in enumerate(lst):
                xin = XB[kind][k] if kind == "rad" else (0.80, 1.36)
                if xin[0] <= abs(x) <= xin[1] and np.hypot(pv_[:, 1] - wy, pv_[:, 2] - wz).max() <= wr * 1.03 + 0.01:
                    fg[f] = idx[f"{kind}{s}{k}"]; done = True; break
            if done: break
        if not done and fg[f] == 0 and on_track(pv_, s):
            fg[f] = -1          # Kettenzaehne (Material Brown, mit den Federtraegern vermischt)
from collections import Counter
print("GR Flaechen:", {("weg" if k < 0 else names[k]): v for k, v in sorted(Counter(fg.tolist()).items())})

objs = {}
for k, nm in enumerate(names):
    if not (fg == k).any(): print("GR leer:", nm); continue
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if fg[f.index] != k], context="FACES")
    nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
    for mat in me.materials: nme.materials.append(mat)
    if "teil" in nme.attributes: nme.attributes.remove(nme.attributes["teil"])
    o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o); objs[nm] = o
bpy.data.objects.remove(big)

def bbox(o):
    c = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", c); c = c.reshape(-1, 3)
    return Vector(c.min(0)), Vector(c.max(0))

if preview:
    for nm, o in objs.items():
        c = ((0.9, 0.3, 0.1, 1) if nm == "turm37" else (1, 0.9, 0.1, 1) if nm == "cannon" else (1, 0.2, 0.8, 1) if nm == "kanone37"
             else (0.5, 0.5, 0.5, 1) if nm == "Wanne" else (0.8, 0.2, 0.9, 1) if nm.startswith("roll") else ((0.1, 0.5, 1, 1) if int(nm[4:]) % 2 == 0 else (0.1, 0.9, 0.4, 1)))
        m = bpy.data.materials.new("v" + nm); m.diffuse_color = c; o.data.materials.clear(); o.data.materials.append(m)
    sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 800
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("seite", Vector((-14, 0.3, 1.3))), ("schraeg", Vector((-7, -7, 5))), ("links", Vector((14, 0.3, 1.3)))):
        cam.location = pos; d = Vector((0, 0.3, 1.3)) - pos; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cd.type = "PERSP" if name == "schraeg" else "ORTHO"; cd.ortho_scale = 6.5; cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)
    print("GR Vorschau fertig"); sys.exit(0)

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p

def level(o, piv):
    """Rohr waagerecht drehen (um den Schildzapfen), falls es im Modell angehoben ist."""
    cv = np.array([v.co[:] for v in o.data.vertices]); mn = cv[:, 1].min()
    barrel = cv[cv[:, 1] < mn + (piv.y - mn) * 0.7]
    ys, cz = [], []
    for y0 in np.arange(barrel[:, 1].min(), barrel[:, 1].max(), 0.08):
        sl = barrel[(barrel[:, 1] >= y0) & (barrel[:, 1] < y0 + 0.08)]
        if len(sl) >= 3: ys.append(y0 + 0.04); cz.append((sl[:, 2].min() + sl[:, 2].max()) / 2)
    if len(ys) < 3: return
    sz, _ = np.polyfit(ys, cz, 1); el = math.degrees(math.atan(-sz))
    print(f"GR {o.name}: Rohr um {el:.1f} Grad angehoben")
    if abs(el) > 0.5:
        o.data.transform(Matrix.Translation(piv) @ Matrix.Rotation(math.radians(el), 4, "X") @ Matrix.Translation(-piv))
level(objs["cannon"], GPIV); level(objs["kanone37"], G2PIV)
set_origin(objs["cannon"], GPIV); set_origin(objs["turm37"], TPIV); set_origin(objs["kanone37"], G2PIV)
for s in "LR":
    for lst, kind in ((ST[s], "rad"), (RO[s], "roll")):
        for k, (wy, wz, wr, x0, x1) in enumerate(lst):
            o = objs.get(f"{kind}{s}{k}")
            if o: mn, mx = bbox(o); set_origin(o, Vector(((mn.x + mx.x) / 2, wy, wz)))
turret = bpy.data.objects.new("turret", None); bpy.context.scene.collection.objects.link(turret); turret.location = GPIV

# ---- weniger Dreiecke ----
for nm, o in objs.items():
    if nm in ("cannon", "kanone37"): continue   # Rohre nicht vereinfachen (zerfallen sonst in Streifen)
    md = o.modifiers.new("dec", "DECIMATE"); md.ratio = 0.45 if nm == "Wanne" else 0.6
    md.use_collapse_triangulate = True
    tc.select_only([o]); bpy.ops.object.modifier_apply(modifier="dec")

# ---- Bemalung backen: Light Stone (britisch, Wueste), Gummi/Metall/Glas eigen, Schmutz in Ecken, Schlamm unten ----
SIZE = int(os.environ.get("TEX", 4096))
img = bpy.data.images.new("Grant_Farbe", SIZE, SIZE, alpha=False)
allobj = list(objs.values())
tc.select_only(allobj)
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.002, area_weight=0.0, scale_to_bounds=True)
bpy.ops.object.mode_set(mode="OBJECT")
def lin(r, g, b): return tuple(((c / 255) ** 2.2) for c in (r, g, b)) + (1.0,)
PAINT = {"Black": (28, 27, 26), "Chrome": (70, 68, 64), "Glass": (14, 18, 18), "GlassOp": (14, 18, 18), "Cage": (60, 56, 48), "Green": (40, 40, 36)}
SAND = (196, 172, 126)
for mat in list(me.materials):
    if mat is None: continue
    p = PAINT.get(mat.name, SAND)
    mat.use_nodes = True; nt = mat.node_tree; nt.nodes.clear(); N = nt.nodes.new; L = nt.links.new
    outn = N("ShaderNodeOutputMaterial"); bsdf = N("ShaderNodeBsdfDiffuse"); L(bsdf.outputs[0], outn.inputs[0])
    tex = N("ShaderNodeTexImage"); tex.image = img; nt.nodes.active = tex
    geo = N("ShaderNodeNewGeometry")
    rgb = N("ShaderNodeRGB"); rgb.outputs[0].default_value = lin(*p); base = rgb.outputs[0]
    # leichte Farbschwankung im Lack (ausgeblichen / nachgestrichen)
    nz1 = N("ShaderNodeTexNoise"); nz1.inputs["Scale"].default_value = 1.2; nz1.inputs["Detail"].default_value = 4; L(geo.outputs["Position"], nz1.inputs["Vector"])
    var = N("ShaderNodeMix"); var.data_type = "RGBA"; var.blend_type = "MULTIPLY"; var.inputs[0].default_value = 0.10 if p == SAND else 0.0
    L(base, var.inputs[6]); L(nz1.outputs["Color"], var.inputs[7])
    nz2 = N("ShaderNodeTexNoise"); nz2.inputs["Scale"].default_value = 9; nz2.inputs["Detail"].default_value = 6; L(geo.outputs["Position"], nz2.inputs["Vector"])
    mot = N("ShaderNodeMix"); mot.data_type = "RGBA"; mot.blend_type = "MULTIPLY"; mot.inputs[0].default_value = 0.18
    L(var.outputs[2], mot.inputs[6]); L(nz2.outputs["Color"], mot.inputs[7])
    ao = N("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.3; ao.samples = 12
    inv = N("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0; L(ao.outputs["AO"], inv.inputs[1])
    dk = N("ShaderNodeMath"); dk.operation = "MULTIPLY"; dk.inputs[1].default_value = 0.8; L(inv.outputs[0], dk.inputs[0])
    dirt = N("ShaderNodeMix"); dirt.data_type = "RGBA"; dirt.inputs[7].default_value = lin(70, 58, 42); L(mot.outputs[2], dirt.inputs[6]); L(dk.outputs[0], dirt.inputs[0])
    sep = N("ShaderNodeSeparateXYZ"); L(geo.outputs["Position"], sep.inputs[0])
    nz3 = N("ShaderNodeTexNoise"); nz3.inputs["Scale"].default_value = 3.5; nz3.inputs["Detail"].default_value = 5; L(geo.outputs["Position"], nz3.inputs["Vector"])
    zz = N("ShaderNodeMath"); zz.operation = "MULTIPLY_ADD"; zz.inputs[1].default_value = 0.5; L(nz3.outputs["Fac"], zz.inputs[0]); L(sep.outputs["Z"], zz.inputs[2])
    mr = N("ShaderNodeMapRange"); mr.inputs["From Min"].default_value = 1.25; mr.inputs["From Max"].default_value = 0.55; L(zz.outputs[0], mr.inputs["Value"])
    mud = N("ShaderNodeMix"); mud.data_type = "RGBA"; mud.inputs[7].default_value = lin(150, 126, 92); L(dirt.outputs[2], mud.inputs[6]); L(mr.outputs["Result"], mud.inputs[0])
    L(mud.outputs[2], bsdf.inputs["Color"])
sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.samples = 16; sc.cycles.device = "CPU"
sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False; sc.render.bake.use_pass_color = True
tc.select_only(allobj)
bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_clear=True, margin=6)
fin = bpy.data.materials.new("Grant"); fin.use_nodes = True
bp = fin.node_tree.nodes["Principled BSDF"]; bp.inputs["Roughness"].default_value = 0.8
t = fin.node_tree.nodes.new("ShaderNodeTexImage"); t.image = img; fin.node_tree.links.new(t.outputs["Color"], bp.inputs["Base Color"])
for o in allobj: o.data.materials.clear(); o.data.materials.append(fin)

# ---- Hierarchie, Export ----
bpy.context.view_layer.update()
def parent(ch, pa): ch.parent = pa; ch.matrix_parent_inverse = pa.matrix_world.inverted()
parent(objs["cannon"], turret); parent(objs["kanone37"], objs["turm37"])
root = bpy.data.objects.new("M3Grant", None); bpy.context.scene.collection.objects.link(root)
for o in allobj + [turret]:
    if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
bpy.context.view_layer.update()
w = np.array([v.co[:] for v in objs["Wanne"].data.vertices])
print(f"GR Wanne x {w[:, 0].min():.2f}..{w[:, 0].max():.2f} y {w[:, 1].min():.2f}..{w[:, 1].max():.2f} z {w[:, 2].min():.2f}..{w[:, 2].max():.2f}")
print("GR Drehpunkte 75 mm", tuple(round(v, 3) for v in GPIV), "Turm", tuple(round(v, 3) for v in TPIV), "37 mm", tuple(round(v, 3) for v in G2PIV))
print("GR Dreiecke", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in allobj))
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_jpeg_quality=90)
print("GR exportiert", out, os.path.getsize(out) // 1024, "KB")

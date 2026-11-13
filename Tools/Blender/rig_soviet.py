"""
Riggt "Soviet Heavy Tanks" und "Soviet Medium Tanks" (snrnsrk5, sketchfab.com/snrnsrk5, CC BY 4.0) fuer PanzerSim:
KV-1, IS-1, IS-2, IS-3, T-10, T-10M bzw. T-34, T-34-85, T-44, T-44-100, T-54, T-55A. Jeder Panzer: Hull, Turret, Track (je eigene PBR-Texturen).
Im Original stehen alle sechs nebeneinander, leicht gedreht; Meter, echte Groesse.
  - auf eigenen Ursprung gebracht und gerade gedreht (vorne = -Y, wie die anderen Modelle)
  - Kette entfaellt: das Spiel baut eine Gliederkette
  - Raeder (in der Wanne): Inseln innerhalb der Radscheiben -> radL0..N / radR0..N (0 = hinten), Stuetzrollen rollL/rollR
  - Rohr + Blende aus dem Turm -> cannon
  - Texturen auf 1024 verkleinert
Aufruf: blender -b -P rig_soviet.py -- <soviet_heavy_tanks.glb bzw. soviet_medium_tanks.glb> <ausgabe-ordner> [vorschau-ordner]
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tiger_common as tc

args = sys.argv[sys.argv.index("--") + 1:]
src, outdir = args[0], args[1]
preview = args[2] if len(args) > 2 and args[2] != "-" else None   # "-" = keine Vorschau
ONLY = args[3].split(",") if len(args) > 3 else None

# Panzer aus den Knotennamen ("<Name> Hull"); Dateiname im Spiel = Name ohne Bindestriche (T-34-85 -> T3485)
MANTLET = 0.10   # Blende: Inseln nahe der Rohrachse, deren Hinterkante hoechstens so weit hinter dem Rohrende liegt (m)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
TANKS = {o.name[:-5]: o.name[:-5].replace("-", "") for o in bpy.data.objects if o.name.endswith(" Hull")}
print("SV Panzer:", TANKS)

# Ursprung und Drehung je Panzer aus dem Wannen-Knoten
frames = {}
for t in TANKS:
    l, r, s = bpy.data.objects[f"{t} Hull"].matrix_world.decompose()
    frames[t] = Matrix.Rotation(-r.to_euler().z, 4, "Z") @ Matrix.Translation(-l)
# Turmgelenk des Autors (Mitte des Turmrings)
ring = {t: frames[t] @ bpy.data.objects[f"{t} Turret"].matrix_world.translation for t in TANKS}
parts = {}
for o in list(bpy.data.objects):
    if o.type != "MESH": continue
    t, kind = o.name.split(" ")[0], o.name.split(" ")[1].split("_")[0]
    o.data.transform(frames[t] @ o.matrix_world)
    parts[(t, kind)] = o
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)
    else: o.parent = None; o.matrix_world = Matrix.Identity(4)
for (t, kind), o in list(parts.items()):
    if kind == "Track": bpy.data.objects.remove(o); del parts[(t, kind)]

if not preview:
    for img in bpy.data.images:
        if img.size[0] > 1024: img.scale(1024, 1024 * img.size[1] // img.size[0])

def stats_of(o):
    me = o.data
    fi, lv, ls, lt = tc.islands(me)
    return fi, tc.island_stats(me, fi, lv, ls, lt)

def bbox(o):
    co = np.zeros(len(o.data.vertices) * 3); o.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    return Vector(co.min(0)), Vector(co.max(0))

def split(src_obj, fg, names):
    me = src_obj.data; objs = {}
    for k, nm in enumerate(names):
        if nm is None or not (fg == k).any(): continue
        bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if fg[f.index] != k], context="FACES")
        nme = bpy.data.meshes.new(nm); bm.to_mesh(nme); bm.free()
        for mat in me.materials: nme.materials.append(mat)
        o = bpy.data.objects.new(nm, nme); bpy.context.scene.collection.objects.link(o); objs[nm] = o
    return objs

def set_origin(o, p):
    o.data.transform(Matrix.Translation(-p)); o.location = p

summary = []
for t, game in TANKS.items():
    if ONLY and t not in ONLY: continue
    hull, tur = parts[(t, "Hull")], parts[(t, "Turret")]
    hmn, hmx = bbox(hull)
    cx = (hmn.x + hmx.x) / 2
    # ---- Radscheiben -> Stationen ----
    fi, st = stats_of(hull)
    discs = []
    for i, (mn, mx, n, m) in st.items():
        s = mx - mn; c = (mn + mx) / 2
        if s[1] > 0.22 and abs(s[1] - s[2]) < 0.08 * s[1] and s[0] < 0.45 and abs(c[0] - cx) > 0.7:
            discs.append((c, s[1] / 2, mn[0], mx[0]))
    stations = []   # [seite, y, z, r, xmin, xmax]
    for c, r, x0, x1 in sorted(discs, key=lambda d: -d[1]):
        side = -1 if c[0] < cx else 1
        for sta in stations:
            if sta[0] == side and math.hypot(sta[1] - c[1], sta[2] - c[2]) < 0.8 * sta[3]:
                sta[4] = min(sta[4], x0); sta[5] = max(sta[5], x1); break
        else: stations.append([side, c[1], c[2], r, x0, x1])
    big = [s for s in stations if s[3] > 0.2]
    zroad = min(s[2] for s in big)
    road = [s for s in big if s[2] < zroad + 0.12]
    ry0, ry1 = min(s[1] for s in road), max(s[1] for s in road)
    ends_f = [s for s in big if s[1] < ry0 - 0.2]; ends_r = [s for s in big if s[1] > ry1 + 0.2]
    rollers = [s for s in stations if s[2] > zroad + 0.3 and ry0 - 0.1 < s[1] < ry1 + 0.1 and s[3] < 0.22]
    W, R = {}, {}
    for side, sn in ((-1, "L"), (1, "R")):
        ws = sorted([s for s in road if s[0] == side], key=lambda s: -s[1])
        rear = max([s for s in ends_r if s[0] == side], key=lambda s: s[3])
        front = max([s for s in ends_f if s[0] == side], key=lambda s: s[3])
        W[sn] = [rear] + ws + [front]
        R[sn] = sorted([s for s in rollers if s[0] == side], key=lambda s: -s[1])
        print(f"SV {t} {sn} Raeder", [(round(s[1], 2), round(s[2], 2), round(s[3], 3)) for s in W[sn]], "Stuetzrollen", [(round(s[1], 2), round(s[2], 2), round(s[3], 3)) for s in R[sn]])
    # ---- Wannen-Inseln den Raedern zuordnen ----
    names = ["Wanne"]; idx = {}
    for sn in "LR":
        for k, s in enumerate(W[sn]): idx[id(s)] = len(names); names.append(f"rad{sn}{k}")
        for k, s in enumerate(R[sn]): idx[id(s)] = len(names); names.append(f"roll{sn}{k + 1}")
    allst = [s for sn in "LR" for s in W[sn] + R[sn]]
    island_to = {}
    for i, (mn, mx, n, m) in st.items():
        c = (mn + mx) / 2; ext = max(mx[1] - mn[1], mx[2] - mn[2]) / 2
        for s in allst:
            if s[4] - 0.12 <= c[0] <= s[5] + 0.12 and math.hypot(c[1] - s[1], c[2] - s[2]) + ext <= s[3] * 1.1 + 0.01:
                island_to[i] = idx[id(s)]; break
    fg = np.array([island_to.get(i, 0) for i in fi], dtype=np.int64)
    hobjs = split(hull, fg, names)
    bpy.data.objects.remove(hull)
    # ---- Kanone aus dem Turm ----
    fi, st = stats_of(tur)
    tmn0, tmx0 = bbox(tur); tcx = (tmn0.x + tmx0.x) / 2
    barrel = max((v for v in st.values() if (v[1] - v[0])[0] < 0.4 and (v[1] - v[0])[2] < 0.4 and abs((v[0][0] + v[1][0]) / 2 - tcx) < 0.25
                  and v[1][2] < tmx0.z - 0.3), key=lambda v: (v[1] - v[0])[1])
    bx = (barrel[0][0] + barrel[1][0]) / 2; bz = (barrel[0][2] + barrel[1][2]) / 2; brear = barrel[1][1]
    # Rohr aus mehreren Stuecken (z. B. Rauchabsauger): weitere schlanke Teile auf der Achse anhaengen
    axis = sorted((v for v in st.values() if (v[1] - v[0])[0] < 0.4 and (v[1] - v[0])[2] < 0.4
                   and abs((v[0][0] + v[1][0]) / 2 - bx) < 0.06 and abs((v[0][2] + v[1][2]) / 2 - bz) < 0.06), key=lambda v: v[0][1])
    for v in axis:
        if v[0][1] <= brear + 0.05 and v[1][1] > brear: brear = v[1][1]
    gun = set()
    for i, (mn, mx, n, m) in st.items():
        c = (mn + mx) / 2
        if mx[1] <= brear + MANTLET and abs(c[0] - bx) < 0.7 and abs(c[2] - bz) < 0.5: gun.add(i)
    fg = np.array([1 if i in gun else 0 for i in fi], dtype=np.int64)
    tobjs = split(tur, fg, ["turret", "cannon"])
    bpy.data.objects.remove(tur)
    objs = {**hobjs, **tobjs}
    tmn, tmx = bbox(objs["turret"]); cmn, cmx = bbox(objs["cannon"])
    print(f"SV {t} Rohr x {bx:.3f} z {bz:.3f} Ende y {brear:.2f} Muendung y {barrel[0][1]:.2f}; Kanone y {cmn.y:.2f}..{cmx.y:.2f}; Turm y {tmn.y:.2f}..{tmx.y:.2f} z {tmn.z:.2f}..{tmx.z:.2f}")

    if preview:
        for nm, o in objs.items():
            c = (0.9, 0.2, 0.1, 1) if nm == "turret" else (1, 0.9, 0.1, 1) if nm == "cannon" else (0.4, 0.4, 0.4, 1) if nm == "Wanne" else (0.9, 0.3, 0.9, 1) if nm.startswith("roll") else ((0.1, 0.5, 1, 1) if int(nm[4:]) % 2 == 0 else (0.1, 0.9, 0.4, 1))
            mat = bpy.data.materials.new("v" + nm); mat.diffuse_color = c; o.data.materials.clear(); o.data.materials.append(mat)
        sc = bpy.context.scene; sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
        sc.render.resolution_x = 1400; sc.render.resolution_y = 800
        cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
        for o in bpy.data.objects:
            if o.type == "MESH": o.hide_render = o.name not in [x.name for x in objs.values()]
        for name, pos in (("seite", Vector((-14, -0.6, 1.3))), ("schraeg", Vector((-8, -8, 5))), ("oben", Vector((0, -0.6, 15)))):
            cam.location = pos; d = Vector((0, -0.6, 1.3)) - pos
            cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler() if name != "oben" else (0, 0, 0)
            cd.type = "PERSP" if name == "schraeg" else "ORTHO"; cd.ortho_scale = 10; cd.lens = 45
            sc.render.filepath = os.path.join(preview, f"{game}_{name}.png"); bpy.ops.render.render(write_still=True)
        for o in objs.values(): bpy.data.objects.remove(o)
        continue

    # ---- Drehpunkte, Hierarchie, Export ----
    TPIV = Vector((ring[t].x, ring[t].y, tmn.z))
    GPIV = Vector((bx, cmx.y - 0.05, bz))
    set_origin(objs["turret"], TPIV); set_origin(objs["cannon"], GPIV)
    for sn in "LR":
        for k, s in enumerate(W[sn]): set_origin(objs[f"rad{sn}{k}"], Vector(((s[4] + s[5]) / 2, s[1], s[2])))
        for k, s in enumerate(R[sn]): set_origin(objs[f"roll{sn}{k + 1}"], Vector(((s[4] + s[5]) / 2, s[1], s[2])))
    bpy.context.view_layer.update()
    objs["cannon"].parent = objs["turret"]; objs["cannon"].matrix_parent_inverse = objs["turret"].matrix_world.inverted()
    root = bpy.data.objects.new(game, None); bpy.context.scene.collection.objects.link(root)
    for o in objs.values():
        if o.parent is None: mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
    bpy.context.view_layer.update()
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs.values())
    wmn, wmx = bbox(objs["Wanne"])
    info = (f"SV {t}: Wanne x {wmn.x:.2f}..{wmx.x:.2f} y {wmn.y:.2f}..{wmx.y:.2f} z {wmn.z:.2f}..{wmx.z:.2f}; Turm {tuple(round(v, 3) for v in TPIV)} "
            f"Rohr {tuple(round(v, 3) for v in GPIV)} Muendung y {cmn.y:.2f}; Raeder {len(W['L'])} r {[round(s[3], 3) for s in W['L']]} x {[round((s[4] + s[5]) / 2, 2) for s in W['L']][:2]}; "
            f"Stuetzrollen {len(R['L'])} r {[round(s[3], 3) for s in R['L']]}; Dreiecke {tris}")
    print(info); summary.append(info)
    tc.select_only([root] + list(objs.values()))
    out = os.path.join(outdir, game + ".glb")
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, use_selection=True, export_image_format="JPEG", export_jpeg_quality=88)
    print("SV exportiert", out, os.path.getsize(out) // 1024, "KB")
    for o in list(objs.values()) + [root]: bpy.data.objects.remove(o)
for s in summary: print(s)

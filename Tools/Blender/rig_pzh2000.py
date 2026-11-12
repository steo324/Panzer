"""
Riggt das Sketchfab-Modell "Panzerhaubitze 2000" (gogiart, CC BY 4.0) fuer PanzerSim.
Das Original ist nach Materialien zusammengefasst (ein Teil enthaelt Wanne, Turm und Rohr zugleich) -
darum wird alles in lose Teile zerlegt und nach Lage/Groesse sortiert:
  hull, turret (Drehpunkt Turmmitte) > cannon (Drehpunkt Schildzapfen),
  radL0..radL8 / radR0..radR8 (0 = Antriebsrad hinten, 1-7 Laufrollen, 8 = Leitrad vorne - vorne/hinten wie im Spiel:
  Index 0 = kleinstes z = hinten), Ketten des Modells bleiben (Material kette_links/kette_rechts, Textur wird verschoben).
Original: vorne = -X, oben = Z. Danach gedreht: vorne = Blender -Y (in Unity +Z), links = Blender -X.
Aufruf: blender -b --python rig_pzh2000.py -- <panzerhaubitze_2000.glb> <out.glb> [vorschau.png]
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
preview = args[2] if len(args) > 2 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)

def meshes(): return [o for o in bpy.data.objects if o.type == "MESH"]
def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[0]
def bbox(o):
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    return (Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))),
            Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs))))
def join(objs, name):
    objs = [o for o in objs if o is not None]
    if not objs: return None
    select_only(objs); bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    return o
def set_origin(o, p):
    d = o.matrix_world.inverted() @ p
    o.data.transform(Matrix.Translation(-d))
    o.matrix_world = o.matrix_world @ Matrix.Translation(d)

# ---- Hierarchie aufloesen, Transformationen anwenden ----
for o in meshes():
    if o.parent is not None:
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
select_only(meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.data.objects):
    if o.type != "MESH": bpy.data.objects.remove(o)

# Kettenmaterialien eindeutig machen (Textur wird im Spiel verschoben): Object_44 = +Y-Seite, Object_5 = -Y-Seite
def own_material(o, name):
    m = o.material_slots[0].material.copy(); m.name = name
    for s in o.material_slots: s.material = m
own_material(bpy.data.objects["Object_44"], "kette_links")    # +Y wird nach dem Drehen -X = links
own_material(bpy.data.objects["Object_5"], "kette_rechts")
tracks = [bpy.data.objects["Object_44"], bpy.data.objects["Object_5"]]

# Einzelne Ebenen des Originals (je 5-14 Dreiecke, halbtransparente Aufkleber/Schatten) stehen im Spiel als Platten ab -> weg
for nm in ("Object_16", "Object_52", "Object_53", "Object_58"):
    if nm in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[nm])

# ---- alles andere in lose Teile zerlegen ----
rest = [o for o in meshes() if o not in tracks]
big = join(rest, "alles")
select_only([big])
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
parts = [o for o in meshes() if o not in tracks]
print("PZH lose Teile:", len(parts))

TURRET_X0, DECK_Z = -0.35, 1.70   # Turmboden ~1,72 m; Gitterroste auf dem Deck liegen tiefer
groups = {"turret": [], "gun": [], "wheel": [], "hull": []}
info = {}
removed = 0
for p in list(parts):
    mn, mx = bbox(p); c = (mn + mx) / 2; s = mx - mn
    # vereinfachtes Schraegblech (12 Flaechen, 3,9 m lang) an der Turmseite: steht im Spiel als Platte ab
    if len(p.data.polygons) <= 16 and s.x > 3.0 and s.y < 0.6 and s.z > 0.5:
        bpy.data.objects.remove(p); parts.remove(p); removed += 1; continue
    info[p.name] = (mn, mx, c, s)
    if c.z > 1.95 and c.x < 0.55 and abs(c.y) < 0.75 and mn.z > 1.7 and (c.x < -0.6 or s.x > 0.25):
        groups["gun"].append(p)                                   # Rohr, Rohrvorholer, Blende
    elif c.x > TURRET_X0 and abs(c.y) < 1.75 and ((mn.z >= DECK_Z and c.z > 1.80) or (c.z > 2.05 and c.x > 0.3)):
        # Turm; auch Seitenwaende, die etwas unter den Turmboden reichen (Mitte klar in Turmhoehe)
        groups["turret"].append(p)
    elif c.z < 0.95 and abs(c.y) > 0.75 and 0.25 < s.z < 0.95 and abs(s.x - s.z) < 0.25 * max(s.x, s.z) + 0.05:
        groups["wheel"].append(p)                                 # runde Teile im Laufwerk
    else:
        groups["hull"].append(p)
print("PZH Gruppen:", {k: len(v) for k, v in groups.items()}, "entfernte Flaechen:", removed)

# ---- Laufrollen nach Stationen ordnen (Seite, x) ----
stations = []   # [seite, x, z, teile]
for p in groups["wheel"]:
    mn, mx, c, s = info[p.name]
    side = 1 if c.y > 0 else -1
    for st in stations:
        if st[0] == side and abs(st[1] - c.x) < 0.25 and abs(st[2] - c.z) < 0.25:
            st[3].append(p); break
    else:
        stations.append([side, c.x, c.z, [p]])
stations.sort(key=lambda s: (s[0], s[1]))
print("PZH Stationen:", [(s[0], round(s[1], 2), round(s[2], 2), len(s[3])) for s in stations])

if preview:
    cols = {"turret": (0.9, 0.2, 0.1, 1), "gun": (1.0, 0.9, 0.1, 1), "wheel": (0.1, 0.5, 1.0, 1), "hull": (0.35, 0.35, 0.35, 1)}
    for g, ps in groups.items():
        m = bpy.data.materials.new("vorschau_" + g); m.diffuse_color = cols[g]
        for p in ps:
            p.data.materials.clear(); p.data.materials.append(m)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.color_type = "MATERIAL"
    sc.render.resolution_x = 1400; sc.render.resolution_y = 800
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    for name, pos in (("seite", Vector((-0.9, -15, 1.6))), ("schraeg", Vector((-9, -9, 6))), ("oben", Vector((-0.9, 0.01, 20)))):
        cam.location = pos; d = Vector((-0.9, 0, 1.6)) - pos; cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        cd.type = "PERSP" if name == "schraeg" else "ORTHO"; cd.ortho_scale = 11.5; cd.lens = 45
        sc.render.filepath = preview.replace(".png", f"_{name}.png"); bpy.ops.render.render(write_still=True)
    print("PZH Vorschau fertig"); sys.exit(0)

# ---- Stationen mit zu wenig Teilen (Kleinkram) zurueck in die Wanne ----
good = [s for s in stations if len(s[3]) >= 4]
for s in stations:
    if s not in good: groups["hull"] += s[3]
stations = good

# ---- Drehpunkte ----
tmin = Vector((1e9, 1e9, 1e9)); tmax = -tmin
for p in groups["turret"]:
    mn, mx, c, s = info[p.name]
    tmin = Vector(map(min, tmin, mn)); tmax = Vector(map(max, tmax, mx))
gun_far = [info[p.name][2] for p in groups["gun"] if info[p.name][2].x < -3.0]
gun_z = sum(c.z for c in gun_far) / max(len(gun_far), 1)
gun_rear = max(info[p.name][1].x for p in groups["gun"])
tpiv = Vector(((tmin.x + tmax.x) / 2, 0.0, tmin.z))
gpiv = Vector((min(gun_rear, tmin.x + 0.55), 0.0, gun_z))   # Schildzapfen kurz hinter der Turmfront, auf der Rohrachse
print(f"PZH Turm x {tmin.x:.2f}..{tmax.x:.2f} z ab {tmin.z:.2f}; Drehpunkt ({tpiv.x:.2f},{tpiv.z:.2f}); Rohrachse z {gun_z:.2f}, Rohr hinten x {gun_rear:.2f}")

hull = join(groups["hull"], "Wanne")          # nicht "hull": keine automatische Zerlegung in Anbauteile (Modell zu gross)
# Manche Turmbleche haengen im Original an Wannenteilen (ein zusammenhaengendes Stueck) -> flaechenweise trennen:
# alles, was in Turmhoehe ueber dem Turmbereich liegt, gehoert zum Turm (sonst bleibt es beim Drehen stehen)
import bmesh
bm = bmesh.new(); bm.from_mesh(hull.data)
mw = hull.matrix_world
for f in bm.faces:
    c = mw @ f.calc_center_median()
    # ab knapp unter der Turmunterkante (1,75 m: Unterseite des Turmbodens gehoert dazu), Wannendeck (1,68 m) bleibt
    f.select_set(c.z > tmin.z - 0.03 and c.x > tmin.x + 0.02 and abs(c.y) < 1.9)
bm.to_mesh(hull.data); bm.free()
select_only([hull]); bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.separate(type="SELECTED"); bpy.ops.object.mode_set(mode="OBJECT")
# Schraege Flaeche der Wanne unter dem Turm, die seitlich ueber die Wanne hinausragt (vom Turm verdeckt, beim Drehen sichtbar) -> weg
bm = bmesh.new(); bm.from_mesh(hull.data); mw = hull.matrix_world; nmat = mw.to_3x3()
flap = []
for f in bm.faces:
    c = mw @ f.calc_center_median(); n = (nmat @ f.normal).normalized()
    if c.x > tmin.x and abs(c.y) > 1.62 and 1.4 < c.z < 1.9 and abs(n.z) > 0.3 and f.calc_area() > 0.15:
        flap.append(f)
bmesh.ops.delete(bm, geom=flap, context="FACES"); bm.to_mesh(hull.data); bm.free()
print("PZH abstehende Wannenflaechen entfernt:", len(flap))
extra = [o for o in meshes() if o.name.startswith("Wanne") and o is not hull]
if extra:
    select_only(extra); bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE"); bpy.ops.object.mode_set(mode="OBJECT")
    extra = [o for o in meshes() if o.name.startswith("Wanne") and o is not hull]
    for o in list(extra):
        mn, mx = bbox(o); s = mx - mn
        if len(o.data.polygons) <= 16 and s.x > 3.0 and s.y < 0.6 and s.z > 0.5:   # das abstehende Schraegblech
            extra.remove(o); bpy.data.objects.remove(o); removed += 1
print("PZH Turmflaechen aus der Wanne:", sum(len(o.data.polygons) for o in extra), "entfernt gesamt:", removed)
turret = join(groups["turret"] + extra, "turret"); set_origin(turret, tpiv)
cannon = join(groups["gun"], "cannon"); set_origin(cannon, gpiv)
for i, (o, nm) in enumerate(((tracks[0], "ketteL"), (tracks[1], "ketteR"))): o.name = nm; o.data.name = nm

# Raeder: Index 0 = hinten (groesstes x), letzter = vorne (kleinstes x); Seite +Y = links (nach dem Drehen -X)
for side, letter in ((1, "L"), (-1, "R")):
    sts = sorted([s for s in stations if s[0] == side], key=lambda s: -s[1])
    for i, st in enumerate(sts):
        w = join(st[3], f"rad{letter}{i}")
        mn, mx = bbox(w)
        set_origin(w, (mn + mx) / 2)
    print(f"PZH Seite {letter}: {len(sts)} Raeder, Radien", [round((bbox(bpy.data.objects[f'rad{letter}{i}'])[1].z - bbox(bpy.data.objects[f'rad{letter}{i}'])[0].z) / 2, 3) for i in range(len(sts))])

# ---- drehen: vorne -X -> -Y, Wurzel, Export ----
root = bpy.data.objects.new("PzH2000", None); bpy.context.collection.objects.link(root)
cannon.parent = turret; cannon.matrix_parent_inverse = turret.matrix_world.inverted()
for o in meshes():
    if o.parent is None:
        mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
root.rotation_euler = (0, 0, math.radians(90))
bpy.context.view_layer.update()
select_only([root]); bpy.ops.object.transform_apply(location=False, rotation=True, scale=False, properties=False, isolate_users=False)
# Abstehendes Schraegblech am Turm (3,9 m lang, aussen an der linken Turmseite, x < -1,08 m nach dem Drehen):
# es haengt ueber gemeinsame Eckpunkte an anderen Flaechen -> flaechenweise loeschen (nur grosse Flaechen in diesem Bereich)
tur = bpy.data.objects["turret"]
bm = bmesh.new(); bm.from_mesh(tur.data); mw = tur.matrix_world
kill = []   # (deaktiviert: traf auch die Turmpanzerung)
bmesh.ops.delete(bm, geom=kill, context="FACES")
bm.to_mesh(tur.data); bm.free()
print("PZH Schraegblech-Flaechen entfernt:", len(kill))
tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes())
print("PZH Teile:", sorted(o.name for o in meshes()), "Dreiecke:", tris)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True)
print("PZH exportiert", out, os.path.getsize(out) // 1024, "KB")

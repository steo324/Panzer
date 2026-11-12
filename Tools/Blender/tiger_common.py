"""
Gemeinsame Hilfen fuer rig_tiger.py: Modell laden, in Meter umrechnen, Einzelteile (zusammenhaengende Inseln) schnell finden.
"Panzer Vi Tiger I Heavy Tank - Toshueyi" von Joanthan To (sketchfab.com/jonathanto99), CC BY 4.0.
Original: Millimeter, ca. 1,2-fach zu gross, vorne = -Y, oben = Z.
"""
import bpy
import numpy as np
from mathutils import Vector, Matrix

S = 0.00084   # mm -> m und auf echte Groesse (Hoehe 2,94 m, Wannenlaenge 6,35 m)

def meshes(): return [o for o in bpy.data.objects if o.type == "MESH"]

def select_only(objs, active=None):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = active or (objs[0] if objs else None)

def load(src):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    for o in meshes():
        if o.parent is not None:
            mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in list(bpy.data.objects):
        if o.type != "MESH": bpy.data.objects.remove(o)
    select_only(meshes()); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # Kette (TIGcad) und Hilfsdraht: Boden und Mitte merken, dann weg (das Spiel baut eine eigene Gliederkette)
    track = [o for o in meshes() if o.material_slots and o.material_slots[0].material and o.material_slots[0].material.name.startswith("TIGcad")]
    vs = np.concatenate([np.array([v.co[:] for v in o.data.vertices]) for o in track])
    ground, cx = vs[:, 2].min(), (vs[:, 0].min() + vs[:, 0].max()) / 2
    for o in list(meshes()):
        m = o.material_slots[0].material.name if o.material_slots and o.material_slots[0].material else ""
        if m.startswith("TIGcad") or m.startswith("wire_"): bpy.data.objects.remove(o)
    M = Matrix.Scale(S, 4) @ Matrix.Translation(Vector((-cx, 0, -ground)))
    for o in meshes(): o.data.transform(M)
    select_only(meshes()); bpy.ops.object.join()
    big = bpy.context.view_layer.objects.active; big.name = "alles"
    return big

def islands(me):
    """Insel-Nummer je Flaeche (Flaechen, die ueber Eckpunkte zusammenhaengen)."""
    nv = len(me.vertices)
    parent = np.arange(nv)
    def find(a):
        r = a
        while parent[r] != r: r = parent[r]
        while parent[a] != r: parent[a], a = r, parent[a]
        return r
    lt = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("loop_total", lt)
    ls = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("loop_start", ls)
    lv = np.zeros(len(me.loops), dtype=np.int64); me.loops.foreach_get("vertex_index", lv)
    for f in range(len(lt)):
        a = find(lv[ls[f]])
        for k in range(1, lt[f]):
            b = find(lv[ls[f] + k])
            if a != b: parent[b] = a
    root = np.array([find(i) for i in range(nv)])
    face_island = root[lv[ls]]
    return face_island, lv, ls, lt

def island_stats(me, face_island, lv, ls, lt):
    co = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    mi = np.zeros(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("material_index", mi)
    stats = {}
    for f in range(len(face_island)):
        i = face_island[f]
        p = co[lv[ls[f]:ls[f] + lt[f]]]
        s = stats.get(i)
        if s is None: stats[i] = [p.min(0), p.max(0), 1, mi[f]]
        else:
            s[0] = np.minimum(s[0], p.min(0)); s[1] = np.maximum(s[1], p.max(0)); s[2] += 1
    return stats

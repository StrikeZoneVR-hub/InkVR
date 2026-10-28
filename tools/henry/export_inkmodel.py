"""
Export a Henry .blend (from build_henry.py) as an .inkmodel, the simple file InkVR loads at runtime:

  blender --background <henry.blend> --python tools/henry/export_inkmodel.py -- <out.inkmodel> [texture size]

Layout (little-endian): "INKM", u32 version 1, then
  bones:    u32 n; per bone: string name, i32 parent, f32[3] local position, f32[4] local rotation (x y z w)
  textures: u32 n; per texture: string name, u16 width, u16 height, u32 bytes, zlib(RGBA32 rows bottom-up)
  parts:    u32 n; per part (one skinned mesh): string name, string material, i32 texture, u8 flags (1 alpha cut-out),
            u32 vertices; f32[3] position, f32[3] normal, f32[2] uv, u8[4] bones, f32[4] weights (each per vertex)
            u32 indices; u32 index...
Strings are u16 length + UTF-8. Coordinates are Unity's (metres, Y up, the figure facing +Z, left-handed):
Blender's (x, y, z) becomes (-x, z, -y), and triangles are wound the other way round to match.
"""
import os
import struct
import sys
import zlib

import bpy
import numpy as np
from mathutils import Matrix

args = sys.argv[sys.argv.index("--") + 1:]
OUT = args[0]
TEX_SIZE = int(args[1]) if len(args) > 1 else 1024

# Blender -> Unity: a reflection (x flipped) composed with Z-up to Y-up
M = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
MI = M.inverted()


def conv(v):
    return (-v[0], v[2], -v[1])


def s(b, text):
    data = text.encode("utf-8")
    b += struct.pack("<H", len(data)) + data


rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
# (the eyelashes are cards that need see-through edges, which the game's opaque shaders don't give: left out)
meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.parent == rig and "Lashes" not in o.name]


def game_name(n):
    """MakeHuman's game-engine bone names in the game's own scheme ("bn_L_ForeArm001"), which InkVR's
    CharacterSkin reads roles from."""
    side = ""
    if n.endswith("_l") or n.endswith("_r"):
        side = "L_" if n.endswith("_l") else "R_"
        n = n[:-2]
    fixed = {"pelvis": "Hips", "spine_01": "Spine_A", "spine_02": "Spine_B", "spine_03": "Chest", "neck_01": "Neck",
             "head": "Head", "clavicle": "Clavicle", "upperarm": "UpperArm", "lowerarm": "ForeArm", "hand": "Hand",
             "thigh": "Thigh", "calf": "Calve", "foot": "Foot", "ball": "Toe"}
    if n in fixed:
        return f"bn_{side}{fixed[n]}001"
    for finger in ("thumb", "index", "middle", "ring", "pinky"):
        if n.startswith(finger + "_"):
            return f"bn_{side}{finger.capitalize()}{int(n.split('_')[1])}_001"
    return n  # Root stays a plain transform
out = bytearray(b"INKM") + struct.pack("<I", 1)

# ---- bones: their rest frames in Unity's space, each relative to its parent
bones = list(rig.data.bones)
index = {b.name: i for i, b in enumerate(bones)}
world = {}
for b in bones:
    w = rig.matrix_world @ b.matrix_local
    world[b.name] = M @ w @ MI
out += struct.pack("<I", len(bones))
for b in bones:
    w = world[b.name]
    local = (world[b.parent.name].inverted() @ w) if b.parent else w
    t, q, _ = local.decompose()
    s(out, game_name(b.name))
    out += struct.pack("<i", index[b.parent.name] if b.parent else -1)
    out += struct.pack("<3f", t.x, t.y, t.z)
    out += struct.pack("<4f", q.x, q.y, q.z, q.w)

# ---- textures: the image each material shows, scaled down to TEX_SIZE at most
images = []
mat_tex = {}
for ob in meshes:
    for slot in ob.material_slots:
        mat = slot.material
        if mat is None or mat.name in mat_tex:
            continue
        node = next((n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image), None)
        if node is None:
            mat_tex[mat.name] = -1
            continue
        img = node.image
        if img.name not in [i.name for i in images]:
            images.append(img)
        mat_tex[mat.name] = [i.name for i in images].index(img.name)
out += struct.pack("<I", len(images))
for img in images:
    w, h = img.size
    if max(w, h) > TEX_SIZE:
        img = img.copy()
        img.scale(min(w, TEX_SIZE), min(h, TEX_SIZE))
        w, h = img.size
    px = np.clip(np.array(img.pixels[:], dtype=np.float32), 0, 1).reshape(-1, 4)
    if (px[:, 3] < 0.99).any():
        # see-through edges baked solid (the game's character shaders are opaque): the colour of what's there
        # behind it, hair for hair and skin for eyebrows, so a card reads as hair, not as a dark rectangle
        solid = px[px[:, 3] > 0.5, :3].mean(axis=0) if (px[:, 3] > 0.5).any() else np.array([0.5, 0.5, 0.5])
        under = solid if "hair" in img.name.lower() else np.array([0.80, 0.64, 0.50])
        a = px[:, 3:4]
        px[:, :3] = px[:, :3] * a + under * (1 - a)
        px[:, 3] = 1
    px = (px.ravel() * 255 + 0.5).astype(np.uint8)
    data = zlib.compress(px.tobytes(), 6)
    s(out, os.path.splitext(img.name)[0])
    out += struct.pack("<HHI", w, h, len(data)) + data

# ---- parts: one per mesh, its corners split where UVs or normals differ
out += struct.pack("<I", len(meshes))
for ob in meshes:
    me = ob.data
    me.calc_loop_triangles()
    uv = me.uv_layers.active.data if me.uv_layers.active else None
    try:
        normals = [n.vector for n in me.corner_normals]
    except AttributeError:
        me.calc_normals_split()
        normals = [l.normal for l in me.loops]
    groups = {g.index: index.get(g.name, -1) for g in ob.vertex_groups}
    vweights = []
    for v in me.vertices:
        ws = sorted(((g.weight, groups.get(g.group, -1)) for g in v.groups if groups.get(g.group, -1) >= 0), reverse=True)[:4]
        tot = sum(w for w, _ in ws) or 1.0
        ws = [(w / tot, b) for w, b in ws] + [(0.0, 0)] * (4 - len(ws))
        vweights.append(ws)
    key_to_index = {}
    verts = bytearray()
    indices = []
    count = 0
    for tri in me.loop_triangles:
        corner = []
        for li in tri.loops:
            vi = me.loops[li].vertex_index
            u = tuple(round(c, 5) for c in uv[li].uv) if uv else (0.0, 0.0)
            n = normals[li]
            key = (vi, u, round(n[0], 3), round(n[1], 3), round(n[2], 3))
            k = key_to_index.get(key)
            if k is None:
                k = count
                key_to_index[key] = k
                count += 1
                p = ob.matrix_world @ me.vertices[vi].co
                verts += struct.pack("<3f", *conv(p))
                verts += struct.pack("<3f", *conv(n))
                verts += struct.pack("<2f", u[0], u[1])
                ws = vweights[vi]
                verts += struct.pack("<4B", *(b for _, b in ws))
                verts += struct.pack("<4f", *(w for w, _ in ws))
            corner.append(k)
        indices += [corner[0], corner[2], corner[1]]  # wound the other way round (the reflection)
    mat = ob.material_slots[0].material if ob.material_slots else None
    s(out, ob.name)
    s(out, mat.name if mat else "")
    out += struct.pack("<i", mat_tex.get(mat.name, -1) if mat else -1)
    alpha = 1 if mat and any(l.to_socket.name == "Alpha" for l in mat.node_tree.links) else 0
    out += struct.pack("<B", alpha)
    out += struct.pack("<I", count) + verts
    out += struct.pack("<I", len(indices)) + struct.pack(f"<{len(indices)}I", *indices)
    print(f"INKMODEL part {ob.name}: {count} vertices, {len(indices) // 3} triangles, material {mat.name if mat else '-'}")

with open(OUT, "wb") as f:
    f.write(out)
print(f"INKMODEL wrote {OUT}: {len(bones)} bones, {len(images)} textures, {len(meshes)} parts, {len(out) / 1048576:.1f} MB")

"""
Henry Stein for InkVR, built in Blender from MakeHuman's CC0 base (run with Blender in the background):

  blender --background --factory-startup --python tools/henry/build_henry.py -- <out dir> [stage]

Stages build up: body (shaped, skinned, on a game-engine skeleton), then his clothes, then the textures in the
game's sepia, then the export InkVR reads. Each stage saves henry.blend and preview renders into <out dir>.
"""
import math
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mh  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(args[0] if args else "henry_out")
STAGE = args[1] if len(args) > 1 else "body"
os.makedirs(OUT, exist_ok=True)

AGE = 62  # Henry comes back to the studio thirty years on


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_object(name, verts, faces, uvs=None, face_uvs=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    if uvs is not None:
        layer = me.uv_layers.new(name="UVMap")
        loop_uv = []
        for fu in face_uvs:
            loop_uv.extend(fu)
        coords = np.asarray(uvs)[np.asarray(loop_uv, dtype=np.int64)]
        layer.data.foreach_set("uv", coords.astype(np.float32).ravel())
    me.validate()
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def image_material(name, path, alpha=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(path, check_existing=True)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    if alpha:
        nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        mat.blend_method = "HASHED" if hasattr(mat, "blend_method") else None
    nt.nodes.active = tex
    return mat


# ---------------- textures: MakeHuman's photo textures, regraded into the studio's sepia ----------------

SEPIA = {
    "cream": (0.86, 0.80, 0.66),     # his shirt
    "vest": (0.40, 0.27, 0.16),      # the waistcoat
    "tie": (0.20, 0.14, 0.11),       # the tie's dark stripes
    "trousers": (0.30, 0.26, 0.22),  # wool trousers
    "hair": (0.55, 0.52, 0.48),      # greying
}


def tex(kind, src, name=None):
    """A regraded copy of a texture (saved beside the .blend, so the export can carry it)."""
    if not STAGE in ("textures", "export", "teaser"):
        return src
    out_dir = os.path.join(OUT, "tex")
    os.makedirs(out_dir, exist_ok=True)
    dst = os.path.join(out_dir, (name or kind) + ".png")
    img = bpy.data.images.load(src)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    rgb, a = px[..., :3], px[..., 3:]
    lum = (rgb * np.array([0.299, 0.587, 0.114])).sum(axis=2, keepdims=True)
    v = np.linspace(0, 1, h)[:, None, None]  # (pixels run bottom-up in Blender: row 0 is v=0)
    u = np.linspace(0, 1, w)[None, :, None]

    def tint(col, base, gain):
        return np.clip(np.array(col) * (base + gain * lum), 0, 1)

    if kind == "skin":
        warm = lum * np.array([1.10, 0.90, 0.72])
        out = rgb * 0.55 + warm * 0.45
    elif kind == "shirt":
        out = tint(SEPIA["cream"], 0.55, 0.9)
    elif kind == "suit":
        sat = rgb.max(axis=2, keepdims=True) - rgb.min(axis=2, keepdims=True)
        bright = lum > 0.5
        tie = (sat > 0.06) & (lum > 0.12) & (lum < 0.62) & (v > 0.7) & (u < 0.5)
        jacket = v > 0.5
        out = np.where(jacket, tint(SEPIA["vest"], 0.55, 1.8), tint(SEPIA["trousers"], 0.55, 1.8))
        out = np.where(bright, tint(SEPIA["cream"], 0.3, 0.75), out)
        out = np.where(tie, tint(SEPIA["tie"], 0.6, 1.4), out)
    elif kind == "hair":
        out = tint(SEPIA["hair"], 0.35, 1.3)
    elif kind == "shoes":
        out = rgb * 0.6 + lum * np.array([0.95, 0.75, 0.55]) * 0.4
    else:
        out = rgb
    px = np.concatenate([np.clip(out, 0, 1), a], axis=2)
    new = bpy.data.images.new(os.path.basename(dst), w, h, alpha=True)
    new.pixels[:] = px.ravel()
    new.filepath_raw = dst
    new.file_format = "PNG"
    new.save()
    bpy.data.images.remove(img)
    return dst


def armature(bones):
    arm = bpy.data.armatures.new("HenryRig")
    ob = bpy.data.objects.new("HenryRig", arm)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for name, (head, tail, parent, roll, _) in bones.items():
        b = arm.edit_bones.new(name)
        b.head = mh.to_blender(head)
        b.tail = mh.to_blender(tail)
        b.roll = roll
        eb[name] = b
    for name, (_, _, parent, _, _) in bones.items():
        if parent:
            eb[name].parent = eb[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def skin(ob, rig, names, w):
    groups = [ob.vertex_groups.new(name=n) for n in names]
    rows, cols = np.nonzero(w > 1e-4)
    for r, c in zip(rows.tolist(), cols.tolist()):
        groups[c].add([r], float(w[r, c]), "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    ob.parent = rig


def bounds():
    lo = np.array([1e9, 1e9, 1e9])
    hi = -lo
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH":
            continue
        for c in ob.bound_box:
            p = ob.matrix_world @ __import__("mathutils").Vector(c)
            lo = np.minimum(lo, p)
            hi = np.maximum(hi, p)
    return lo, hi


def render(name, views=(("front", 0), ("side", 90), ("three", 35))):
    scene = bpy.context.scene
    lo, hi = bounds()
    mid_z = (lo[2] + hi[2]) / 2
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_specular_highlight = False
    scene.render.resolution_x = 700
    scene.render.resolution_y = 1100
    scene.render.film_transparent = False
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = (hi[2] - lo[2]) * 1.08 * 1100 / 700 if False else (hi[2] - lo[2]) * 1.08
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    for view, yaw in views:
        a = math.radians(yaw)
        # orbit round the figure at chest height, looking at it (front = from -Y)
        cam.location = (5 * math.sin(a), -5 * math.cos(a), mid_z)
        cam.rotation_euler = (math.radians(90), 0, a)
        scene.render.filepath = os.path.join(OUT, f"{name}_{view}.png")
        bpy.ops.render.render(write_still=True)


ARMS_LOWER = ("lowerarm", "hand", "thumb", "index", "middle", "ring", "pinky")
ARMS_UPPER = ("upperarm",)
LEGS = ("thigh", "calf", "foot", "ball", "pelvis")


def kind(bone):
    for k, prefixes in (("lower", ARMS_LOWER), ("upper", ARMS_UPPER), ("legs", LEGS)):
        if bone.startswith(prefixes):
            return k
    return "torso"


def fit_weights(proxy, W):
    """A proxy vertex's bone weights: its three body vertices' weights, mixed as it sits on them."""
    w = (W[proxy.ref] * np.clip(proxy.w, 0, None)[:, :, None]).sum(axis=1)
    s = w.sum(axis=1, keepdims=True)
    s[s == 0] = 1
    return w / s


def face_kinds(faces, w, names):
    """Each face's region by its vertices' main bones (a majority vote)."""
    main = [kind(names[i]) for i in np.argmax(w, axis=1)]
    out = []
    for f, _ in faces:
        votes = {}
        for v in f:
            votes[main[v]] = votes.get(main[v], 0) + 1
        out.append(max(votes, key=votes.get))
    return out, main


_BODY = None


def covered_by(proxy, coord, rings=2):
    """The body vertices under a garment's remaining faces. MakeHuman fits clothes to its helper layers (tights
    and skirt over the body), so each point it sits on is taken to the nearest real body vertex, then spread a
    couple of rings over the body mesh (a garment's vertices are sparser than the body's)."""
    global _BODY
    from mathutils import kdtree
    if _BODY is None:
        base = mh.ObjMesh(os.path.join(mh.MH, "3dobjs", "base.npz"))
        body_faces = base.fvert[base.group == base.groups.index("body")]
        ids = np.unique(body_faces)
        e = np.concatenate([body_faces[:, [0, 1]], body_faces[:, [1, 2]], body_faces[:, [2, 3]], body_faces[:, [3, 0]]])
        _BODY = (ids, np.concatenate([e, e[:, ::-1]]))
    ids, edges = _BODY
    tree = kdtree.KDTree(len(ids))
    for k, v in enumerate(ids):
        tree.insert(coord[v], k)
    tree.balance()
    refs = np.unique(np.concatenate([proxy.ref[f].ravel() for f, _ in proxy.kept]))
    mask = np.zeros(len(coord), dtype=bool)
    for r in refs:
        _, k, d = tree.find(coord[r])
        if d < 0.6:  # dm: within 6 cm
            mask[ids[k]] = True
    for _ in range(rings):
        grow = mask.copy()
        grow[edges[mask[edges[:, 0]], 1]] = True
        mask = grow
    return mask


_ALL_EDGES = None


def ref_covered(proxy, rings=2):
    """The helper-layer vertices a garment's remaining faces sit on, spread a couple of rings (to compare garments
    fitted to the same layer: a shirt under a waistcoat)."""
    global _ALL_EDGES
    if _ALL_EDGES is None:
        fv = mh.ObjMesh(os.path.join(mh.MH, "3dobjs", "base.npz")).fvert
        e = np.concatenate([fv[:, [0, 1]], fv[:, [1, 2]], fv[:, [2, 3]], fv[:, [3, 0]]])
        _ALL_EDGES = np.concatenate([e, e[:, ::-1]])
    mask = np.zeros(19158, dtype=bool)
    for f, _ in proxy.kept:
        mask[proxy.ref[f].ravel()] = True
    for _ in range(rings):
        grow = mask.copy()
        grow[_ALL_EDGES[mask[_ALL_EDGES[:, 0]], 1]] = True
        mask = grow
    return mask


def proxy_object(name, proxy, coord, W, names, rig, keep=None, material=None, tuck=None, adjust=None):
    pos = proxy.fit(coord)
    w = fit_weights(proxy, W)
    if tuck is not None:
        # pulled in towards the body (a shirt under a waistcoat): its distance from the skin times this, by region
        on_body = (coord[proxy.ref] * proxy.w[:, :, None]).sum(axis=1)
        main = [kind(names[i]) for i in np.argmax(w, axis=1)]
        f = np.array([tuck.get(m, 1.0) for m in main])[:, None]
        pos = on_body + (pos - on_body) * f
    faces = proxy.obj.faces()
    if keep is not None:
        faces = [f for f, k in zip(faces, keep(faces, w, pos)) if k]
    proxy.kept = faces
    if adjust is not None:
        pos = adjust(pos, faces)
    used = sorted({v for f, _ in faces for v in f})
    remap = {v: i for i, v in enumerate(used)}
    ob = mesh_object(name, mh.to_blender(pos[used]), [[remap[v] for v in f] for f, _ in faces], proxy.obj.texco, [t for _, t in faces])
    if material is not None:
        ob.data.materials.append(material)
    skin(ob, rig, names, w[used])
    return ob, pos, w


def skinned_meshes(rig):
    return [ob for ob in bpy.data.objects if ob.type == "MESH" and ob.parent == rig]


def tpose(rig):
    """Arms straight out to the sides (the teaser's T-pose; the model itself stays in MakeHuman's A-pose)."""
    from mathutils import Matrix, Vector
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for side, sign in (("l", 1), ("r", -1)):
        for name in (f"upperarm_{side}", f"lowerarm_{side}", f"hand_{side}"):
            pb = rig.pose.bones[name]
            bpy.context.view_layer.update()
            cur = (pb.tail - pb.head).normalized()
            q = cur.rotation_difference(Vector((sign, 0, 0)))
            pb.matrix = Matrix.Translation(pb.head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pb.head) @ pb.matrix
            bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")


def teaser_render(name):
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.055, 0.045, 0.035, 1)
    scene.world = world
    scene.render.resolution_x = 1100
    scene.render.resolution_y = 1100
    lo, hi = bounds()
    mid = (lo + hi) / 2
    for lname, kind_, loc, energy, size in (("Key", "AREA", (-2.2, -3.0, 2.6), 900, 2.5), ("Fill", "AREA", (2.8, -2.2, 1.4), 300, 3.0), ("Rim", "AREA", (0.5, 3.0, 2.4), 600, 2.0)):
        ld = bpy.data.lights.new(lname, kind_)
        ld.energy = energy
        ld.size = size
        ld.color = (1.0, 0.92, 0.80)
        lo_ = bpy.data.objects.new(lname, ld)
        lo_.location = loc
        bpy.context.collection.objects.link(lo_)
        direction = __import__("mathutils").Vector(mid) - lo_.location
        lo_.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam_data = bpy.data.cameras.new("TeaserCam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("TeaserCam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    width = hi[0] - lo[0]
    dist = max(width, hi[2] - lo[2]) * 1.25 / (36 / 50)
    cam.location = (mid[0], mid[1] - dist, mid[2])
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.render.filepath = os.path.join(OUT, f"{name}.png")
    bpy.ops.render.render(write_still=True)


def build_body(covered=None):
    base = mh.ObjMesh(os.path.join(mh.MH, "3dobjs", "base.npz"))
    coord = base.coord.copy()
    mh.macro(coord, mh.Targets(), AGE)
    body_group = base.groups.index("body")
    keep = base.group == body_group
    if covered is not None:
        # a face goes only when every corner is under clothes (a corner showing keeps it: no gaps at hems)
        keep &= ~covered[base.fvert].all(axis=1)
    faces = base.faces(keep)
    used = sorted({v for f, _ in faces for v in f})
    remap = {v: i for i, v in enumerate(used)}
    verts = mh.to_blender(coord[used])
    ob = mesh_object("HenryBody", verts, [[remap[v] for v in f] for f, _ in faces], base.texco, [t for _, t in faces])
    skin_tex = tex("skin", os.path.join(mh.MH, "skins", "textures", "middleage_lightskinned_male_diffuse.png"))
    ob.data.materials.append(image_material("Skin", skin_tex))
    bones = mh.skeleton(coord)
    rig = armature(bones)
    names, w = mh.weights(bones, len(coord))
    skin(ob, rig, names, w[used])
    return coord, base, rig, ob, used


def build_clothes(coord, rig):
    names, W = mh.weights(mh.skeleton(coord), len(coord))
    bones = mh.skeleton(coord)
    waist = bones["spine_01"][0][1]  # MakeHuman height (dm) of the small of the back
    suit = mh.Proxy("clothes", "male_elegantsuit01")
    shirt = mh.Proxy("clothes", "male_casualsuit01")

    hem = waist - 0.4  # dm: the waistcoat comes down over the trousers' waistband
    arm_bones = np.array([kind(n) in ("upper", "lower") for n in names])

    def suit_keep(faces, w, pos):
        uv = suit.obj.texco
        armw = w[:, arm_bones].sum(axis=1)
        keep = []
        for f, t in faces:
            jacket = uv[t][:, 1].mean() > 0.5
            if armw[f].mean() > 0.42:
                keep.append(False)  # the jacket's sleeves: it's a waistcoat now (cut where the arm takes over)
            elif jacket and pos[f][:, 1].max() < hem:
                keep.append(False)  # and it stops at the hem
            else:
                keep.append(True)
        return keep

    def suit_hem(pos, faces):
        # the hem straight: the waistcoat's corners below it come up onto it
        uv = suit.obj.texco
        for f, t in faces:
            if uv[t][:, 1].mean() > 0.5:
                low = [v for v in f if pos[v][1] < hem]
                for v in low:
                    pos[v][1] = hem
        return pos


    so, _, _ = proxy_object("HenrySuit", suit, coord, W, names, rig, suit_keep, image_material("Suit", tex("suit", suit.texture())), tuck={"torso": 1.06}, adjust=suit_hem)
    # the body vertices the waistcoat (and the shirt front under it) really covers: under there, no shirt
    vest_covered = ref_covered(suit)

    def shirt_keep(faces, w, pos):
        kinds, _ = face_kinds(faces, w, names)
        keep = []
        for (f, _), k in zip(faces, kinds):
            under_vest = vest_covered[shirt.ref[f]].all()
            keep.append(k in ("torso", "upper") and pos[f][:, 1].min() > hem and not (k == "torso" and under_vest))
        return keep

    sh, _, _ = proxy_object("HenryShirt", shirt, coord, W, names, rig, shirt_keep, image_material("Shirt", tex("shirt", shirt.texture())), tuck={"torso": 0.6})
    shoes = mh.Proxy("clothes", "shoes01")
    proxy_object("HenryShoes", shoes, coord, W, names, rig, None, image_material("Shoes", tex("shoes", shoes.texture())))
    hair = mh.Proxy("hair", "short02")
    proxy_object("HenryHair", hair, coord, W, names, rig, None, image_material("Hair", tex("hair", hair.texture()), alpha=True))
    brows = mh.Proxy("eyebrows", "eyebrow001")
    proxy_object("HenryBrows", brows, coord, W, names, rig, None, image_material("Brows", tex("hair", os.path.join(brows.dir, "eyebrow001.png"), "brows"), alpha=True))
    lashes = mh.Proxy("eyelashes", "eyelashes01")
    proxy_object("HenryLashes", lashes, coord, W, names, rig, None, image_material("Lashes", os.path.join(lashes.dir, "eyelashes01.png"), alpha=True))
    eyes = mh.Proxy("eyes", "low-poly")
    proxy_object("HenryEyes", eyes, coord, W, names, rig, None, image_material("Eyes", os.path.join(mh.MH, "eyes", "materials", "brown_eye.png")))

    # under the clothes there's no body to show through: only where what's left of each garment really covers it
    # (and MakeHuman says it may go), so the waistcoat's armholes and the rolled sleeves leave no gaps
    covered = (covered_by(suit, coord) & suit.delete) | (covered_by(shirt, coord) & shirt.delete) | (covered_by(shoes, coord) & shoes.delete)
    main = [kind(names[i]) for i in np.argmax(W, axis=1)]
    bare = np.array([m == "lower" for m in main])
    print(f"HENRY covered: {(covered & ~bare).sum()} body vertices go")
    return covered & ~bare


clear()
base = mh.ObjMesh(os.path.join(mh.MH, "3dobjs", "base.npz"))
coord0 = base.coord.copy()
mh.macro(coord0, mh.Targets(), AGE)
rig0 = None
coord, base, rig, body, used = build_body()
covered = None
if STAGE in ("clothes", "textures", "export", "teaser"):
    covered = build_clothes(coord, rig)
    bpy.data.objects.remove(body, do_unlink=True)
    for ob in list(bpy.data.objects):
        if ob.type == "MESH":
            bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.objects.remove(rig, do_unlink=True)
    coord, base, rig, body, used = build_body(covered)
    build_clothes(coord, rig)
print(f"HENRY: {sum(len(o.data.vertices) for o in skinned_meshes(rig))} vertices in {len(skinned_meshes(rig))} meshes, {len(rig.data.bones)} bones")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "henry.blend"))
if STAGE == "teaser":
    tpose(rig)
    teaser_render("teaser_henry")
else:
    render(f"stage_{STAGE}")
print("HENRY done")

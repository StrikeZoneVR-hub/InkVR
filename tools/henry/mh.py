"""
MakeHuman 1.x data (CC0), read straight from its compiled files for the Blender build of Henry.

MakeHuman units are decimetres, Y up, the figure facing +Z. Blender gets metres, Z up, facing -Y.
"""
import json
import os

import numpy as np

MH = os.environ.get("MAKEHUMAN_DATA", r"C:/Users/cgors/FathomVR/_local/tools/makehuman/app/makehuman/data")


def to_blender(c):
    """MakeHuman coordinates (dm, Y up, facing +Z) to Blender's (m, Z up, facing -Y)."""
    c = np.asarray(c, dtype=np.float64) * 0.1
    return np.stack([c[..., 0], -c[..., 2], c[..., 1]], axis=-1)


def _s(z, key):
    return z[key].tobytes().decode("utf-8", "replace") if key in z.files else ""


class ObjMesh:
    """A compiled MakeHuman OBJ (.npz): vertices, quads or triangles, UVs, face groups."""

    def __init__(self, path):
        z = np.load(path)
        self.coord = z["coord"].astype(np.float64)
        self.fvert = z["fvert"].astype(np.int64)
        self.fuvs = z["fuvs"].astype(np.int64)
        self.texco = z["texco"].astype(np.float64)
        self.group = z["group"].astype(np.int64)
        names = _s(z, "fgstr")
        idx = list(z["fgidx"]) + [len(names)]
        self.groups = [names[idx[i]:idx[i + 1]] for i in range(len(idx) - 1)]

    def faces(self, keep=None):
        """(vertex indices, uv indices) per face; triangles are stored with their last corner repeated."""
        out = []
        for f in range(len(self.fvert)):
            if keep is not None and not keep[f]:
                continue
            v = list(self.fvert[f])
            t = list(self.fuvs[f])
            if v[3] == v[2] or v[3] == v[0]:
                v, t = v[:3], t[:3]
            out.append((v, t))
        return out


class Targets:
    """targets.npz: per target, the vertices it moves and by how much (int16, thousandths of a decimetre)."""

    def __init__(self):
        self.z = np.load(os.path.join(MH, "targets.npz"), allow_pickle=True)

    def apply(self, coord, name, weight):
        if weight == 0:
            return
        key = "targets/" + name
        idx = self.z[key + ".index"].astype(np.int64)
        if idx.size == 0:
            return
        vec = self.z[key + ".vector"].astype(np.float64) * 1e-3
        coord[idx] += vec * weight


def macro(coord, targets, age_years, race="caucasian"):
    """MakeHuman's macro modifier for a man of average muscle and weight: the race's young and old targets, mixed by age."""
    age = 0.5 + 0.5 * (age_years - 25.0) / (90.0 - 25.0) if age_years >= 25 else 0.5
    old = max(0.0, min(1.0, (age - 0.5) / 0.5))
    targets.apply(coord, f"macrodetails/{race}-male-young", 1.0 - old)
    targets.apply(coord, f"macrodetails/{race}-male-old", old)
    return coord


class Proxy:
    """A clothes/hair/eyes proxy (.mhpxy): each of its vertices sits on three body vertices plus a scaled offset."""

    def __init__(self, folder, name):
        d = os.path.join(MH, folder, name)
        z = np.load(os.path.join(d, name + ".mhpxy"))
        self.name = name
        self.dir = d
        # (pieces that sit exactly on body vertices, like eyebrows, store one reference and no offsets)
        self.ref = z["ref_vIdxs"].astype(np.int64)
        if self.ref.ndim == 1:
            self.ref = self.ref[:, None]
        n, k = self.ref.shape
        self.w = z["weights"].astype(np.float64).reshape(n, k) if "weights" in z.files else np.ones((n, k)) / k
        self.off = z["offsets"].astype(np.float64) if "offsets" in z.files else np.zeros((n, 3))
        self.tmat = z["tmat_scale"].astype(np.float64) if "tmat_scale" in z.files else np.zeros(3)
        self.tidx = z["tmat_scale_idx"].astype(np.int64) if "tmat_scale_idx" in z.files else np.zeros(0, dtype=np.int64)
        self.delete = z["deleteVerts"].astype(bool) if "deleteVerts" in z.files else None
        self.license = _s(z, "lic_str")
        self.obj = ObjMesh(os.path.join(d, _s(z, "obj_file").replace(".obj", ".npz")))
        self.material = os.path.join(d, _s(z, "material_file"))

    def fit(self, coord):
        """Its vertices on a (shaped) body."""
        scale = np.ones(3)
        if len(self.tidx) >= 6:
            for i in range(3):
                a, b = self.tidx[2 * i], self.tidx[2 * i + 1]
                if self.tmat[i] > 0:
                    scale[i] = abs(coord[a][i] - coord[b][i]) / self.tmat[i]
        return (coord[self.ref] * self.w[:, :, None]).sum(axis=1) + self.off * scale

    def texture(self, kind="diffuse"):
        for f in os.listdir(self.dir):
            if f.endswith(f"_{kind}.png"):
                return os.path.join(self.dir, f)
        return None


def skeleton(coord, rig="game_engine"):
    """The rig's bones: name -> (head, tail, parent, roll, reference bones), positions from the body's joints."""
    sk = json.load(open(os.path.join(MH, "rigs", rig + ".mhskel")))
    joints = {k: coord[np.asarray(v, dtype=np.int64)].mean(axis=0) for k, v in sk["joints"].items()}
    bones = {}
    for name, b in sk["bones"].items():
        bones[name] = (joints[b["head"]], joints[b["tail"]], b["parent"], b.get("roll", 0.0), b.get("reference") or [])
    return bones


def weights(bones, nverts):
    """Per-vertex weights for the rig's bones (MakeHuman's default weights, summed over each bone's references)."""
    w = json.load(open(os.path.join(MH, "rigs", "default_weights.mhw")))["weights"]
    out = np.zeros((nverts, len(bones)), dtype=np.float64)
    names = list(bones.keys())
    default_to_rig = {}
    for i, name in enumerate(names):
        refs = bones[name][4] or [name]
        for r in refs:
            default_to_rig[r] = i
    # default bones the rig doesn't name (face, toes, twist bones) go to their nearest ancestor it does
    parents = {k: v["parent"] for k, v in json.load(open(os.path.join(MH, "rigs", "default.mhskel")))["bones"].items()}

    def mapped(dname):
        while dname is not None:
            if dname in default_to_rig:
                return default_to_rig[dname]
            dname = parents.get(dname)
        return None

    for dname, pairs in w.items():
        i = mapped(dname)
        if i is None:
            continue
        for v, wt in pairs:
            if v < nverts:
                out[int(v), i] += wt
    s = out.sum(axis=1, keepdims=True)
    s[s == 0] = 1
    return names, out / s

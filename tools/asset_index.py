"""Index named objects (GameObjects, meshes, textures, sprites, clips) in the game's data files.
Writes analysis/asset_index.tsv: file, type, name, path_id, extra."""
import os, sys, UnityPy
from UnityPy.enums import ClassIDType as C
GAME = os.environ.get("BENDY_GAME_DIR", r"C:\Program Files (x86)\Steam\steamapps\common\Bendy and the Ink Machine")
DATA = os.path.join(GAME, "Bendy and the Ink Machine_Data")
WANT = {C.GameObject, C.Mesh, C.Texture2D, C.Sprite, C.AnimationClip, C.AudioClip, C.Material, C.Shader, C.Font, C.MonoBehaviour}
files = [f for f in os.listdir(DATA) if f.startswith(("level", "sharedassets", "resources.assets", "globalgamemanagers")) and not f.endswith((".resS", ".resource"))]
os.makedirs("analysis", exist_ok=True)
out = open("analysis/asset_index.tsv", "w", encoding="utf-8")
for fn in sorted(files):
    try:
        env = UnityPy.load(os.path.join(DATA, fn))
    except Exception as e:
        print("skip", fn, e); continue
    n = 0
    for obj in env.objects:
        if obj.type not in WANT: continue
        try:
            if obj.type == C.MonoBehaviour:
                d = obj.read(check_read=False); name = getattr(d, "m_Name", "")
                if not name: continue
                extra = ""
            else:
                d = obj.read(); name = getattr(d, "m_Name", "") or getattr(d, "name", "")
                extra = ""
                if obj.type == C.Texture2D: extra = f"{d.m_Width}x{d.m_Height}"
                elif obj.type == C.Sprite: extra = f"{d.m_Rect.width:.0f}x{d.m_Rect.height:.0f}"
        except Exception as e:
            name, extra = "?", str(e)[:40]
        out.write(f"{fn}\t{obj.type.name}\t{name}\t{obj.path_id}\t{extra}\n"); n += 1
    print(fn, n, flush=True)
out.close()

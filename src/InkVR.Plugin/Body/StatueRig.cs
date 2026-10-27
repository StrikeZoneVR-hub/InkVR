using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InkVR.Body;

/// <summary>
/// A skeleton for a character the game only has as a statue (the Archives' concept Bendy: a cartoon Bendy in a
/// T-pose, his big head in two pieces, his bow tie, rubber-hose arms and big gloves): the joints are found from the
/// statue's own shape, bones are made with the game's rig names ("bn_L_forearm001"...), and its meshes become skinned
/// meshes weighted to them, so <see cref="CharacterSkin"/> wears it like any other character.
/// </summary>
public static class StatueRig
{
    private enum J
    {
        Hips, Spine, Chest, Neck, Head,
        LClav, LUpper, LFore, LHand, LKnuckle, LFingerB, LFingerC, LTip, LThumbA, LThumbB, LThumbTip,
        RClav, RUpper, RFore, RHand, RKnuckle, RFingerB, RFingerC, RTip, RThumbA, RThumbB, RThumbTip,
        LThigh, LShin, LFoot, LToe, RThigh, RShin, RFoot, RToe,
        Count
    }

    public static bool Rig(GameObject statue)
    {
        var root = statue.transform;
        var renderers = new List<MeshRenderer>(root.GetComponentsInChildren<MeshRenderer>(true));
        if (renderers.Count == 0) return false;

        // ---- facing +z: the shoes point the way he faces ----
        var pts = WorldPoints(renderers, out _);
        var b = Bounds(pts);
        var footY = b.min.y + b.size.y * 0.04f;
        var kneeY = b.min.y + b.size.y * 0.15f;
        Vector3 toes = Vector3.zero, shins = Vector3.zero;
        int nt = 0, ns = 0;
        foreach (var p in pts)
        {
            if (p.y < footY) { toes += p; nt++; }
            else if (Mathf.Abs(p.y - kneeY) < b.size.y * 0.03f) { shins += p; ns++; }
        }

        if (nt > 0 && ns > 0)
        {
            var dir = toes / nt - shins / ns;
            dir.y = 0;
            // (to the nearest quarter turn: statues stand square)
            var yaw = Mathf.Round(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg / 90f) * 90f;
            if (Mathf.Abs(yaw) > 1f) root.rotation = Quaternion.Euler(0, -yaw, 0) * root.rotation;
        }

        WholeHead(renderers);
        pts = WorldPoints(renderers, out var owner);
        b = Bounds(pts);
        var floor = b.min.y;
        var H = b.size.y;
        var maxX = Mathf.Max(Mathf.Abs(b.min.x - b.center.x), Mathf.Abs(b.max.x - b.center.x));
        var cx = b.center.x;
        var heads = new HashSet<int>();
        var ties = new HashSet<int>();
        for (var r = 0; r < renderers.Count; r++)
        {
            var n = renderers[r].name.ToLowerInvariant();
            if (n.Contains("head")) heads.Add(r);
            else if (n.Contains("tie")) ties.Add(r);
        }

        // ---- the arms: the thin hose between the body and the glove, at its height ----
        var body = new List<Vector3>();
        foreach (var i in Body(pts, owner, heads, ties)) body.Add(pts[i]);
        var bodyB = Bounds(body);
        maxX = Mathf.Max(Mathf.Abs(bodyB.min.x - cx), Mathf.Abs(bodyB.max.x - cx));
        var armBand = new List<Vector3>();
        foreach (var p in body)
        {
            var ax = Mathf.Abs(p.x - cx);
            if (ax > 0.35f * maxX && ax < 0.6f * maxX) armBand.Add(p);
        }

        if (armBand.Count < 10) return false;
        var armY = Median(armBand, v => v.y);
        var armZ = Median(armBand, v => v.z);
        var armR = Median(armBand, v => new Vector2(v.y - armY, v.z - armZ).magnitude);

        // the body's half-width a little below the arms (where they join it)
        var bodyHalf = 0f;
        foreach (var i in Body(pts, owner, heads, ties))
        {
            var p = pts[i];
            if (Mathf.Abs(p.y - (armY - 0.04f * H)) < 0.02f * H && Mathf.Abs(p.x - cx) < 0.3f * maxX) bodyHalf = Mathf.Max(bodyHalf, Mathf.Abs(p.x - cx));
        }

        if (bodyHalf <= 0) bodyHalf = 0.15f * maxX;

        // the glove's cuff: going out along the arm, where the cross-section first grows well past the hose's
        var cuff = 0.75f * maxX;
        for (var x = 0.55f * maxX; x < maxX; x += maxX * 0.01f)
        {
            var spread = 0f;
            foreach (var p in body)
                if (Mathf.Abs(Mathf.Abs(p.x - cx) - x) < maxX * 0.01f)
                    spread = Mathf.Max(spread, Mathf.Abs(p.y - armY));
            if (spread > armR * 2.6f)
            {
                cuff = x;
                break;
            }
        }

        // the glove: its height above the arm (the thumb) and how far out it reaches
        var gloveTop = armY;
        var gloveFar = cuff;
        foreach (var p in body)
        {
            var ax = Mathf.Abs(p.x - cx);
            if (ax < cuff) continue;
            gloveTop = Mathf.Max(gloveTop, p.y);
            gloveFar = Mathf.Max(gloveFar, ax);
        }

        var gloveLen = gloveFar - cuff;

        // ---- the legs: where they part (below that, nothing in the middle) ----
        var crotch = floor + 0.3f * H;
        for (var y = armY - 0.1f * H; y > floor + 0.05f * H; y -= H * 0.005f)
        {
            var middle = false;
            foreach (var i in Body(pts, owner, heads, ties))
            {
                var p = pts[i];
                if (Mathf.Abs(p.y - y) < H * 0.008f && Mathf.Abs(p.x - cx) < bodyHalf * 0.12f)
                {
                    middle = true;
                    break;
                }
            }

            if (!middle)
            {
                crotch = y;
                break;
            }
        }

        var legX = 0f;
        var legZ = 0f;
        var nl = 0;
        foreach (var p in body)
            if (Mathf.Abs(p.y - (crotch - 0.03f * H)) < 0.01f * H && Mathf.Abs(p.x - cx) < bodyHalf)
            {
                legX += Mathf.Abs(p.x - cx);
                legZ += p.z;
                nl++;
            }

        legX = nl > 0 ? legX / nl : bodyHalf * 0.4f;
        legZ = nl > 0 ? legZ / nl : armZ;
        var ankleY = floor + 0.07f * H;
        var toeZ = b.max.z;
        toeZ = float.MinValue;
        foreach (var p in body)
            if (p.y < floor + 0.06f * H)
                toeZ = Mathf.Max(toeZ, p.z);

        // the head: the bottom of its pieces is the top of the neck
        var headBottom = float.MaxValue;
        var headCenter = Vector3.zero;
        var nh = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            if (!heads.Contains(owner[i])) continue;
            headBottom = Mathf.Min(headBottom, pts[i].y);
            headCenter += pts[i];
            nh++;
        }

        if (nh == 0) return false;
        headCenter /= nh;
        headBottom = Mathf.Min(headBottom, b.max.y);

        // ---- the joints ----
        var jp = new Vector3[(int)J.Count];
        var spineZ = Median(armBand, v => v.z);
        jp[(int)J.Hips] = new Vector3(cx, crotch + 0.04f * H, legZ);
        jp[(int)J.Chest] = new Vector3(cx, armY - 0.06f * H, spineZ);
        jp[(int)J.Spine] = Vector3.Lerp(jp[(int)J.Hips], jp[(int)J.Chest], 0.5f);
        jp[(int)J.Neck] = new Vector3(cx, Mathf.Lerp(armY, headBottom, 0.4f), spineZ);
        jp[(int)J.Head] = new Vector3(cx, headBottom, headCenter.z);
        for (var s = 0; s < 2; s++)
        {
            var sx = s == 0 ? -1f : 1f; // left is -x (he faces +z)
            var o = s == 0 ? 0 : (int)J.RClav - (int)J.LClav;
            var shoulder = new Vector3(cx + sx * bodyHalf * 0.85f, armY, armZ);
            var wrist = new Vector3(cx + sx * cuff, armY, armZ);
            jp[(int)J.LClav + o] = new Vector3(cx + sx * bodyHalf * 0.3f, armY, armZ);
            jp[(int)J.LUpper + o] = shoulder;
            jp[(int)J.LFore + o] = Vector3.Lerp(shoulder, wrist, 0.5f);
            jp[(int)J.LHand + o] = wrist;
            var knuckle = wrist + new Vector3(sx * gloveLen * 0.45f, 0, 0);
            jp[(int)J.LKnuckle + o] = knuckle;
            jp[(int)J.LFingerB + o] = knuckle + new Vector3(sx * gloveLen * 0.18f, 0, 0);
            jp[(int)J.LFingerC + o] = knuckle + new Vector3(sx * gloveLen * 0.36f, 0, 0);
            jp[(int)J.LTip + o] = new Vector3(cx + sx * gloveFar, armY, armZ);
            // the thumb stands up off the top of the glove
            jp[(int)J.LThumbA + o] = wrist + new Vector3(sx * gloveLen * 0.25f, (gloveTop - armY) * 0.35f, 0);
            jp[(int)J.LThumbB + o] = wrist + new Vector3(sx * gloveLen * 0.3f, (gloveTop - armY) * 0.7f, 0);
            jp[(int)J.LThumbTip + o] = wrist + new Vector3(sx * gloveLen * 0.32f, gloveTop - armY, 0);
            var lo = s == 0 ? 0 : (int)J.RThigh - (int)J.LThigh;
            jp[(int)J.LThigh + lo] = new Vector3(cx + sx * legX, crotch, legZ);
            jp[(int)J.LFoot + lo] = new Vector3(cx + sx * legX, ankleY, legZ);
            jp[(int)J.LShin + lo] = Vector3.Lerp(jp[(int)J.LThigh + lo], jp[(int)J.LFoot + lo], 0.5f) + new Vector3(0, 0, 0.01f * H);
            jp[(int)J.LToe + lo] = new Vector3(cx + sx * legX, floor + 0.03f * H, Mathf.Lerp(legZ, toeZ, 0.7f));
        }

        // ---- the bones (the game's rig names; rotations level, the posing works from rest offsets) ----
        var names = new Dictionary<J, string>
        {
            [J.Hips] = "bn_hips001", [J.Spine] = "bn_spine_A001", [J.Chest] = "bn_chest001", [J.Neck] = "bn_neck001", [J.Head] = "bn_head001",
            [J.LClav] = "bn_L_clavicle001", [J.LUpper] = "bn_L_arm001", [J.LFore] = "bn_L_forearm001", [J.LHand] = "bn_L_hand001",
            [J.LKnuckle] = "bn_L_middleA001", [J.LFingerB] = "bn_L_middleB001", [J.LFingerC] = "bn_L_middleC001",
            [J.LThumbA] = "bn_L_thumbA001", [J.LThumbB] = "bn_L_thumbB001",
            [J.RClav] = "bn_R_clavicle001", [J.RUpper] = "bn_R_arm001", [J.RFore] = "bn_R_forearm001", [J.RHand] = "bn_R_hand001",
            [J.RKnuckle] = "bn_R_middleA001", [J.RFingerB] = "bn_R_middleB001", [J.RFingerC] = "bn_R_middleC001",
            [J.RThumbA] = "bn_R_thumbA001", [J.RThumbB] = "bn_R_thumbB001",
            [J.LThigh] = "bn_L_thigh001", [J.LShin] = "bn_L_shin001", [J.LFoot] = "bn_L_foot001", [J.LToe] = "bn_L_ball001",
            [J.RThigh] = "bn_R_thigh001", [J.RShin] = "bn_R_shin001", [J.RFoot] = "bn_R_foot001", [J.RToe] = "bn_R_ball001",
        };
        var parentOf = new Dictionary<J, J>
        {
            [J.Spine] = J.Hips, [J.Chest] = J.Spine, [J.Neck] = J.Chest, [J.Head] = J.Neck,
            [J.LClav] = J.Chest, [J.LUpper] = J.LClav, [J.LFore] = J.LUpper, [J.LHand] = J.LFore, [J.LKnuckle] = J.LHand, [J.LFingerB] = J.LKnuckle, [J.LFingerC] = J.LFingerB, [J.LThumbA] = J.LHand, [J.LThumbB] = J.LThumbA,
            [J.RClav] = J.Chest, [J.RUpper] = J.RClav, [J.RFore] = J.RUpper, [J.RHand] = J.RFore, [J.RKnuckle] = J.RHand, [J.RFingerB] = J.RKnuckle, [J.RFingerC] = J.RFingerB, [J.RThumbA] = J.RHand, [J.RThumbB] = J.RThumbA,
            [J.LThigh] = J.Hips, [J.LShin] = J.LThigh, [J.LFoot] = J.LShin, [J.LToe] = J.LFoot,
            [J.RThigh] = J.Hips, [J.RShin] = J.RThigh, [J.RFoot] = J.RShin, [J.RToe] = J.RFoot,
        };
        var bones = new Dictionary<J, Transform>();
        foreach (var j in new[]
                 {
                     J.Hips, J.Spine, J.Chest, J.Neck, J.Head, J.LClav, J.LUpper, J.LFore, J.LHand, J.LKnuckle, J.LFingerB, J.LFingerC, J.LThumbA, J.LThumbB,
                     J.RClav, J.RUpper, J.RFore, J.RHand, J.RKnuckle, J.RFingerB, J.RFingerC, J.RThumbA, J.RThumbB,
                     J.LThigh, J.LShin, J.LFoot, J.LToe, J.RThigh, J.RShin, J.RFoot, J.RToe
                 })
        {
            var t = new GameObject(names[j]).transform;
            t.SetParent(parentOf.TryGetValue(j, out var pj) ? bones[pj] : root, false);
            t.SetPositionAndRotation(jp[(int)j], Quaternion.identity);
            bones[j] = t;
        }

        // the bone segments the skin goes by (head of each bone to the next joint)
        var segs = new List<(J bone, Vector3 a, Vector3 b, int region)>
        {
            (J.Hips, jp[(int)J.Hips], jp[(int)J.Spine], 0), (J.Spine, jp[(int)J.Spine], jp[(int)J.Chest], 0), (J.Chest, jp[(int)J.Chest], jp[(int)J.Neck], 0),
            (J.Neck, jp[(int)J.Neck], jp[(int)J.Head], 0),
        };
        for (var s = 0; s < 2; s++)
        {
            var o = s == 0 ? 0 : (int)J.RClav - (int)J.LClav;
            var lo = s == 0 ? 0 : (int)J.RThigh - (int)J.LThigh;
            var arm = 1 + s;
            var leg = 3 + s;
            segs.Add(((J)((int)J.LUpper + o), jp[(int)J.LUpper + o], jp[(int)J.LFore + o], arm));
            segs.Add(((J)((int)J.LFore + o), jp[(int)J.LFore + o], jp[(int)J.LHand + o], arm));
            segs.Add(((J)((int)J.LHand + o), jp[(int)J.LHand + o], jp[(int)J.LKnuckle + o], arm));
            segs.Add(((J)((int)J.LKnuckle + o), jp[(int)J.LKnuckle + o], jp[(int)J.LFingerB + o], arm));
            segs.Add(((J)((int)J.LFingerB + o), jp[(int)J.LFingerB + o], jp[(int)J.LFingerC + o], arm));
            segs.Add(((J)((int)J.LFingerC + o), jp[(int)J.LFingerC + o], jp[(int)J.LTip + o], arm));
            segs.Add(((J)((int)J.LThumbA + o), jp[(int)J.LThumbA + o], jp[(int)J.LThumbB + o], arm));
            segs.Add(((J)((int)J.LThumbB + o), jp[(int)J.LThumbB + o], jp[(int)J.LThumbTip + o], arm));
            segs.Add(((J)((int)J.LThigh + lo), jp[(int)J.LThigh + lo], jp[(int)J.LShin + lo], leg));
            segs.Add(((J)((int)J.LShin + lo), jp[(int)J.LShin + lo], jp[(int)J.LFoot + lo], leg));
            segs.Add(((J)((int)J.LFoot + lo), jp[(int)J.LFoot + lo], jp[(int)J.LToe + lo], leg));
        }

        // ---- every mesh skinned to them ----
        var boneList = new List<Transform>(bones.Values);
        var index = new Dictionary<J, int>();
        var k = 0;
        foreach (var kv in bones) index[kv.Key] = k++;
        for (var r = 0; r < renderers.Count; r++)
        {
            var mr = renderers[r];
            var mf = mr.GetComponent<MeshFilter>();
            var src = mf != null ? mf.sharedMesh : null;
            if (src == null) continue;
            var mats = mr.sharedMaterials;
            var go = mr.gameObject;
            var mesh = Object.Instantiate(src);
            mesh.name = src.name + "_Rigged";
            var toWorld = go.transform.localToWorldMatrix;
            var verts = mesh.vertices;
            var weights = new List<(int b0, int b1, float w1)>(verts.Length);
            var rigid = heads.Contains(r) ? index[J.Head] : ties.Contains(r) ? index[J.Chest] : -1;
            foreach (var v in verts)
            {
                if (rigid >= 0)
                {
                    weights.Add((rigid, -1, 0));
                    continue;
                }

                var p = toWorld.MultiplyPoint3x4(v);
                if (p.y >= headBottom - 0.01f * H && Mathf.Abs(p.x - cx) < bodyHalf * 1.6f)
                {
                    // (the lower face is modelled with the body)
                    weights.Add((index[J.Head], -1, 0));
                    continue;
                }

                var region = RegionOf(p, cx, bodyHalf, crotch, armY, H);
                int best = -1, second = -1;
                float d1 = float.MaxValue, d2 = float.MaxValue;
                for (var i = 0; i < segs.Count; i++)
                {
                    // a limb's bones only take that limb (and the torso blends into the limbs' roots)
                    if (segs[i].region != region && !(region == 0 && segs[i].region > 0 && IsRoot(segs[i].bone)) && !(region > 0 && segs[i].region == 0)) continue;
                    var d = SegDistance(p, segs[i].a, segs[i].b);
                    if (d < d1)
                    {
                        d2 = d1;
                        second = best;
                        d1 = d;
                        best = i;
                    }
                    else if (d < d2)
                    {
                        d2 = d;
                        second = i;
                    }
                }

                var b0 = index[segs[best].bone];
                if (second < 0 || segs[second].bone == segs[best].bone)
                {
                    weights.Add((b0, -1, 0));
                    continue;
                }

                var w0 = 1f / (Mathf.Pow(d1, 4) + 1e-9f);
                var w1 = 1f / (Mathf.Pow(d2, 4) + 1e-9f);
                weights.Add((b0, index[segs[second].bone], w1 / (w0 + w1)));
            }

            var bind = new Matrix4x4[boneList.Count];
            for (var i = 0; i < bind.Length; i++) bind[i] = boneList[i].worldToLocalMatrix * toWorld;
            mesh.bindposes = bind;
            SetWeights(mesh, weights);
            Object.DestroyImmediate(mr);
            if (mf != null) Object.DestroyImmediate(mf);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = boneList.ToArray();
            smr.rootBone = bones[J.Hips];
            smr.sharedMaterials = mats;
            smr.updateWhenOffscreen = true;
        }

        Plugin.L.LogInfo($"Statue rigged: arms at {armY - floor:F2} (hose {armR:F2}, cuff {cuff:F2}), body {bodyHalf:F2}, crotch {crotch - floor:F2}, neck {headBottom - floor:F2}, height {H:F2}");
        return true;
    }

    /// <summary>
    /// The concept statue's head is a cutaway: one half shows the gears inside. That half goes, and the other (the face,
    /// one eye, one horn) is mirrored across the cut to make the whole head.
    /// </summary>
    private static void WholeHead(List<MeshRenderer> renderers)
    {
        var heads = renderers.FindAll(r => r.name.ToLowerInvariant().Contains("head"));
        if (heads.Count != 2) return;
        int Count(MeshRenderer r) => r.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount ?? 0;
        var big = Count(heads[0]) >= Count(heads[1]) ? heads[0] : heads[1];
        var cut = big == heads[0] ? heads[1] : heads[0];
        if (Count(big) < Count(cut) * 3) return;
        var src = big.GetComponent<MeshFilter>().sharedMesh;
        var m = big.transform.localToWorldMatrix;
        var inv = big.transform.worldToLocalMatrix;
        // the cut is the face half's side towards the cutaway
        var verts = src.vertices;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var v in verts)
        {
            var w = m.MultiplyPoint3x4(v);
            lo = Mathf.Min(lo, w.x);
            hi = Mathf.Max(hi, w.x);
        }

        // (renderer bounds are empty while the model waits inactive: go by the vertices)
        float CenterX(MeshRenderer r)
        {
            var mm = r.transform.localToWorldMatrix;
            var vs = r.GetComponent<MeshFilter>().sharedMesh.vertices;
            var sum = 0f;
            foreach (var v in vs) sum += mm.MultiplyPoint3x4(v).x;
            return vs.Length > 0 ? sum / vs.Length : 0f;
        }

        var cutSide = CenterX(cut) < CenterX(big) ? lo : hi;

        // The half is his right side (one eye, one horn, half the grin) and the other side is a gear plate showing
        // the works inside: the whole head is the half and its mirror image across the cut.
        var mirrored = Object.Instantiate(src);
        mirrored.name = src.name + "_Mirrored";
        var normals = src.normals;
        for (var i = 0; i < verts.Length; i++)
        {
            var w = m.MultiplyPoint3x4(verts[i]);
            w.x = 2f * cutSide - w.x;
            verts[i] = inv.MultiplyPoint3x4(w);
            if (i < normals.Length)
            {
                var n = m.MultiplyVector(normals[i]);
                n.x = -n.x;
                normals[i] = inv.MultiplyVector(n).normalized;
            }
        }

        mirrored.vertices = verts;
        if (normals.Length == verts.Length) mirrored.normals = normals;
        for (var s = 0; s < mirrored.subMeshCount; s++)
        {
            var tris = mirrored.GetTriangles(s);
            var flipped = new int[tris.Length];
            for (var t = 0; t + 2 < tris.Length; t += 3)
            {
                flipped[t] = tris[t];
                flipped[t + 1] = tris[t + 2];
                flipped[t + 2] = tris[t + 1];
            }

            mirrored.SetTriangles(flipped, s);
        }

        mirrored.RecalculateBounds();
        var go = new GameObject(big.name + "_Other");
        go.transform.SetParent(big.transform.parent, false);
        go.transform.localPosition = big.transform.localPosition;
        go.transform.localRotation = big.transform.localRotation;
        go.transform.localScale = big.transform.localScale;
        go.AddComponent<MeshFilter>().sharedMesh = mirrored;
        go.AddComponent<MeshRenderer>().sharedMaterials = big.sharedMaterials;
        renderers.Add(go.GetComponent<MeshRenderer>());
        renderers.Remove(cut);
        Object.DestroyImmediate(cut.gameObject);
    }

    /// <summary>The materials with their pictures swapped for plain ink black (the back of Bendy's head).</summary>
    private static Material[] Black(Material[] mats)
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "InkVR_InkBlack" };
        var px = new Color32[16];
        for (var i = 0; i < px.Length; i++) px[i] = new Color32(16, 15, 14, 255);
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var outp = new Material[mats.Length];
        for (var i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            var mat = new Material(mats[i]) { name = mats[i].name + "_Back" };
            foreach (var slot in new[] { "_MainTex", "_MainTexture" })
                if (mat.HasProperty(slot))
                    mat.SetTexture(slot, tex);
            outp[i] = mat;
        }

        return outp;
    }

    private static bool IsRoot(J j) => j is J.LUpper or J.RUpper or J.LThigh or J.RThigh;

    /// <summary>0 torso and head, 1/2 the left/right arm (out past the body), 3/4 the left/right leg (below the crotch).</summary>
    private static int RegionOf(Vector3 p, float cx, float bodyHalf, float crotch, float armY, float h)
    {
        var x = p.x - cx;
        if (Mathf.Abs(x) > bodyHalf * 0.95f && Mathf.Abs(p.y - armY) < 0.25f * h) return x < 0 ? 1 : 2;
        if (p.y < crotch) return x < 0 ? 3 : 4;
        return 0;
    }

    private static float SegDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (p - (a + ab * t)).magnitude;
    }

    private static IEnumerable<int> Body(List<Vector3> pts, List<int> owner, HashSet<int> heads, HashSet<int> ties)
    {
        for (var i = 0; i < pts.Count; i++)
            if (!heads.Contains(owner[i]) && !ties.Contains(owner[i]))
                yield return i;
    }

    private static List<Vector3> WorldPoints(List<MeshRenderer> renderers, out List<int> owner)
    {
        var pts = new List<Vector3>();
        owner = new List<int>();
        for (var r = 0; r < renderers.Count; r++)
        {
            var mf = renderers[r].GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) continue;
            var m = renderers[r].transform.localToWorldMatrix;
            foreach (var v in mesh.vertices)
            {
                pts.Add(m.MultiplyPoint3x4(v));
                owner.Add(r);
            }
        }

        return pts;
    }

    private static Bounds Bounds(List<Vector3> pts)
    {
        var b = new Bounds(pts[0], Vector3.zero);
        foreach (var p in pts) b.Encapsulate(p);
        return b;
    }

    private static float Median(List<Vector3> pts, Func<Vector3, float> f)
    {
        var v = new List<float>(pts.Count);
        foreach (var p in pts) v.Add(f(p));
        v.Sort();
        return v[v.Count / 2];
    }

    private static unsafe void SetWeights(Mesh mesh, List<(int b0, int b1, float w1)> weights)
    {
        var n = weights.Count;
        var total = 0;
        foreach (var w in weights) total += w.b1 >= 0 ? 2 : 1;
        var counts = (byte*)Marshal.AllocHGlobal(n);
        var records = (byte*)Marshal.AllocHGlobal(total * 8);
        try
        {
            var r = 0;
            for (var i = 0; i < n; i++)
            {
                var w = weights[i];
                var two = w.b1 >= 0;
                counts[i] = (byte)(two ? 2 : 1);
                var heavy0 = !two || w.w1 <= 0.5f;
                if (heavy0)
                {
                    *(float*)(records + r * 8) = two ? 1f - w.w1 : 1f;
                    *(int*)(records + r * 8 + 4) = w.b0;
                    r++;
                    if (!two) continue;
                    *(float*)(records + r * 8) = w.w1;
                    *(int*)(records + r * 8 + 4) = w.b1;
                    r++;
                }
                else
                {
                    *(float*)(records + r * 8) = w.w1;
                    *(int*)(records + r * 8 + 4) = w.b1;
                    r++;
                    *(float*)(records + r * 8) = 1f - w.w1;
                    *(int*)(records + r * 8 + 4) = w.b0;
                    r++;
                }
            }

            mesh.InternalSetBoneWeights((IntPtr)counts, n, (IntPtr)records, total);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)counts);
            Marshal.FreeHGlobal((IntPtr)records);
        }
    }
}

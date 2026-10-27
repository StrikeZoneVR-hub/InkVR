using System.Collections.Generic;
using InkVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace InkVR.UI;

/// <summary>
/// A real mirror in the theatre: each eye gets its own reflection, rendered from that eye reflected through the glass
/// with an off-axis projection through the mirror's corners, so the depth is right in a headset and the reflection
/// stays put as you move. Your whole body shows in it, head and all (the head is drawn only for the mirror: you
/// look out of it). It renders only when you can see it.
/// </summary>
public sealed class Mirror
{
    private static readonly List<Mirror> All = new();
    private static Camera.CameraCallback _hook;
    private static Camera _cam;

    private readonly Transform _t;
    private readonly float _w, _h;
    private readonly RenderTexture[] _rt = new RenderTexture[2];
    private readonly Material _mat;

    /// <summary>The glass: centre, facing <c>forward</c> (into the room), <paramref name="w"/> by <paramref name="h"/> metres.</summary>
    public Mirror(Transform parent, Vector3 localPos, Quaternion localRot, float w, float h, Color tint)
    {
        var go = new GameObject("InkVR_Mirror") { layer = VRLayers.UI };
        _t = go.transform;
        _t.SetParent(parent, false);
        _t.localPosition = localPos;
        _t.localRotation = localRot;
        _w = w;
        _h = h;

        // the quad faces +z; u runs with local x (the mirror camera's own left to right), so the image lands where it belongs
        var mesh = new Mesh { name = "InkVR_MirrorGlass" };
        mesh.SetVertices(new[] { new Vector3(-w * 0.5f, -h * 0.5f, 0), new Vector3(w * 0.5f, -h * 0.5f, 0), new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(-w * 0.5f, h * 0.5f, 0) });
        mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
        mesh.SetColors(new[] { tint, tint, tint, tint });
        mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0); // clockwise seen from the front (+z)
        mesh.RecalculateBounds();
        Keep(mesh);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        _mat = new Material(Shader.Find("UI/Default")) { name = "InkVR_Mirror", renderQueue = 2002 };
        _mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
        Keep(_mat);
        // depth first, so the glass hides what's behind it (signs and glows drawn later included)
        var depthMat = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "InkVR_MirrorDepth", renderQueue = 1999 };
        depthMat.SetInt("_SrcBlend", (int)BlendMode.Zero);
        depthMat.SetInt("_DstBlend", (int)BlendMode.One);
        depthMat.SetInt("_Cull", (int)CullMode.Back);
        depthMat.SetInt("_ZWrite", 1);
        depthMat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        Keep(depthMat);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = new[] { depthMat, _mat };
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        var px = 900;
        var py = Mathf.Clamp(Mathf.RoundToInt(px * h / w), 64, 1400);
        for (var i = 0; i < 2; i++)
        {
            _rt[i] = new RenderTexture(px, py, 24) { name = "InkVR_MirrorEye" + i, antiAliasing = 2 };
            _rt[i].Create();
            Keep(_rt[i]);
        }

        _mat.mainTexture = _rt[0];
        All.Add(this);
        Hook();
    }

    public Transform Transform => _t;

    /// <summary>Kept through scene loads and the game's unloading of unused assets (a lost texture shows white).</summary>
    private static void Keep(Object o)
    {
        o.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Object.DontDestroyOnLoad(o);
    }

    private static void Hook()
    {
        if (_hook != null) return;
        _hook = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new System.Action<Camera>(BeforeCull));
        var existing = Camera.onPreCull;
        Camera.onPreCull = existing == null ? _hook : Il2CppSystem.Delegate.Combine(existing, _hook).Cast<Camera.CameraCallback>();
    }

    private static int _frame = -1, _shown;

    /// <summary>Dev: how many eye renders the mirrors made last frame, and the last eye seen.</summary>
    public static string Describe() => $"mirrors={All.Count} rendersLastFrame={_shown} lastEye={_lastEye}";

    private static Camera.MonoOrStereoscopicEye _lastEye;
    private static int _rendersThisFrame;

    private static void BeforeCull(Camera cam)
    {
        if (All.Count == 0 || cam == null || cam != VRRig.CinemaCamera || _rendering) return;
        if (Time.frameCount != _frame)
        {
            _shown = _rendersThisFrame;
            _rendersThisFrame = 0;
            _frame = Time.frameCount;
        }

        var active = cam.stereoActiveEye;
        _lastEye = active;
        var eye = active == Camera.MonoOrStereoscopicEye.Right ? 1 : 0;
        Vector3 eyePos;
        if (cam.stereoEnabled && active != Camera.MonoOrStereoscopicEye.Mono)
            eyePos = cam.GetStereoViewMatrix(eye == 1 ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left).inverse.GetColumn(3);
        else eyePos = cam.transform.position;

        for (var i = All.Count - 1; i >= 0; i--)
        {
            var m = All[i];
            if (m._t == null)
            {
                All.RemoveAt(i);
                continue;
            }

            if (m._t.gameObject.activeInHierarchy) m.Render(cam, eye, eyePos);
        }
    }

    private static bool _rendering;

    /// <summary>Dev: save each mirror's eye images (and, with render, a fresh render from outside any camera's callback).</summary>
    public static void DevDump(string dir, bool render)
    {
        System.IO.Directory.CreateDirectory(dir);
        for (var i = 0; i < All.Count; i++)
        {
            var m = All[i];
            if (render && VRRig.CinemaCamera != null) m.Render(VRRig.CinemaCamera, 0, VRRig.CinemaCamera.transform.position);
            for (var e = 0; e < 2; e++)
            {
                var rt = m._rt[e];
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"mirror{i}_eye{e}{(render ? "_fresh" : "")}.png"), ImageConversion.EncodeToPNG(tex));
                Object.Destroy(tex);
            }
        }
    }

    private void Render(Camera main, int eye, Vector3 eyePos)
    {
        var n = _t.forward;
        var c = _t.position;
        var d = Vector3.Dot(eyePos - c, n);
        if (d < 0.02f) return; // behind the glass
        var to = c - eyePos;
        var dist = to.magnitude;
        if (dist > 9f) return;
        // in view? (near enough, or within the eye's wide cone)
        if (dist > 1.2f && Vector3.Angle(main.transform.forward, to) > 75f) return;

        if (_cam == null)
        {
            _cam = new GameObject("InkVR_MirrorCamera").AddComponent<Camera>();
            Object.DontDestroyOnLoad(_cam.gameObject);
            _cam.enabled = false;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.renderingPath = RenderingPath.Forward;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
        }

        // the eye, reflected through the glass, looking back through it into the room
        var reflected = eyePos - 2f * d * n;
        _cam.transform.SetPositionAndRotation(reflected, Quaternion.LookRotation(n, _t.up));
        _cam.cullingMask = main.cullingMask;
        _cam.backgroundColor = main.backgroundColor;

        // A plain frustum straight through the glass, wide enough for the mirror's corners, its near plane just past the
        // glass (what's behind the mirror isn't in it); the glass shows the part of the picture inside its corners.
        // (The camera faces straight through the glass with the mirror's own up, so the glass is parallel to the picture
        // and that part maps onto it exactly. An off-axis projection matrix broke the theatre's depth-tested drawing.)
        var s = _t.lossyScale;
        var hw = _w * 0.5f * s.x;
        var hh = _h * 0.5f * s.y;
        var local = _cam.transform.InverseTransformPoint(c);
        var depth = Mathf.Max(0.02f, local.z);
        var tx = Mathf.Max(Mathf.Abs(local.x - hw), Mathf.Abs(local.x + hw)) / depth * 1.01f;
        var ty = Mathf.Max(Mathf.Abs(local.y - hh), Mathf.Abs(local.y + hh)) / depth * 1.01f;
        _cam.ResetProjectionMatrix();
        _cam.nearClipPlane = depth + 0.004f * Mathf.Max(1f, s.x);
        _cam.farClipPlane = 40f * Mathf.Max(1f, s.x);
        _cam.fieldOfView = 2f * Mathf.Atan(ty) * Mathf.Rad2Deg;
        _cam.aspect = tx / ty;
        var u0 = 0.5f + (local.x - hw) / (2f * depth * tx);
        var u1 = 0.5f + (local.x + hw) / (2f * depth * tx);
        var v0 = 0.5f + (local.y - hh) / (2f * depth * ty);
        var v1 = 0.5f + (local.y + hh) / (2f * depth * ty);
        _cam.targetTexture = _rt[eye];

        _rendering = true;
        var body = Body.Hands.Body;
        body?.ShowHead(true);
        try
        {
            _cam.Render();
        }
        finally
        {
            body?.ShowHead(false);
            _rendering = false;
        }

        _mat.mainTexture = _rt[eye];
        _mat.mainTextureOffset = new Vector2(u0, v0);
        _mat.mainTextureScale = new Vector2(u1 - u0, v1 - v0);
        _rendersThisFrame++;
    }
}

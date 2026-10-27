using System;
using System.IO;
using InkVR.Core;
using UnityEngine;

namespace InkVR.UI;

/// <summary>
/// The Fun Hall's extras: the photo booth (look at the lens, a countdown, a flash, and a framed photo of you, head and
/// all, saved to your Pictures folder and printed in the booth's slot) and what the prizes do once they're yours.
/// </summary>
public static partial class Arcade
{
    // =====================================================================================================
    // The photo booth
    // =====================================================================================================

    private static float _photoLook, _photoCount = -1;
    private static int _photoTick;
    private static Texture2D _print;
    private static GameObject _printQuad;

    private static void PhotoBooth()
    {
        var eye = TheatreWalk.Eye;
        var inside = eye.x > Theatre.ArcadeX0Pub + 0.1f && eye.x < 10.05f && eye.z > -16.95f && eye.z < Theatre.ArcadeZ1Pub - 0.1f;
        var lens = Theatre.PhotoLens;
        if (_photoCount < 0)
        {
            _photoLook = inside && TheatreToys.Sees(lens, 22f, 2.2f) ? _photoLook + _dt : 0f;
            if (_photoLook > 0.8f)
            {
                _photoCount = 3.2f;
                _photoTick = 4;
            }

            SetSign("PhotoInfo", inside ? "LOOK AT THE LENS" : "STEP INSIDE");
            return;
        }

        _photoCount -= _dt;
        var tick = Mathf.CeilToInt(_photoCount);
        if (tick < _photoTick && tick > 0)
        {
            _photoTick = tick;
            SetSign("PhotoInfo", tick.ToString());
            TheatreSounds.Play(TheatreSounds.Click, lens, 0.4f, 1.8f);
        }

        if (_photoCount > 0) return;
        _photoCount = -1;
        _photoLook = -3f; // (not straight away again)
        SetSign("PhotoInfo", "SMILE!");
        TheatreSounds.Play(TheatreSounds.Shutter, lens, 0.7f);
        _t.SetRoomTint(Theatre.Room.Arcade, new Color(1.8f, 1.8f, 1.7f, 1f));
        _flashUntil = _now + 0.12f;
        try
        {
            TakePhoto();
            FanFinds.Mark("photo");
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Photo booth: " + e.Message);
        }
    }

    private static float _flashUntil = -1;

    private static void TakePhoto()
    {
        var head = VRRig.Head;
        var root = _cinema.Root;
        if (head == null || root == null) return;
        const int w = 1200, h = 900, border = 48, caption = 96;
        var go = new GameObject("InkVR_PhotoCam");
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.cullingMask = VRLayers.UIMask;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.04f, 0.03f);
        cam.fieldOfView = 52f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 40f;
        var from = root.TransformPoint(Theatre.PhotoLens + Vector3.back * 0.06f);
        go.transform.position = from;
        go.transform.rotation = Quaternion.LookRotation(head.position + Vector3.down * 0.12f - from, Vector3.up);
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        var body = Body.Hands.Body;
        body?.ShowHead(true);
        try
        {
            cam.Render();
        }
        finally
        {
            body?.ShowHead(false);
        }

        // the print: the picture in a cream border, a caption under it
        int pw = w + border * 2, ph = h + border + caption;
        var tex = new Texture2D(pw, ph, TextureFormat.RGB24, false) { name = "InkVR_Photo" };
        var fill = new Color32[pw * ph];
        var cream = default(Color32);
        cream.r = 240;
        cream.g = 228;
        cream.b = 200;
        cream.a = 255;
        for (var i = 0; i < fill.Length; i++) fill[i] = cream;
        tex.SetPixels32(fill);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), border, caption);
        RenderTexture.active = prev;
        var strip = new InkCanvas(pw, caption, new Color(0.94f, 0.89f, 0.78f), Environment.TickCount);
        var text = "INKVR  -  THE FUN HALL  -  " + DateTime.Now.ToString("d MMMM yyyy").ToUpperInvariant();
        TheatreArt.InkText(strip, text, pw * 0.5f - TheatreArt.InkTextWidth(text, 34) * 0.5f, 30, 34, 4f, new Color(0.08f, 0.05f, 0.04f));
        strip.Stroke(border, caption - 8, pw - border, caption - 8, 3f, new Color(0.08f, 0.05f, 0.04f), 0.3f);
        tex.SetPixels(0, 0, pw, caption, strip.Px);
        tex.Apply(false, false);

        var name = "InkVR-FunHall-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
        var dir = Dev.DevSaveGuard.Active ? Path.Combine(Plugin.DataDir, "shots") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "InkVR");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), ImageConversion.EncodeToPNG(tex));
        Plugin.L.LogInfo("Photo booth: saved " + Path.Combine(dir, name));
        cam.targetTexture = null;
        UnityEngine.Object.Destroy(go);
        UnityEngine.Object.Destroy(rt);

        // and printed out of the slot on the booth's side
        if (_print != null) UnityEngine.Object.Destroy(_print);
        _print = tex;
        _print.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        if (_printQuad == null)
        {
            _printQuad = new GameObject("InkVR_PhotoPrint") { layer = VRLayers.UI };
            _printQuad.transform.SetParent(root, false);
            var mb = new Rendering.MeshBuilder();
            mb.FlatQuad(Vector3.zero, new Vector3(0, 0, -0.2f), new Vector3(0, 0.165f, 0));
            _printQuad.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("InkVR_PhotoPrint");
            var mat = new Material(Shader.Find("Unlit/Texture"));
            _printQuad.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        _printQuad.transform.localPosition = new Vector3(10.27f, Theatre.PhotoLens.y + 0.2f, -16.0f);
        _printQuad.GetComponent<MeshRenderer>().sharedMaterial.mainTexture = _print;
        _printQuad.SetActive(true);
    }

    // =====================================================================================================
    // Prizes, once they're yours
    // =====================================================================================================

    private static readonly Holdable[] Plushes = new Holdable[3];
    private static Vector3 _balloon, _balloonVel;

    private static void SetupPrizes()
    {
        for (var i = 0; i < 3; i++) Plushes[i] = Make("Prize" + i, Vector3.up * 0.1f, Quaternion.identity, 0.14f, false);
    }

    private static void PrizeEffects()
    {
        // the plushes: yours to pick up and throw about (they stay where they land, till the theatre next opens)
        for (var i = 0; i < 3; i++)
        {
            var p = Plushes[i];
            if (p == null) continue;
            if (!Owns(i) && p.Hand >= 0) Home(p);
            if (!p.Loose) continue;
            if (!Fly(p, 0.1f, 0.25f)) p.Loose = false;
        }

        // the balloon: floats over your left hand on its string, bobbing
        var balloon = _t.Piece("Prize3");
        if (balloon != null)
        {
            if (Wearing(3) && TheatreToys.HandOk(0))
            {
                var want = TheatreToys.PalmOf(0) + new Vector3(Mathf.Sin(_now * 1.3f) * 0.07f, 0.95f, Mathf.Cos(_now * 0.9f) * 0.06f);
                if ((_balloon - want).sqrMagnitude > 4f) _balloon = want;
                _balloonVel += ((want - _balloon) * 18f - _balloonVel * 4f) * _dt;
                _balloon += _balloonVel * _dt;
                balloon.localPosition = _balloon - Vector3.up * 0.28f;
                balloon.localScale = Vector3.one * 0.85f;
                balloon.localRotation = Quaternion.Euler(_balloonVel.z * 8f, 0, -_balloonVel.x * 8f);
            }
            else
            {
                balloon.localPosition = _t.PivotOf("Prize3");
                balloon.localRotation = Quaternion.identity;
                balloon.localScale = Vector3.one;
                _balloon = balloon.localPosition + Vector3.up * 0.28f;
            }
        }

        // the hat and the bow tie are worn (you see them in the mirror); the ink laser is LaserPointer's;
        // the golden Bendy stands on its pedestal in the lobby once it's won
        Body.Hands.Body?.SetAccessories(Wearing(4), Wearing(7));
        LaserPointer.Inky = Wearing(5);
        var trophy = _t.Piece("Trophy");
        if (trophy != null && trophy.gameObject.activeSelf != Owns(6)) trophy.gameObject.SetActive(Owns(6));
        SetSign("TrophyTag", Owns(6) ? "THE GOLDEN BENDY" : "WIN ME IN THE FUN HALL");
        var shelf = _t.Piece("Prize6");
        if (shelf != null && shelf.gameObject.activeSelf == Owns(6)) shelf.gameObject.SetActive(!Owns(6));

        if (_flashUntil > 0 && _now > _flashUntil)
        {
            _flashUntil = -1;
            _t.SetRoomTint(Theatre.Room.Arcade, Color.white);
        }
    }
}

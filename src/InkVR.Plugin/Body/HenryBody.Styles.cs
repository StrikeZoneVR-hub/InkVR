using System.Collections.Generic;
using InkVR.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace InkVR.Body;

/// <summary>
/// The other ten bodies (Bendy, Boris, Alice Angel, the Ink Demon, Sammy, the Projectionist, Brute Boris, a Searcher,
/// Joey Drew, Wally Franks) on the same ten-bone skeleton, plus every style's head. You look out of the head, so it is
/// never drawn for you: it casts your shadow (horns, ears and halo included) and shows in the theatre's mirrors.
/// Each style's clothes keep torso, legs and feet as separate materials, so ink lands on them the way it does on Henry.
/// </summary>
public sealed partial class HenryBody
{
    // Henry's torso rings (shared by every style; the radii change)
    private static readonly Vector3[] TorsoAt =
    {
        new(0, 0.97f, 0), new(0, 1.05f, 0.006f), new(0, 1.15f, 0.012f), new(0, 1.25f, 0.012f), new(0, 1.34f, 0.004f), new(0, 1.4f, -0.012f), new(0, 1.445f, -0.022f), new(0, 1.47f, -0.026f)
    };

    private static readonly float[] ToonTorso = { 0.176f, 0.174f, 0.18f, 0.195f, 0.205f, 0.19f, 0.14f, 0.075f };
    private static readonly float[] SlimTorso = { 0.15f, 0.145f, 0.15f, 0.165f, 0.176f, 0.168f, 0.125f, 0.07f };

    private static float[] Scaled(float[] r, float k)
    {
        var o = new float[r.Length];
        for (var i = 0; i < r.Length; i++) o[i] = r[i] * k;
        return o;
    }

    private void Torso(string mat, float[] radii, float outline, float depth = 0.68f, float shoulder = 0.07f)
    {
        Add(Tube(TorsoAt, radii, Vector3.forward, 30, false, false, new Vector2(1f, depth)), mat, outline, TorsoBones);
        var r = radii[4];
        foreach (var s in new[] { -1f, 1f })
            Add(Blob(new Vector3((r - 0.02f) * s, 1.405f, -0.02f), new Vector3(shoulder, shoulder * 0.93f, shoulder)), mat, outline * 0.8f, B.Chest);
    }

    private void Hips(string mat, float r, float outline, float depth = 0.74f)
    {
        Add(Tube(new[] { new Vector3(0, 0.84f, 0), new Vector3(0, 0.92f, 0.003f), new Vector3(0, 1.0f, 0.006f) }, new[] { r * 0.93f, r, r * 0.99f }, Vector3.forward, 28, true, false, new Vector2(1f, depth)), mat, outline, B.Pelvis);
    }

    private void Legs(string mat, float[] radii, float outline)
    {
        foreach (var s in new[] { -1f, 1f })
            Add(Tube(new[]
                {
                    new Vector3(0.095f * s, 0.98f, 0), new Vector3(0.095f * s, 0.86f, 0.006f), new Vector3(0.095f * s, 0.7f, 0.012f), new Vector3(0.095f * s, 0.52f, 0.018f),
                    new Vector3(0.096f * s, 0.36f, 0.012f), new Vector3(0.098f * s, 0.22f, 0), new Vector3(0.1f * s, 0.14f, -0.006f), new Vector3(0.1f * s, 0.11f, -0.008f)
                }, radii, Vector3.forward, 22, true, true), mat, outline,
                s < 0 ? new[] { B.LThigh, B.LShin } : new[] { B.RThigh, B.RShin });
    }

    private static float[] LegRadii(float thigh, float ankle) => new[] { thigh, thigh * 0.96f, thigh * 0.86f, thigh * 0.77f, Mathf.Lerp(thigh * 0.7f, ankle, 0.5f), ankle, ankle * 1.03f, ankle * 1.03f };

    private void CartoonShoes(string mat, string sole, float size)
    {
        foreach (var s in new[] { -1f, 1f })
        {
            var a = Ankle(s);
            var foot = s < 0 ? B.LFoot : B.RFoot;
            Add(Blob(a + new Vector3(0, -0.02f, 0.05f), new Vector3(0.08f, 0.068f, 0.165f) * size), mat, 0.0079f, foot);
            Add(Blob(a + new Vector3(0, -0.015f, 0.17f * size), new Vector3(0.095f, 0.078f, 0.095f) * size), mat, 0.0079f, foot);
            Add(Blob(a + new Vector3(0, -0.07f, 0.065f), new Vector3(0.09f, 0.018f, 0.2f) * size, 18, 6), sole, 0.0053f, foot);
        }
    }

    private void Boots(string mat, string sole, float size = 1f)
    {
        foreach (var s in new[] { -1f, 1f })
        {
            var a = Ankle(s);
            var foot = s < 0 ? B.LFoot : B.RFoot;
            Add(Tube(new[] { a + new Vector3(0, -0.03f, -0.006f), a + new Vector3(0, 0.08f, -0.01f) }, new[] { 0.06f * size, 0.058f * size }, Vector3.forward, 16, false, false), mat, 0.0035f, foot);
            Add(Blob(a + new Vector3(0, -0.032f, 0.04f), new Vector3(0.056f, 0.052f, 0.135f) * size), mat, 0.0035f, foot);
            Add(Blob(a + new Vector3(0, -0.037f, 0.12f), new Vector3(0.058f, 0.046f, 0.072f) * size), mat, 0.0035f, foot);
            Add(Blob(a + new Vector3(0, -0.074f, 0.045f), new Vector3(0.062f, 0.013f, 0.16f) * size, 18, 6), sole, 0.0025f, foot);
        }
    }

    /// <summary>Overalls: a bib over the chest, straps over the shoulders, brass buttons.</summary>
    private void Overalls(string bib, float chestR, float outline)
    {
        var z = chestR * 0.68f + 0.006f;
        Add(Tube(new[] { new Vector3(0, 1.0f, z - 0.01f), new Vector3(0, 1.12f, z), new Vector3(0, 1.27f, z + 0.004f) }, new[] { 0.1f, 0.1f, 0.095f }, Vector3.forward, 12, false, false, new Vector2(1f, 0.12f)), bib, outline, B.Pelvis, B.Spine, B.Chest);
        foreach (var x in new[] { -1f, 1f })
        {
            Add(Tube(new[]
                {
                    new Vector3(0.075f * x, 1.27f, z + 0.004f), new Vector3(0.088f * x, 1.37f, z - 0.01f), new Vector3(0.095f * x, 1.474f, 0.01f), new Vector3(0.09f * x, 1.41f, -0.128f),
                    new Vector3(0.075f * x, 1.27f, -0.142f), new Vector3(0.06f * x, 1.12f, -0.136f), new Vector3(0.055f * x, 1.03f, -0.135f)
                }, new[] { 0.016f, 0.016f, 0.016f, 0.016f, 0.016f, 0.016f, 0.016f }, Vector3.right, 8, false, false, new Vector2(0.45f, 1.9f)), bib, outline * 0.5f, TorsoBones);
            Add(Blob(new Vector3(0.075f * x, 1.27f, z + 0.012f), new Vector3(0.016f, 0.016f, 0.008f), 10, 6), "gold", 0.0018f, B.Chest);
        }
    }

    private void BowTie(string mat, float size, float z = 0.07f)
    {
        var at = new Vector3(0, 1.455f, z);
        foreach (var x in new[] { -1f, 1f })
            Add(Blob(at + new Vector3(0.032f * x * size, 0, 0), new Vector3(0.032f, 0.024f, 0.012f) * size, 12, 8), mat, 0.0025f, B.Chest, B.Neck);
        Add(Blob(at, new Vector3(0.012f, 0.014f, 0.012f) * size, 10, 6), mat, 0.002f, B.Chest, B.Neck);
    }

    /// <summary>A flared dress from the waist to below the knee, swaying with the legs.</summary>
    private void Skirt(string mat, float hem, float flare, float outline)
    {
        var centers = new[] { new Vector3(0, 1.02f, 0.006f), new Vector3(0, 0.92f, 0.004f), new Vector3(0, 0.8f, 0.01f), new Vector3(0, 0.66f, 0.014f), new Vector3(0, hem, 0.016f) };
        var radii = new[] { 0.165f, 0.18f, 0.2f + flare * 0.3f, 0.22f + flare * 0.7f, 0.23f + flare };
        var mb = Tube(centers, radii, Vector3.forward, 32, false, false, new Vector2(1f, 0.78f));
        _skin.Add(mb, mat, outline, v =>
        {
            // high up it's the hips; lower down it follows the nearer leg halfway
            var down = Mathf.InverseLerp(0.95f, hem, v.y);
            var thigh = v.x < 0 ? (int)B.LThigh : (int)B.RThigh;
            return SkinWeight.Blend((int)B.Pelvis, thigh, Mathf.Clamp01(down) * 0.55f * Mathf.Clamp01(Mathf.Abs(v.x) / 0.08f));
        });
    }

    private void Collarless(string mat, float r, float outline) =>
        Add(Tube(new[] { new Vector3(0, 1.44f, -0.022f), new Vector3(0, 1.52f, -0.022f) }, new[] { r, r * 0.92f }, Vector3.forward, 20, false, false, new Vector2(1f, 0.92f)), mat, outline, B.Chest, B.Neck);

    // =====================================================================================================
    // The ten new bodies
    // =====================================================================================================

    private void BuildStyled()
    {
        switch (Style)
        {
            case BodyStyle.Bendy:
                // Bendy: a slim black body, his white bow tie, thin black legs, big black cartoon shoes (the tail is extra)
                Torso("bendyBody", Scaled(SlimTorso, 0.9f), 0.0085f, 0.72f, 0.062f);
                Collarless("bendyBody", 0.06f, 0.006f);
                BowTie("bendyBow", 1.25f, 0.06f);
                Hips("bendyLegs", 0.15f, 0.0085f);
                Legs("bendyLegs", LegRadii(0.085f, 0.055f), 0.0085f);
                CartoonShoes("bendyShoe", "bendyShoe", 1.08f);
                break;
            case BodyStyle.Boris:
                // Boris: pale fur, blue-grey overalls with brass buttons, brown cartoon shoes
                Torso("borisFur", Scaled(ToonTorso, 0.95f), 0.0085f, 0.72f, 0.07f);
                Collarless("borisFur", 0.07f, 0.006f);
                Overalls("borisOveralls", 0.2f, 0.006f);
                Hips("borisOverallLegs", 0.18f, 0.0085f);
                Legs("borisOverallLegs", LegRadii(0.12f, 0.085f), 0.0085f);
                CartoonShoes("borisShoe", "belt", 1.05f);
                break;
            case BodyStyle.Alice:
                // Alice Angel: her black dress, the white bow at her chest, black stockings and little shoes
                Torso("aliceDress", Scaled(SlimTorso, 0.92f), 0.0075f, 0.7f, 0.06f);
                Collarless("aliceArmBody", 0.055f, 0.005f);
                BowTie("aliceBow", 1.6f, 0.085f);
                Skirt("aliceSkirt", 0.5f, 0.08f, 0.0075f);
                Legs("aliceLegs", LegRadii(0.07f, 0.045f), 0.006f);
                CartoonShoes("aliceShoe", "aliceShoe", 0.78f);
                break;
            case BodyStyle.InkDemon:
                // the Ink Demon: tall and dripping, a melted bow tie, legs that end in ink
                Torso("demonBody", Scaled(SlimTorso, 0.95f), 0.006f, 0.74f, 0.068f);
                Collarless("demonBody", 0.065f, 0.005f);
                BowTie("demonBow", 1.2f, 0.065f);
                Hips("demonLegs", 0.155f, 0.006f);
                Legs("demonLegs", LegRadii(0.09f, 0.07f), 0.006f);
                InkFeet("demonFeet");
                Strands("demonBody", 9, 0.03f);
                break;
            case BodyStyle.BeastBendy:
                // Beast Bendy: a vast chest, thick arms (the hands' side), heavy legs into puddles of ink
                Torso("demonBody", Scaled(ToonTorso, 1.28f), 0.006f, 0.8f, 0.12f);
                Collarless("demonBody", 0.12f, 0.005f);
                Hips("demonLegs", 0.23f, 0.006f);
                Legs("demonLegs", LegRadii(0.155f, 0.11f), 0.006f);
                InkFeet("demonFeet");
                Strands("demonBody", 16, 0.06f);
                break;
            case BodyStyle.Sammy:
                // Sammy: inky skin under ink-stained overalls, work boots
                Torso("sammySkinBody", Scaled(SlimTorso, 1f), 0.005f, 0.7f, 0.068f);
                Collarless("sammySkinBody", 0.068f, 0.004f);
                Overalls("sammyOveralls", 0.176f, 0.005f);
                Hips("sammyOverallLegs", 0.165f, 0.0055f);
                Legs("sammyOverallLegs", LegRadii(0.1f, 0.07f), 0.0055f);
                Boots("sammyBoot", "belt");
                Strands("sammySkinBody", 4, 0.02f);
                break;
            case BodyStyle.Projectionist:
                // the Projectionist: a hunched inky frame in a torn vest, dark trousers, heavy boots, the projector's cable
                Torso("normanSkinBody", Scaled(SlimTorso, 1.08f), 0.005f, 0.74f, 0.075f);
                Add(Tube(new[] { new Vector3(0, 0.96f, 0.004f), new Vector3(0, 1.1f, 0.012f), new Vector3(0, 1.25f, 0.014f), new Vector3(0, 1.37f, 0.004f) },
                    new[] { 0.175f, 0.172f, 0.19f, 0.2f }, Vector3.forward, 30, false, false, new Vector2(1f, 0.71f)), "normanVest", 0.0045f, TorsoBones);
                Collarless("normanSkinBody", 0.075f, 0.004f);
                Hips("normanTrousers", 0.17f, 0.0045f);
                Legs("normanTrousers", LegRadii(0.1f, 0.072f), 0.0045f);
                Boots("normanBoot", "belt", 1.1f);
                // the projector's power cable, from the back of the head down the spine
                Add(Tube(new[] { new Vector3(0, 1.52f, -0.08f), new Vector3(0.02f, 1.42f, -0.16f), new Vector3(0.03f, 1.25f, -0.17f), new Vector3(0.02f, 1.08f, -0.14f) },
                    new[] { 0.012f, 0.012f, 0.012f, 0.012f }, Vector3.right, 8, true, true), "belt", 0.002f, B.Spine, B.Chest, B.Neck);
                break;
            case BodyStyle.BruteBoris:
                // Brute Boris: a huge inky chest, torn overalls on one strap, massive legs
                Torso("bruteBody", Scaled(ToonTorso, 1.18f), 0.006f, 0.76f, 0.1f);
                Collarless("bruteBody", 0.1f, 0.005f);
                Hips("bruteOverallLegs", 0.21f, 0.006f);
                Legs("bruteOverallLegs", LegRadii(0.14f, 0.1f), 0.006f);
                Add(Tube(new[]
                    {
                        new Vector3(0.09f, 1.02f, 0.17f), new Vector3(0.1f, 1.2f, 0.18f), new Vector3(0.11f, 1.4f, 0.12f), new Vector3(0.12f, 1.49f, 0.0f), new Vector3(0.11f, 1.38f, -0.17f), new Vector3(0.08f, 1.04f, -0.17f)
                    }, new[] { 0.022f, 0.022f, 0.022f, 0.022f, 0.022f, 0.022f }, Vector3.right, 8, false, false, new Vector2(0.45f, 1.9f)), "bruteOverallLegs", 0.003f, TorsoBones);
                Add(Blob(new Vector3(0.09f, 1.03f, 0.18f), new Vector3(0.02f, 0.02f, 0.01f), 10, 6), "gold", 0.002f, B.Pelvis);
                Boots("bruteFeet", "bruteFeet", 1.25f);
                Strands("bruteBody", 4, 0.03f);
                break;
            case BodyStyle.Searcher:
                // a Searcher: lumps of goo for a body, legs that are columns of ink, a puddle for feet
                Torso("gooBody", Scaled(SlimTorso, 1f), 0.005f, 0.74f, 0.07f);
                var rng = new System.Random(77);
                for (var i = 0; i < 16; i++)
                {
                    var y = Mathf.Lerp(0.98f, 1.42f, (float)rng.NextDouble());
                    var a = (float)rng.NextDouble() * Mathf.PI * 2;
                    var r = Mathf.Lerp(0.15f, 0.17f, (float)rng.NextDouble());
                    var p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r * 0.7f);
                    var s = Mathf.Lerp(0.035f, 0.06f, (float)rng.NextDouble());
                    Add(Blob(p, new Vector3(s, s * 1.2f, s)), "gooBody", 0.003f, TorsoBones);
                }

                Collarless("gooBody", 0.07f, 0.004f);
                Hips("gooLegs", 0.16f, 0.005f);
                Legs("gooLegs", LegRadii(0.1f, 0.085f), 0.005f);
                InkFeet("gooFeet");
                Strands("gooBody", 12, 0.04f);
                break;
            case BodyStyle.Joey:
                // Joey Drew: shirt, waistcoat and the suit jacket over it, a bow tie, the studio's gold pin, a pocket square
                Torso("joeyShirt", Scaled(SlimTorso, 0.98f), 0.004f, 0.68f, 0.066f);
                Collar("joeyShirt", 0.074f);
                Add(Tube(new[] { new Vector3(0, 0.9f, 0.003f), new Vector3(0, 1.05f, 0.01f), new Vector3(0, 1.17f, 0.016f), new Vector3(0, 1.28f, 0.016f), new Vector3(0, 1.37f, 0.006f), new Vector3(0, 1.43f, -0.012f) },
                    new[] { 0.168f, 0.165f, 0.17f, 0.186f, 0.2f, 0.19f }, Vector3.forward, 32, false, false, new Vector2(1f, 0.69f)), "joeySuit", 0.0045f, TorsoBones);
                foreach (var x in new[] { -1f, 1f })
                {
                    // lapels
                    Add(Blob(new Vector3(0.05f * x, 1.36f, 0.122f), new Vector3(0.035f, 0.075f, 0.01f), 10, 8), "joeyLapel", 0.002f, B.Chest);
                    Add(Blob(new Vector3(0.18f * x, 1.405f, -0.02f), new Vector3(0.07f, 0.066f, 0.07f)), "joeySuit", 0.0035f, B.Chest);
                }

                BowTie("joeyTie", 0.9f, 0.075f);
                Add(Blob(new Vector3(-0.07f, 1.34f, 0.135f), new Vector3(0.012f, 0.012f, 0.006f), 10, 6), "gold", 0.0012f, B.Chest); // the studio's pin
                Add(Blob(new Vector3(0.1f, 1.33f, 0.128f), new Vector3(0.024f, 0.01f, 0.006f), 10, 6), "joeyShirtBits", 0.0012f, B.Chest); // pocket square
                Add(Tube(new[] { new Vector3(0.01f, 1.12f, 0.135f), new Vector3(0.05f, 1.1f, 0.132f), new Vector3(0.09f, 1.11f, 0.12f) }, new[] { 0.0045f, 0.0045f, 0.0045f }, Vector3.forward, 6, true, true), "gold", 0.0012f, B.Spine);
                Hips("joeySuitLegs", 0.158f, 0.0045f);
                Legs("joeySuitLegs", LegRadii(0.093f, 0.062f), 0.0045f);
                Boots("joeyShoe", "belt", 0.95f);
                break;
            case BodyStyle.Wally:
                // Wally: work shirt, dark overalls, boots, the janitor's ring of keys on his hip and a rag in the back pocket
                Torso("wallyShirt", Scaled(SlimTorso, 1.02f), 0.004f, 0.68f, 0.068f);
                Collar("wallyShirt", 0.075f);
                Overalls("wallyOveralls", 0.18f, 0.0045f);
                Hips("wallyOverallLegs", 0.165f, 0.0045f);
                Legs("wallyOverallLegs", LegRadii(0.097f, 0.066f), 0.0045f);
                Boots("wallyBoot", "belt", 1.05f);
                var ring = new MeshBuilder();
                ring.Torus(new Vector3(0.15f, 0.92f, 0.04f), Vector3.right, 0.03f, 0.003f, 18, 6);
                for (var i = 0; i < 4; i++) ring.Box(new Vector3(0.152f, 0.885f - i * 0.006f, 0.025f + i * 0.012f), new Vector3(0.004f, 0.045f, 0.012f), Quaternion.Euler(10 * i - 15, 0, 0));
                Add(ring, "wallyKeys", 0.0012f, B.Pelvis);
                Add(Blob(new Vector3(-0.1f, 0.9f, -0.14f), new Vector3(0.04f, 0.06f, 0.012f), 10, 8), "wallyRag", 0.0015f, B.Pelvis);
                break;
        }
    }

    /// <summary>Feet that are ink: a puddle-ish blob under each leg.</summary>
    private void InkFeet(string mat)
    {
        foreach (var s in new[] { -1f, 1f })
        {
            var a = Ankle(s);
            var foot = s < 0 ? B.LFoot : B.RFoot;
            Add(Blob(a + new Vector3(0, -0.03f, 0.05f), new Vector3(0.085f, 0.05f, 0.14f), 16, 8), mat, 0.004f, foot);
            Add(Blob(a + new Vector3(0, -0.075f, 0.06f), new Vector3(0.11f, 0.012f, 0.17f), 16, 6), mat, 0.003f, foot);
        }
    }

    /// <summary>Strands of ink hanging off the body (they swing with it).</summary>
    private void Strands(string mat, int count, float length)
    {
        var rng = new System.Random(count * 31 + mat.Length);
        for (var i = 0; i < count; i++)
        {
            var y = Mathf.Lerp(1.0f, 1.42f, (float)rng.NextDouble());
            var a = (float)rng.NextDouble() * Mathf.PI * 2;
            var p = new Vector3(Mathf.Cos(a) * 0.15f, y, Mathf.Sin(a) * 0.11f);
            var len = length * Mathf.Lerp(0.6f, 1.6f, (float)rng.NextDouble());
            Add(Tube(new[] { p, p + new Vector3(0, -len * 0.6f, 0.004f), p + new Vector3(0, -len, 0.002f) }, new[] { 0.007f, 0.005f, 0.004f }, Vector3.forward, 8, false, true), mat, 0.0015f, TorsoBones);
        }
    }

    private static InkRegion? StyledRegion(string material)
    {
        if (material.EndsWith("Shoe") || material.EndsWith("Boot") || material.EndsWith("Feet")) return InkRegion.Feet;
        if (material.EndsWith("Legs") || material.EndsWith("Skirt") || material.EndsWith("Trousers")) return InkRegion.Legs;
        return material switch
        {
            "bendyBody" or "bendyBow" or "borisFur" or "borisOveralls" or "aliceDress" or "aliceBow" or "aliceArmBody" or "demonBody" or "demonBow" or "sammySkinBody" or "sammyOveralls"
                or "normanSkinBody" or "normanVest" or "bruteBody" or "gooBody" or "joeyShirt" or "joeySuit" or "joeyLapel" or "joeyTie" or "wallyShirt" or "wallyOveralls" or "wallyRag" => InkRegion.Torso,
            _ => null
        };
    }

    // =====================================================================================================
    // Heads: for mirrors and your shadow, never for your own eyes
    // =====================================================================================================

    private Transform _headRoot;
    private readonly List<Renderer> _headRenderers = new();
    private readonly List<Renderer> _headOutlines = new();
    private Light _lensLight, _haloLight;
    private Transform _halo;

    /// <summary>A head part, in eye space: the eyes at the origin, looking along +z, up +y.</summary>
    private void HeadPart(MeshBuilder mb, Material material, float outline, string name = "HeadPart")
    {
        var go = new GameObject(name);
        go.transform.SetParent(_headRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("InkVR_Head_" + name);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        r.receiveShadows = false;
        _headRenderers.Add(r);
        if (outline <= 0) return;
        var o = new GameObject(name + "_Ink");
        o.transform.SetParent(go.transform, false);
        o.AddComponent<MeshFilter>().sharedMesh = mb.ToOutlineMesh("InkVR_Head_" + name + "_Ink", outline);
        var orr = o.AddComponent<MeshRenderer>();
        orr.sharedMaterial = InkMaterials.Outline;
        orr.shadowCastingMode = ShadowCastingMode.Off;
        orr.enabled = false;
        _headOutlines.Add(orr);
    }

    private static MeshBuilder E(Vector3 c, Vector3 r, Quaternion? q = null, int sides = 20, int stacks = 12)
    {
        var mb = new MeshBuilder();
        mb.Ellipsoid(c, r, q ?? Quaternion.identity, sides, stacks);
        return mb;
    }

    private static MeshBuilder Horn(float x, float scale, bool melted)
    {
        // up and out from the top of the head, curling at the tip
        var p0 = new Vector3(0.07f * x, 0.08f, -0.08f) * scale;
        var p1 = p0 + new Vector3(0.035f * x, 0.05f, 0.01f) * scale;
        var p2 = p1 + new Vector3(0.02f * x, 0.05f, 0.02f) * scale;
        var p3 = p2 + new Vector3(-0.012f * x, 0.035f, 0.025f) * scale;
        var mb = new MeshBuilder();
        mb.Tube(new[] { p0, p1, p2, p3 }, melted ? new[] { 0.03f * scale, 0.02f * scale, 0.012f * scale, 0.006f * scale } : new[] { 0.026f * scale, 0.017f * scale, 0.009f * scale, 0.002f * scale }, Vector3.forward, 10, true, true);
        return mb;
    }

    private System.Func<string, Material> _material;

    // ---- prizes from the Fun Hall: a party hat (on your head: you see it in a mirror, and in your shadow) and the
    // Ink Demon's bow tie (at your collar) ----
    private Transform _hat, _bow;
    private bool _hatOn, _bowOn;

    public void SetAccessories(bool hat, bool bow)
    {
        if (hat != _hatOn)
        {
            _hatOn = hat;
            if (hat && _hat == null) BuildHat();
            if (_hat != null) _hat.gameObject.SetActive(hat);
        }

        if (bow != _bowOn)
        {
            _bowOn = bow;
            if (bow && _bow == null) BuildBow();
            if (_bow != null) _bow.gameObject.SetActive(bow);
        }
    }

    private float HatHeight => Style switch
    {
        BodyStyle.Toon => 0.15f,
        BodyStyle.Bendy or BodyStyle.InkDemon => 0.14f,
        BodyStyle.BruteBoris => 0.14f,
        BodyStyle.Alice => 0.14f,
        BodyStyle.Projectionist => 0.21f,
        BodyStyle.Wally => 0.13f,
        BodyStyle.BeastBendy => 0.18f,
        _ => 0.13f
    };

    private void BuildHat()
    {
        var y = HatHeight;
        var mb = new MeshBuilder();
        mb.Tube(new[] { new Vector3(0, y, -0.08f), new Vector3(0.005f, y + 0.12f, -0.075f), new Vector3(0.01f, y + 0.22f, -0.07f) }, new[] { 0.075f, 0.04f, 0.004f }, Vector3.forward, 18, true, true);
        mb.Ellipsoid(new Vector3(0.01f, y + 0.23f, -0.07f), Vector3.one * 0.022f, Quaternion.identity, 10, 6);
        var before = _headRenderers.Count;
        HeadPart(mb, _material("partyHat"), 0.003f, "PartyHat");
        _hat = _headRenderers[before].transform;
        ShowHead(_headShown);
    }

    private void BuildBow()
    {
        // on the chest bone, at the collar: in the rest pose's frame, so it rides with the chest
        var chest = _bones[(int)B.Chest];
        var rest = Quaternion.LookRotation(NeckB - ChestP, Vector3.forward);
        var at = new Vector3(0, 1.45f, 0.085f + (Def.Id is BodyStyle.Toon or BodyStyle.Boris or BodyStyle.BruteBoris or BodyStyle.BeastBendy ? 0.04f : 0f));
        var mb = new MeshBuilder();
        foreach (var x in new[] { -1f, 1f }) mb.Ellipsoid(at + new Vector3(0.04f * x, 0, 0), new Vector3(0.04f, 0.03f, 0.014f), Quaternion.identity, 12, 8);
        mb.Ellipsoid(at, new Vector3(0.016f, 0.018f, 0.016f), Quaternion.identity, 10, 6);
        mb.Tube(new[] { at + new Vector3(0.02f, -0.02f, 0.005f), at + new Vector3(0.022f, -0.07f, 0.008f) }, new[] { 0.008f, 0.004f }, Vector3.forward, 6, false, true);
        var go = new GameObject("InkVR_BowTie");
        go.transform.SetParent(chest, false);
        go.transform.localPosition = Quaternion.Inverse(rest) * (Vector3.zero - ChestP);
        go.transform.localRotation = Quaternion.Inverse(rest);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("InkVR_BowTie");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = _material("demonBow");
        r.shadowCastingMode = ModConfig.PlayerShadows.Value ? ShadowCastingMode.On : ShadowCastingMode.Off;
        _renderers.Add(r);
        _casters.Add(r);
        var o = new GameObject("InkVR_BowTie_Ink");
        o.transform.SetParent(go.transform, false);
        o.AddComponent<MeshFilter>().sharedMesh = mb.ToOutlineMesh("InkVR_BowTie_Ink", 0.002f);
        var orr = o.AddComponent<MeshRenderer>();
        orr.sharedMaterial = InkMaterials.Outline;
        orr.shadowCastingMode = ShadowCastingMode.Off;
        _renderers.Add(orr);
        _bow = go.transform;
        SetLayer(go.layer);
    }

    private void BuildHead(System.Func<string, Material> material)
    {
        _material = material;
        _headRoot = new GameObject("InkVR_Head").transform;
        _headRoot.SetParent(_root, false);

        // the neck, from under the head down into the collar (the body's neck sits behind and below your eyes)
        var neck = new MeshBuilder();
        neck.Tube(new[] { new Vector3(0, -0.03f, -0.07f), new Vector3(0, -0.1f, -0.085f), new Vector3(0, -0.165f, -0.105f) }, new[] { 0.043f, 0.045f, 0.05f }, Vector3.forward, 16, false, false);
        HeadPart(neck, material(Style switch
        {
            BodyStyle.Bendy => "bendyBody",
            BodyStyle.InkDemon => "demonBody",
            BodyStyle.Boris => "borisFur",
            BodyStyle.BruteBoris => "bruteBody",
            BodyStyle.Alice => "aliceArmBody",
            BodyStyle.Sammy => "sammySkinBody",
            BodyStyle.Projectionist => "normanSkinBody",
            BodyStyle.Searcher => "gooBody",
            _ => "henrySkin"
        }), 0.003f, "Neck");

        switch (Style)
        {
            case BodyStyle.Lifelike:
            case BodyStyle.Toon:
            {
                var toon = Style == BodyStyle.Toon;
                var k = toon ? 1.15f : 1f;
                HeadPart(E(new Vector3(0, 0.0f, -0.07f) * k, new Vector3(0.082f, 0.105f, 0.098f) * k), material(toon ? "toonHenryFace" : "henryFace"), toon ? 0.006f : 0.003f, "Face");
                HeadPart(E(new Vector3(0, 0.045f, -0.085f) * k, new Vector3(0.088f, 0.075f, 0.1f) * k), material("henryHair"), toon ? 0.005f : 0.002f, "Hair");
                HeadPart(E(new Vector3(0, -0.015f, 0.025f) * k, new Vector3(0.016f, 0.022f, 0.018f) * k), material(toon ? "toonHenryFace" : "henryFace"), 0.0015f, "Nose");
                foreach (var x in new[] { -1f, 1f }) HeadPart(E(new Vector3(0.083f * x, -0.005f, -0.08f) * k, new Vector3(0.012f, 0.026f, 0.018f) * k), material(toon ? "toonHenryFace" : "henryFace"), 0.0015f, "Ear");
                break;
            }
            case BodyStyle.Bendy:
            case BodyStyle.InkDemon:
            {
                var demon = Style == BodyStyle.InkDemon;
                HeadPart(E(new Vector3(0, 0.01f, -0.085f), new Vector3(0.12f, 0.125f, 0.115f), null, 28, 16), material(demon ? "demonHead" : "bendyHead"), 0.006f, "Head");
                foreach (var x in new[] { -1f, 1f }) HeadPart(Horn(x, 1.15f, demon), material(demon ? "demonBody" : "bendyBody"), 0.004f, "Horn");
                if (demon)
                    for (var i = 0; i < 9; i++)
                    {
                        // round the jaw, below the grin, and down the back of the head
                        var a = i < 5 ? -0.9f + i * 0.45f : 2.2f + (i - 5) * 0.6f;
                        var top = new Vector3(Mathf.Sin(a) * 0.075f, -0.09f, -0.085f + Mathf.Cos(a) * 0.07f);
                        var len = 0.03f + (i * 7 % 5) * 0.012f;
                        var mb = new MeshBuilder();
                        mb.Tube(new[] { top + new Vector3(0, 0.02f, 0), top, top + new Vector3(0, -len, 0.003f) }, new[] { 0.012f, 0.008f, 0.0045f }, Vector3.forward, 8, false, true);
                        HeadPart(mb, material("demonBody"), 0.0015f, "Drip");
                    }

                break;
            }
            case BodyStyle.Boris:
            case BodyStyle.BruteBoris:
            {
                var brute = Style == BodyStyle.BruteBoris;
                var k = brute ? 1.15f : 1f;
                var fur = material(brute ? "bruteHead" : "borisHead");
                HeadPart(E(new Vector3(0, 0.0f, -0.08f) * k, new Vector3(0.105f, 0.115f, 0.1f) * k, null, 26, 14), fur, 0.006f, "Head");
                HeadPart(E(new Vector3(0, -0.035f, 0.03f) * k, new Vector3(0.06f, 0.05f, 0.075f) * k), material(brute ? "bruteBody" : "borisMuzzle"), 0.004f, "Muzzle");
                HeadPart(E(new Vector3(0, -0.015f, 0.1f) * k, new Vector3(0.026f, 0.02f, 0.02f) * k), material("bendyBody"), 0.002f, "Nose");
                foreach (var x in new[] { -1f, 1f })
                {
                    // long ears flopping down the sides
                    var mb = new MeshBuilder();
                    mb.Tube(new[] { new Vector3(0.07f * x, 0.08f, -0.08f) * k, new Vector3(0.11f * x, 0.04f, -0.07f) * k, new Vector3(0.13f * x, -0.04f, -0.06f) * k, new Vector3(0.125f * x, -0.1f, -0.065f) * k },
                        new[] { 0.03f * k, 0.034f * k, 0.03f * k, 0.018f * k }, Vector3.forward, 12, true, true, new Vector2(1f, 0.45f));
                    HeadPart(mb, material(brute ? "bruteBody" : "bendyBody"), 0.003f, "Ear");
                }

                break;
            }
            case BodyStyle.Alice:
            {
                HeadPart(E(new Vector3(0, 0.0f, -0.075f), new Vector3(0.1f, 0.115f, 0.1f), null, 26, 14), material("aliceHead"), 0.005f, "Face");
                HeadPart(E(new Vector3(0, 0.05f, -0.095f), new Vector3(0.108f, 0.09f, 0.1f)), material("aliceHair"), 0.004f, "Hair");
                // her bob, falling to the jaw at the sides, and the curl over her forehead
                foreach (var x in new[] { -1f, 1f }) HeadPart(E(new Vector3(0.078f * x, -0.01f, -0.085f), new Vector3(0.04f, 0.085f, 0.075f), Quaternion.Euler(0, 0, 8 * x)), material("aliceHair"), 0.003f, "Bob");
                HeadPart(E(new Vector3(0.025f, 0.085f, 0.0f), new Vector3(0.035f, 0.022f, 0.03f), Quaternion.Euler(0, 0, -20)), material("aliceHair"), 0.003f, "Curl");
                // the halo: a gold ring floating over her head, glowing
                var halo = new MeshBuilder();
                halo.Torus(Vector3.zero, Vector3.up, 0.085f, 0.009f, 32, 8);
                HeadPart(halo, material("halo"), 0.002f, "Halo");
                _halo = _headRenderers[_headRenderers.Count - 1].transform;
                _halo.localPosition = new Vector3(0, 0.2f, -0.08f);
                _haloLight = _halo.gameObject.AddComponent<Light>();
                _haloLight.type = LightType.Point;
                _haloLight.color = new Color(1f, 0.85f, 0.5f);
                _haloLight.range = 1.2f;
                _haloLight.intensity = 0.5f;
                _haloLight.shadows = LightShadows.None;
                break;
            }
            case BodyStyle.BeastBendy:
            {
                // a great head, the grin wide, horns sweeping up and out, ink running off it
                HeadPart(E(new Vector3(0, 0.02f, -0.1f), new Vector3(0.165f, 0.16f, 0.15f), null, 28, 16), material("demonHead"), 0.007f, "Head");
                foreach (var x in new[] { -1f, 1f }) HeadPart(Horn(x, 1.75f, true), material("demonBody"), 0.005f, "Horn");
                for (var i = 0; i < 12; i++)
                {
                    var a = -1.4f + i * 0.25f;
                    var top = new Vector3(Mathf.Sin(a) * 0.12f, -0.1f, -0.1f + Mathf.Cos(a) * 0.1f);
                    var mb = new MeshBuilder();
                    mb.Tube(new[] { top + new Vector3(0, 0.03f, 0), top, top + new Vector3(0, -0.05f - (i * 7 % 5) * 0.015f, 0.003f) }, new[] { 0.016f, 0.01f, 0.005f }, Vector3.forward, 8, false, true);
                    HeadPart(mb, material("demonBody"), 0.0015f, "Drip");
                }

                break;
            }
            case BodyStyle.Sammy:
            {
                HeadPart(E(new Vector3(0, 0.0f, -0.08f), new Vector3(0.09f, 0.11f, 0.1f)), material("sammySkinBody"), 0.003f, "Head");
                // the cut-out mask, a flat card in front of the face, tied on with rope
                var mask = new MeshBuilder();
                mask.Ellipsoid(new Vector3(0, 0.005f, 0.035f), new Vector3(0.11f, 0.12f, 0.012f), Quaternion.Euler(0, 180, 0), 24, 10);
                HeadPart(mask, material("sammyMask"), 0.004f, "Mask");
                var rope = new MeshBuilder();
                rope.Torus(new Vector3(0, 0.02f, -0.05f), Vector3.up, 0.098f, 0.006f, 28, 6);
                HeadPart(rope, material("rope"), 0.0015f, "Rope");
                break;
            }
            case BodyStyle.Projectionist:
            {
                // a projector for a head: the body, two reels on top, the lens in front (and its light)
                var body = new MeshBuilder();
                body.Box(new Vector3(0, -0.01f, -0.06f), new Vector3(0.17f, 0.15f, 0.2f), Quaternion.identity);
                HeadPart(body, material("projector"), 0.004f, "Projector");
                foreach (var z in new[] { -0.12f, 0.0f })
                {
                    var reel = new MeshBuilder();
                    reel.Tube(new[] { new Vector3(-0.012f, 0.13f, z), new Vector3(0.012f, 0.13f, z) }, new[] { 0.07f, 0.07f }, Vector3.up, 24, true, true);
                    HeadPart(reel, material("projector"), 0.003f, "Reel");
                }

                var lens = new MeshBuilder();
                lens.Tube(new[] { new Vector3(0, -0.01f, 0.04f), new Vector3(0, -0.01f, 0.09f) }, new[] { 0.035f, 0.032f }, Vector3.up, 20, false, false);
                HeadPart(lens, material("projector"), 0.003f, "LensTube");
                var glass = new MeshBuilder();
                glass.Ellipsoid(new Vector3(0, -0.01f, 0.09f), new Vector3(0.03f, 0.03f, 0.008f), Quaternion.identity, 16, 8);
                HeadPart(glass, material("lens"), 0f, "Lens");
                var lightGo = new GameObject("InkVR_ProjectorLight");
                lightGo.transform.SetParent(_headRoot, false);
                lightGo.transform.localPosition = new Vector3(0, -0.01f, 0.1f);
                _lensLight = lightGo.AddComponent<Light>();
                _lensLight.type = LightType.Spot;
                _lensLight.spotAngle = 38f;
                _lensLight.range = 6f;
                _lensLight.intensity = 1.1f;
                _lensLight.color = new Color(1f, 0.93f, 0.78f);
                _lensLight.shadows = LightShadows.None;
                break;
            }
            case BodyStyle.Searcher:
            {
                HeadPart(E(new Vector3(0, -0.005f, -0.075f), new Vector3(0.1f, 0.12f, 0.1f)), material("searcherHead"), 0.004f, "Head");
                var rng = new System.Random(5);
                for (var i = 0; i < 7; i++)
                {
                    var a = (float)rng.NextDouble() * Mathf.PI * 2;
                    var p = new Vector3(Mathf.Cos(a) * 0.08f, 0.04f + (float)rng.NextDouble() * 0.06f, -0.08f + Mathf.Sin(a) * 0.07f);
                    var s = 0.03f + (float)rng.NextDouble() * 0.03f;
                    HeadPart(E(p, new Vector3(s, s, s)), material("gooBody"), 0.002f, "Lump");
                }

                break;
            }
            case BodyStyle.Joey:
            case BodyStyle.Wally:
            {
                var wally = Style == BodyStyle.Wally;
                var face = material(wally ? "wallyFace" : "joeyFace");
                HeadPart(E(new Vector3(0, 0.0f, -0.07f), new Vector3(0.082f, 0.105f, 0.098f)), face, 0.003f, "Face");
                HeadPart(E(new Vector3(0, -0.015f, 0.026f), new Vector3(0.017f, 0.023f, 0.019f)), face, 0.0015f, "Nose");
                foreach (var x in new[] { -1f, 1f }) HeadPart(E(new Vector3(0.083f * x, -0.005f, -0.08f), new Vector3(0.012f, 0.026f, 0.018f)), face, 0.0015f, "Ear");
                if (!wally)
                {
                    HeadPart(E(new Vector3(0, 0.05f, -0.085f), new Vector3(0.087f, 0.07f, 0.098f)), material("joeyHair"), 0.002f, "Hair");
                    foreach (var x in new[] { -1f, 1f }) HeadPart(E(new Vector3(0.018f * x, -0.042f, 0.022f), new Vector3(0.022f, 0.009f, 0.01f), Quaternion.Euler(0, 0, 12 * x)), material("joeyHair"), 0.0012f, "Moustache");
                }
                else
                {
                    // the cap: a crown and a peak
                    HeadPart(E(new Vector3(0, 0.055f, -0.08f), new Vector3(0.092f, 0.065f, 0.1f)), material("wallyCap"), 0.003f, "Cap");
                    HeadPart(E(new Vector3(0, 0.035f, 0.035f), new Vector3(0.075f, 0.008f, 0.06f)), material("wallyCap"), 0.002f, "Peak");
                }

                break;
            }
        }
    }

    /// <summary>
    /// The head shows (a mirror is about to render): drawn and outlined. Otherwise it's only in your shadow (with YOUR
    /// SHADOW on). Lying down, the body steps aside and so does the head.
    /// </summary>
    public void ShowHead(bool on)
    {
        _headShown = on;
        var drawn = on && !_low;
        var shadow = _headShadowOn && !_low;
        foreach (var r in _headRenderers)
        {
            if (r == null) continue;
            var mode = drawn ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            if (r.shadowCastingMode != mode) r.shadowCastingMode = mode;
            if (r.enabled != (drawn || shadow)) r.enabled = drawn || shadow;
        }

        foreach (var r in _headOutlines)
            if (r != null && r.enabled != drawn)
                r.enabled = drawn;
    }

    private bool _headShadowOn = true, _headShown, _low;

    /// <summary>Where the head is (the eyes), for mirrors.</summary>
    public Transform HeadRoot => _headRoot;

    // =====================================================================================================
    // Extras: the tail, the drips, the halo and the projector
    // =====================================================================================================

    private Transform _tailPivot;
    private Quaternion _tailRest;
    private Vector2 _tailSway, _tailVel;
    private float _lastYaw;

    private void BuildTail(System.Func<string, Material> material)
    {
        string mat;
        MeshBuilder mb;
        switch (Style)
        {
            case BodyStyle.Bendy:
            {
                // thin and long, curving down and out, a spade at the end
                mb = new MeshBuilder();
                var pts = new[] { new Vector3(0, 0, 0), new Vector3(0, -0.06f, -0.06f), new Vector3(0, -0.12f, -0.14f), new Vector3(0, -0.13f, -0.24f), new Vector3(0, -0.08f, -0.32f) };
                mb.Tube(pts, new[] { 0.013f, 0.011f, 0.01f, 0.009f, 0.008f }, Vector3.right, 10, true, false);
                mb.Ellipsoid(new Vector3(0, -0.06f, -0.35f), new Vector3(0.006f, 0.035f, 0.04f), Quaternion.Euler(30, 0, 0), 12, 8);
                mat = "bendyBody";
                break;
            }
            case BodyStyle.Boris:
            case BodyStyle.BruteBoris:
            {
                mb = new MeshBuilder();
                var k = Style == BodyStyle.BruteBoris ? 1.3f : 1f;
                mb.Tube(new[] { new Vector3(0, 0, 0), new Vector3(0, -0.05f, -0.06f) * k, new Vector3(0, -0.12f, -0.1f) * k, new Vector3(0, -0.2f, -0.11f) * k },
                    new[] { 0.025f * k, 0.045f * k, 0.05f * k, 0.03f * k }, Vector3.right, 14, true, true);
                mat = Style == BodyStyle.BruteBoris ? "bruteBody" : "borisFur";
                break;
            }
            default:
                return;
        }

        var pivot = new GameObject("InkVR_Tail").transform;
        pivot.SetParent(_root, false);
        pivot.localPosition = new Vector3(0, 0.93f, -0.13f);
        var go = new GameObject("Tail");
        go.transform.SetParent(pivot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("InkVR_Tail");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material(mat);
        r.shadowCastingMode = ShadowCastingMode.On;
        _renderers.Add(r);
        _casters.Add(r);
        var o = new GameObject("Tail_Ink");
        o.transform.SetParent(go.transform, false);
        o.AddComponent<MeshFilter>().sharedMesh = mb.ToOutlineMesh("InkVR_Tail_Ink", 0.004f);
        var orr = o.AddComponent<MeshRenderer>();
        orr.sharedMaterial = InkMaterials.Outline;
        orr.shadowCastingMode = ShadowCastingMode.Off;
        _renderers.Add(orr);
        // it rides on the pelvis, swinging on a spring
        pivot.SetParent(_bones[(int)B.Pelvis], true);
        _tailRest = pivot.localRotation;
        _tailPivot = pivot;
    }

    /// <summary>Every pose: the tail swings with your turns and steps, the halo bobs, the projector's light flickers like film.</summary>
    private void Animate(float dt, float yaw, Vector3 velocity, float k)
    {
        if (dt <= 0) return;
        if (_tailPivot != null)
        {
            // a damped spring, pushed by turning and by moving
            var turn = Mathf.DeltaAngle(_lastYaw, yaw) / Mathf.Max(dt, 1e-3f);
            var push = new Vector2(Mathf.Clamp(-turn * 0.15f, -40f, 40f), Mathf.Clamp(velocity.magnitude / Mathf.Max(k, 1e-3f) * 12f, 0f, 30f));
            var idle = new Vector2(Mathf.Sin(Time.unscaledTime * 1.7f) * 8f, Mathf.Sin(Time.unscaledTime * 1.1f) * 4f);
            var target = push + idle;
            _tailVel += ((target - _tailSway) * 60f - _tailVel * 9f) * dt;
            _tailSway += _tailVel * dt;
            _tailPivot.localRotation = _tailRest * Quaternion.Euler(_tailSway.y, _tailSway.x, 0);
        }

        _lastYaw = yaw;
        if (_halo != null) _halo.localPosition = new Vector3(0, 0.2f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.008f, -0.08f);
        if (_lensLight != null) _lensLight.intensity = 1.0f + 0.15f * Mathf.PerlinNoise(Time.unscaledTime * 12f, 0.3f);
    }

    /// <summary>The projector's and the halo's lights: on in the game, and in the theatre only for Henry's own hands.</summary>
    public void SetLightsOn(bool on, float k)
    {
        if (_lensLight != null)
        {
            _lensLight.enabled = on;
            _lensLight.range = 6f * k;
        }

        if (_haloLight != null)
        {
            _haloLight.enabled = on;
            _haloLight.range = 1.2f * k;
        }
    }
}

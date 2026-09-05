using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Sonic
{
    /// <summary>
    /// Color palette for the Sonic Topography effect (ported from the original
    /// "音域回响" wallpaper). Stored as linear float3 triplets exactly like the
    /// original so the shaders can consume them without conversion.
    /// </summary>
    public readonly struct SonicPalette
    {
        public readonly string Id;
        public readonly string Name;
        public readonly Vector3 BaseColor1;
        public readonly Vector3 BaseColor2;
        public readonly Vector3 CoolCore;
        public readonly Vector3 CoolEdge;
        public readonly Vector3 WarmCore;
        public readonly Vector3 WarmEdge;
        public readonly Vector3 RippleColor;
        public readonly Vector3 PeakColor;
        public readonly float GlowIntensity;

        public SonicPalette(
            string id, string name,
            float b1r, float b1g, float b1b,
            float b2r, float b2g, float b2b,
            float ccr, float ccg, float ccb,
            float cer, float ceg, float ceb,
            float wcr, float wcg, float wcb,
            float wer, float weg, float web,
            float rr, float rg, float rb,
            float pr, float pg, float pb,
            float glow)
        {
            Id = id;
            Name = name;
            BaseColor1 = new Vector3(b1r, b1g, b1b);
            BaseColor2 = new Vector3(b2r, b2g, b2b);
            CoolCore = new Vector3(ccr, ccg, ccb);
            CoolEdge = new Vector3(cer, ceg, ceb);
            WarmCore = new Vector3(wcr, wcg, wcb);
            WarmEdge = new Vector3(wer, weg, web);
            RippleColor = new Vector3(rr, rg, rb);
            PeakColor = new Vector3(pr, pg, pb);
            GlowIntensity = glow;
        }
    }

    public static class SonicThemes
    {
        public static readonly SonicPalette Nocturnal = new("nocturnal", "霁紫",
            .005f, .008f, .025f, .015f, .025f, .07f, .35f, .1f, .9f, .15f, 0f, .45f, .65f, .25f, 1f, .5f, .1f, .8f, .5f, .2f, 1f, 1f, .55f, .05f, 1f);
        public static readonly SonicPalette OceanDeep = new("ocean-deep", "沧蓝",
            .002f, .008f, .028f, .005f, .018f, .06f, 0f, .25f, 1f, 0f, .08f, .35f, .15f, .55f, 1f, .05f, .35f, .85f, .1f, .5f, 1f, 1f, .75f, .1f, 1.1f);
        public static readonly SonicPalette ArcticAurora = new("arctic-aurora", "冰蓝",
            .003f, .015f, .022f, .01f, .03f, .055f, 0f, .75f, .85f, 0f, .3f, .5f, .2f, 1f, .85f, .05f, .6f, .6f, .1f, .9f, .9f, 1f, .25f, .35f, 1.25f);
        public static readonly SonicPalette CyberForest = new("cyber-forest", "碧翠",
            .003f, .018f, .005f, .01f, .045f, .018f, 0f, .85f, .35f, 0f, .35f, .15f, .4f, 1f, .3f, .15f, .65f, .2f, .3f, 1f, .4f, 1f, .2f, .5f, 1.3f);
        public static readonly SonicPalette GoldenHour = new("golden-hour", "流金",
            .018f, .015f, .005f, .045f, .035f, .012f, .85f, .6f, .05f, .5f, .3f, .02f, 1f, .92f, .35f, .85f, .7f, .15f, 1f, .85f, .25f, .2f, .5f, 1f, 1.2f);
        public static readonly SonicPalette EmberFire = new("ember-fire", "余烬",
            .022f, .008f, .002f, .05f, .018f, .005f, 1f, .45f, 0f, .6f, .15f, 0f, 1f, .78f, .15f, .9f, .55f, .05f, 1f, .65f, .1f, .1f, .4f, 1f, 1.5f);
        public static readonly SonicPalette CrimsonSunset = new("crimson-sunset", "赤焰",
            .025f, .003f, .005f, .055f, .01f, .015f, 1f, .05f, .08f, .65f, 0f, .06f, 1f, .35f, .2f, .85f, .12f, .1f, 1f, .15f, .1f, .1f, .9f, .7f, 1.4f);
        public static readonly SonicPalette CoralMirage = new("coral-mirage", "霞粉",
            .02f, .006f, .01f, .045f, .015f, .022f, 1f, .25f, .3f, .7f, .08f, .18f, 1f, .55f, .55f, .9f, .3f, .35f, 1f, .4f, .4f, .1f, .7f, 1f, 1.3f);
        public static readonly SonicPalette NeonTokyo = new("neon-tokyo", "幻紫",
            .01f, .002f, .025f, .03f, .008f, .065f, 1f, .05f, .6f, .55f, .02f, .85f, 1f, .25f, .85f, .8f, .1f, .7f, 1f, .2f, .75f, .95f, 1f, .15f, 1.6f);
        public static readonly SonicPalette MinimalMonochrome = new("minimal-monochrome", "水墨",
            .012f, .012f, .012f, .045f, .045f, .045f, .8f, .8f, .8f, .3f, .3f, .3f, 1f, 1f, 1f, .6f, .6f, .6f, 1f, 1f, 1f, 1f, 1f, 1f, .7f);
        public static readonly SonicPalette TealDepth = new("teal-depth", "幽青",
            .002f, .018f, .02f, .008f, .04f, .045f, 0f, .55f, .55f, 0f, .25f, .28f, .2f, .85f, .75f, .08f, .55f, .5f, .15f, .8f, .7f, 1f, .45f, .15f, 1.2f);
        public static readonly SonicPalette LavenderDream = new("lavender-dream", "薰衣草",
            .012f, .008f, .022f, .03f, .02f, .055f, .55f, .35f, .85f, .3f, .15f, .55f, .75f, .55f, 1f, .5f, .3f, .75f, .65f, .45f, 1f, 1f, .8f, .25f, 1.1f);
        public static readonly SonicPalette CherryBlossom = new("cherry-blossom", "樱",
            .018f, .005f, .012f, .04f, .012f, .025f, 1f, .55f, .65f, .7f, .2f, .35f, 1f, .72f, .78f, .85f, .45f, .55f, 1f, .6f, .7f, .25f, .9f, .55f, 1.15f);
        public static readonly SonicPalette CopperForge = new("copper-forge", "锻铜",
            .02f, .01f, .005f, .045f, .025f, .012f, .85f, .45f, .2f, .5f, .22f, .08f, 1f, .65f, .3f, .75f, .38f, .15f, .9f, .55f, .25f, .3f, .65f, .35f, 1.3f);
        public static readonly SonicPalette MintFresh = new("mint-fresh", "薄荷",
            .003f, .02f, .015f, .01f, .045f, .035f, .3f, .9f, .65f, .1f, .45f, .3f, .5f, 1f, .8f, .25f, .7f, .5f, .4f, 1f, .7f, 1f, .3f, .55f, 1.2f);

        public static readonly SonicPalette[] All =
        [
            Nocturnal, OceanDeep, ArcticAurora, CyberForest, GoldenHour, EmberFire,
            CrimsonSunset, CoralMirage, NeonTokyo, MinimalMonochrome, TealDepth,
            LavenderDream, CherryBlossom, CopperForge, MintFresh,
        ];

        public static SonicPalette Resolve(string? id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.Ordinal)) return All[i];
            }
            return Nocturnal;
        }
    }
}

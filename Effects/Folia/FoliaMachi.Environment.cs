using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaMachi
{
    internal readonly record struct WeatherRoof(float X0, float Z0, float X1, float Z1, float Y);
    internal readonly record struct WeatherDrip(float X, float Z, float From, float To, float Period, float Phase);
    internal readonly record struct WeatherGlint(Vector3 Position, float Radius);
    internal sealed record WeatherLayout(float Half, float GroundY, float WindYaw, float WindSlant,
        WeatherRoof[] Roofs, WeatherDrip[] Drips, WeatherGlint[] Glints);

    internal readonly record struct SteamEmitter(Vector3 Position, float Height, float BaseRadius, float TipRadius,
        float Extinction, float WeatherResponse, Vector2 Drift, float Turbulence, float SunVisibility,
        Vector3 LightPosition, Vector3 LightRadiance);

    private static readonly SteamEmitter[] KonbiniSteam =
    [
        // The louver sits beneath the return hood. Exhaust escapes toward its cyan edge.
        new(new(.98f, 2.16f, -4.32f), .24f, .12f, .22f, 2.1f, .48f,
            new(.46f, .04f), .045f, .12f, new(1.33f, 2.435f, -4.32f), new(.018f, .13f, .20f)),
        new(new(-2.4f, .02f, 4.15f), 1.18f, .13f, .40f, 2.55f, .46f,
            new(.04f, .28f), .14f, 1, new(-1.9f, 1.5f, 1.3f), new(.36f, .24f, .14f))
    ];

    private static readonly SteamEmitter[] StationSteam =
    [
        new(new(-.65f, 3.10f, -3.78f), .97f, .11f, .32f, 2.1f, .48f,
            new(-.09f, .22f), .10f, 1, new(-.7f, 2.4f, .9f), new(.30f, .26f, .16f)),
        new(new(-2.60f, .02f, 4.575f), 1.12f, .12f, .36f, 2.4f, .46f,
            new(-.07f, .30f), .13f, 1, new(-.7f, 2.4f, .9f), new(.25f, .21f, .14f))
    ];

    private static readonly SteamEmitter[] KowloonSteam =
    [
        // The sloped tarp reaches y=1.97 near its front hem. Steam stays below it
        // and drifts from the wok at z=1.875 toward the open front of the stall.
        new(new(4.60f, 1.34f, 1.875f), .60f, .14f, .34f, 2.8f, .1f,
            new(-.025f, 1.15f), .075f, .06f, new(5.175f, 1.67f, 2.60f), new(.56f, .28f, .115f)),
        new(new(1.60f, .04f, 4.10f), 1.04f, .12f, .36f, 2.4f, .46f,
            new(.03f, .28f), .13f, 1, new(2.10f, 1.90f, -.50f), new(.32f, .13f, .26f))
    ];

    private const float Dusk = .45f;
    internal const double CycleDurationSeconds = 240;
    private static readonly Vector3 NightDirection = Vector3.Normalize(new(-7, 13, 8));
    private static readonly Vector3 DuskDirection = Vector3.Normalize(new(-11, 3, 6));
    private static readonly Vector3 DayDirection = Vector3.Normalize(new(-6, 16, 8));
    private static readonly Vector3 NightSky = Color(0x0e1524), DaySky = Color(0x4d8bd4);
    private static readonly Vector3 DawnSky = Color(0x6d639c), DuskSky = Color(0x6a4986);
    private static readonly Vector3 NightHorizon = Color(0x26324a), DayHorizon = Color(0xcde4f2);
    private static readonly Vector3 DawnHorizon = Color(0xffb47d), DuskHorizon = Color(0xff8657);
    private static readonly Vector3 MoonColor = Color(0x9cb3dc), DaySun = Color(0xfff0da);
    private static readonly Vector3 DawnSun = Color(0xffad57), DuskSun = Color(0xff9349);
    private static readonly Vector3 NightHemiSky = Color(0x8392ac), DayHemiSky = Color(0xbcd6ee);
    private static readonly Vector3 DawnHemiSky = Color(0xc2969e), DuskHemiSky = Color(0xba828b);
    private static readonly Vector3 NightHemiGround = Color(0x434350), DayHemiGround = Color(0x5f5b52);
    private static readonly Vector3 TwilightHemiGround = Color(0x6b514b);
    private static readonly Vector3 KonbiniNightSky = Color(0x121a36), KonbiniNightHorizon = Color(0x29345c);
    private static readonly Vector3 StationNightSky = Color(0x18182e), StationNightHorizon = Color(0x443458);
    private static readonly Vector3 KowloonNightSky = Color(0x1a1734), KowloonNightHorizon = Color(0x423160);
    private static readonly Vector3 KonbiniGlow = Color(0x3d5785), StationGlow = Color(0x5d467e),
        KowloonGlow = Color(0x794d9f);

    internal record struct EnvironmentState
    {
        private string? _puddleScene;
        internal (string? Title, string? Artist) WeatherSong;
        internal string? AutomaticWeather;
        internal string? TimeOfDay;
        internal float Daylight, Rain, Snow, Fog, CloudCover, Wetness, PuddleFill, SnowCover;
        internal Vector3 PuddlePattern { get; private set; }

        internal void SetPuddleScene(string sceneId)
        {
            if (_puddleScene == sceneId) return;
            // Offset the world-space noise and vary the common waterline slightly.
            Random random = Random.Shared;
            PuddlePattern = new(192 * random.NextSingle() - 96, 192 * random.NextSingle() - 96, random.NextSingle());
            _puddleScene = sceneId;
        }
    }

    internal readonly record struct EnvironmentFrame(Vector3 SkyTop, Vector3 SkyBottom, Vector3 SunDirection, Vector3 SunRadiance,
        float Artificial, float Wetness, float PuddleFill, float SnowCover, float Rain, float Snow, float Mist, float Haze, float Daylight,
        Vector3 HemiSky, Vector3 HemiGround, Vector3 CityGlow);

    internal static EnvironmentFrame UpdateEnvironment(ref EnvironmentState state, string timeOfDay, string weather,
        FoliaSong song, DateTime now, float delta, bool reset, string? sceneId = null, double cycleSeconds = 0)
    {
        bool freshGround = state.TimeOfDay == null;
        var weatherSong = (song.Title, song.Artist);
        if (state.WeatherSong != weatherSong)
        {
            state.WeatherSong = weatherSong;
            double roll = Hash01(Hash("machi-weather:", song.Title, song.Artist), 0, 0x3d);
            state.AutomaticWeather = roll < .35 ? "rain" : roll < .65 ? "clear" : roll < .85 ? "fog" : "snow";
        }
        bool startCycle = timeOfDay == "cycle" && state.TimeOfDay != timeOfDay;
        state.TimeOfDay = timeOfDay;
        Vector3 solarAxis;
        float daylight;
        bool dawn = false;
        switch (timeOfDay)
        {
            case "day":
                solarAxis = DayDirection;
                daylight = 1;
                break;
            case "dusk":
                solarAxis = DuskDirection;
                daylight = Dusk;
                break;
            case "night":
                solarAxis = -NightDirection;
                daylight = 0;
                break;
            default:
                // Phase zero is sunrise. Begin the cycle in the evening half of the night.
                float phase = timeOfDay == "cycle"
                    ? (float)((cycleSeconds % CycleDurationSeconds) * (Math.Tau / CycleDurationSeconds) + 5 * Math.PI / 4)
                    : LocalSolarPhase((float)now.TimeOfDay.TotalHours);
                (float height, float east) = MathF.SinCos(phase);
                solarAxis = new Vector3(.8f, 0, .6f) * east + new Vector3(-.3f, .8660254f, .4f) * height;
                daylight = Smooth(-.14f, .55f, solarAxis.Y);
                dawn = east > 0;
                break;
        }
        string? kind = weather == "auto" ? state.AutomaticWeather : weather;
        float rain = kind == "rain" ? 1 : 0;
        float snow = kind == "snow" ? 1 : 0;
        float fog = kind == "fog" ? 1 : 0;
        float clouds = rain + snow + fog;
        if (reset)
        {
            state.Rain = rain;
            state.Snow = snow;
            state.Fog = fog;
            state.CloudCover = clouds;
            state.SnowCover = snow;
        }
        else if (delta > 0)
        {
            state.Rain = Approach(state.Rain, rain, 1.4f, delta);
            state.Snow = Approach(state.Snow, snow, 1.4f, delta);
            state.Fog = Approach(state.Fog, fog, 2.2f, delta);
            state.CloudCover = Approach(state.CloudCover, clouds, 1.4f, delta);
            state.SnowCover = Approach(state.SnowCover, state.Snow, state.Snow > state.SnowCover ? 8 : 12, delta);
        }
        if (reset || startCycle)
            state.Daylight = daylight;
        else if (delta > 0)
            state.Daylight = Approach(state.Daylight, daylight, 1.6f, delta);
        // A playback seek resets temporal rendering history, but does not drain the street.
        float wet = Math.Max(state.Rain, state.Fog * .35f);
        if (freshGround)
        {
            state.Wetness = wet;
            state.PuddleFill = .18f * state.Rain;
        }
        else if (delta > 0)
        {
            state.Wetness = Approach(state.Wetness, wet, wet > state.Wetness ? 3 : 20, delta);
            state.PuddleFill = Approach(state.PuddleFill, state.Rain, state.Rain > state.PuddleFill ? 14 : 35, delta);
        }
        float d = state.Daylight;
        float overcast = state.CloudCover;
        float night = 1 - Smooth(0, Dusk, d);
        float clear = 1 - overcast;
        float elevation = solarAxis.Y;
        // Both disks vanish at the horizon. The solar axis and its opposite
        // follow continuous orbits, including while their disks are hidden.
        float moon = .035f * night * clear * Smooth(0, .10f, -elevation);
        float sun = (1.9f * Smooth(0, Dusk, d) + 3.2f * Smooth(Dusk, 1, d)) * (1 - .80f * overcast)
            * Smooth(0, .10f, elevation);
        float twilight = Smooth(-.20f, .06f, elevation) * (1 - Smooth(.24f, .60f, elevation));
        float warmSun = 1 - Smooth(.15f, .62f, elevation);
        Vector3 sunColor = Overcast(Vector3.Lerp(DaySun, dawn ? DawnSun : DuskSun, warmSun), overcast * .45f, 1);
        float mist = .16f * (1 - .4f * d) + .1f * state.Rain + .45f * state.Fog + .06f * state.Snow;
        float haze = .42f * state.Fog + .08f * state.Snow;
        float grayGain = .9f + .35f * d;
        // Three's hemisphere and ambient lights supply irradiance. A constant
        // environment emits radiance E / pi to deliver that same irradiance.
        float hemisphere = ((2.1f + .35f * d + .25f * overcast * d) * (1 - night)
            + (.10f + .24f * clear) * night) / MathF.PI;
        Vector3 hemiSky = Vector3.Lerp(Vector3.Lerp(NightHemiSky, DayHemiSky, d), dawn ? DawnHemiSky : DuskHemiSky, .8f * twilight);
        hemiSky = Overcast(hemiSky, overcast * .7f, grayGain);
        Vector3 hemiGround = Vector3.Lerp(Vector3.Lerp(NightHemiGround, DayHemiGround, d), TwilightHemiGround, twilight);
        hemiGround = Overcast(hemiGround, overcast * .7f, grayGain);
        hemiGround = Vector3.Lerp(hemiGround, new Vector3(.55f, .60f, .70f) * (.35f + .65f * d), state.SnowCover * .5f);
        var (nightSky, nightHorizon, glow) = sceneId switch
        {
            "konbini" => (KonbiniNightSky, KonbiniNightHorizon, KonbiniGlow * .14f),
            "station" => (StationNightSky, StationNightHorizon, StationGlow * .13f),
            "kowloon" => (KowloonNightSky, KowloonNightHorizon, KowloonGlow * .22f),
            _ => (NightSky, NightHorizon, Vector3.Zero)
        };
        Vector3 cityGlow = glow * ((1 - .75f * d) * (1 - .85f * d) / MathF.PI);
        float skyGain = 1 - night * (.55f + .24f * overcast);
        Vector3 sky = Vector3.Lerp(Vector3.Lerp(nightSky, DaySky, d), dawn ? DawnSky : DuskSky, .9f * twilight);
        Vector3 horizon = Vector3.Lerp(Vector3.Lerp(nightHorizon, DayHorizon, d), dawn ? DawnHorizon : DuskHorizon, twilight);
        return new EnvironmentFrame(
            Overcast(sky, overcast * .7f, grayGain) * skyGain,
            Overcast(horizon, overcast * .7f, grayGain) * skyGain,
            elevation >= 0 ? solarAxis : -solarAxis,
            MoonColor * moon + sunColor * sun,
            .55f + .45f * Smooth(.85f, .2f, d) + night * (.45f + .20f * overcast),
            state.Wetness, state.PuddleFill, state.SnowCover, state.Rain, state.Snow, mist, haze, d,
            hemiSky * hemisphere, hemiGround * hemisphere, cityGlow);
    }

    private static float LocalSolarPhase(float hour)
    {
        const float Sunrise = 5.5f, Sunset = 18;
        const float DayHours = Sunset - Sunrise;
        float hoursSinceSunrise = hour - Sunrise;
        if (hoursSinceSunrise < 0) hoursSinceSunrise += 24;
        return hoursSinceSunrise <= DayHours
            ? MathF.PI * (hoursSinceSunrise / DayHours)
            : MathF.PI * (1 + (hoursSinceSunrise - DayHours) / (24 - DayHours));
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Vector3 Overcast(Vector3 color, float amount, float grayGain) =>
        Vector3.Lerp(color, new Vector3(Vector3.Dot(color, new(.2126f, .7152f, .0722f)) * grayGain), amount);

    private static Vector3 Color(int hex) => new(ToLinear((byte)(hex >> 16)), ToLinear((byte)(hex >> 8)), ToLinear((byte)hex));

    private static float Approach(float value, float target, float tau, float delta)
    {
        float next = value + (target - value) * (1 - MathF.Exp(-delta / tau));
        return MathF.Abs(next - target) < .001f ? target : next;
    }
}

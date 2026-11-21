using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaMachi
{
    internal readonly record struct WeatherRoof(float X0, float Z0, float X1, float Z1, float Y);
    internal readonly record struct WeatherDrip(float X, float Z, float From, float To, float Period, float Phase);
    internal readonly record struct WeatherGlint(Vector3 Position, float Radius);
    internal readonly record struct SteamPuff(Vector4 Source, Vector2 Seed);
    internal sealed record WeatherLayout(float Half, float GroundY, float WindYaw, float WindSlant,
        WeatherRoof[] Roofs, WeatherDrip[] Drips, WeatherGlint[] Glints,
        SteamPuff[] Steam, Vector3 Puddles);

    private const float Dusk = .45f;
    private static readonly Vector3 NightDirection = Vector3.Normalize(new(-7, 13, 8));
    private static readonly Vector3 DuskDirection = Vector3.Normalize(new(-11, 5.5f, 6));
    private static readonly Vector3 DayDirection = Vector3.Normalize(new(-6, 16, 8));
    private static readonly Vector3 NightSky = Color(0x0e1524), DuskSky = Color(0x2c2f5c), DaySky = Color(0x4d8bd4);
    private static readonly Vector3 NightHorizon = Color(0x26324a), DuskHorizon = Color(0xe58a62), DayHorizon = Color(0xcde4f2);
    private static readonly Vector3 NightSun = Color(0x9cb3dc), DuskSun = Color(0xffae78), DaySun = Color(0xfff0da);
    private static readonly Vector3 NightHemiSky = Color(0x8392ac), DuskHemiSky = Color(0x8b79a6), DayHemiSky = Color(0xbcd6ee);
    private static readonly Vector3 NightHemiGround = Color(0x434350), DuskHemiGround = Color(0x30262a), DayHemiGround = Color(0x5f5b52);
    private static readonly Vector3 KowloonGlow = Color(0x5a4688);

    internal record struct EnvironmentState
    {
        internal FoliaSong? Song;
        internal string? AutomaticWeather;
        internal float Daylight, Rain, Snow, Fog, Wetness, SnowCover;
    }

    internal readonly record struct EnvironmentFrame(Vector3 SkyTop, Vector3 SkyBottom, Vector3 SunDirection, Vector3 SunRadiance,
        float Artificial, float Wetness, float SnowCover, float Rain, float Snow, float Mist, float Haze, float Daylight,
        Vector3 HemiSky, Vector3 HemiGround, Vector3 CityGlow);

    internal static EnvironmentFrame UpdateEnvironment(ref EnvironmentState state, string timeOfDay, string weather,
        FoliaSong song, DateTime now, float delta, bool reset, string? sceneId = null)
    {
        if (!ReferenceEquals(state.Song, song))
        {
            state.Song = song;
            double roll = Hash01(Hash("machi-weather:", song.Title, song.Artist), 0, 0x3d);
            state.AutomaticWeather = roll < .35 ? "rain" : roll < .65 ? "clear" : roll < .85 ? "fog" : "snow";
        }
        float daylight = timeOfDay switch
        {
            "day" => 1,
            "dusk" => Dusk,
            "night" => 0,
            _ => DaylightAt((float)now.TimeOfDay.TotalHours)
        };
        string? kind = weather == "auto" ? state.AutomaticWeather : weather;
        float rain = kind == "rain" ? 1 : 0;
        float snow = kind == "snow" ? 1 : 0;
        float fog = kind == "fog" ? 1 : 0;
        if (reset)
        {
            state.Daylight = daylight;
            state.Rain = rain;
            state.Snow = snow;
            state.Fog = fog;
            state.Wetness = Math.Max(rain, fog * .35f);
            state.SnowCover = snow;
        }
        else if (delta > 0)
        {
            state.Daylight = Approach(state.Daylight, daylight, 1.6f, delta);
            state.Rain = Approach(state.Rain, rain, 1.4f, delta);
            state.Snow = Approach(state.Snow, snow, 1.4f, delta);
            state.Fog = Approach(state.Fog, fog, 2.2f, delta);
            float wet = Math.Max(state.Rain, state.Fog * .35f);
            state.Wetness = Approach(state.Wetness, wet, wet > state.Wetness ? 3 : 20, delta);
            state.SnowCover = Approach(state.SnowCover, state.Snow, state.Snow > state.SnowCover ? 8 : 12, delta);
        }
        float d = state.Daylight;
        float overcast = Math.Max(state.Rain, Math.Max(state.Fog, state.Snow * .8f));
        float sun = (1.35f + (3 - 1.35f) * d) * (1 - .55f * overcast * d) * (1 - .25f * state.Fog);
        float mist = .16f * (1 - .4f * d) + .1f * state.Rain + .45f * state.Fog + .06f * state.Snow;
        float haze = .42f * state.Fog + .08f * state.Snow;
        float grayGain = .9f + .35f * d;
        // Three's hemisphere and ambient lights supply irradiance. A constant
        // environment emits radiance E / pi to deliver that same irradiance.
        float hemisphere = (2.1f + .35f * d + .25f * overcast * d) / MathF.PI;
        Vector3 hemiSky = Overcast(LerpDay(NightHemiSky, DuskHemiSky, DayHemiSky, d), overcast * .7f, grayGain);
        Vector3 hemiGround = Overcast(LerpDay(NightHemiGround, DuskHemiGround, DayHemiGround, d), overcast * .7f, grayGain);
        hemiGround = Vector3.Lerp(hemiGround, new Vector3(.55f, .60f, .70f) * (.35f + .65f * d), state.SnowCover * .5f);
        Vector3 cityGlow = sceneId == "kowloon"
            ? KowloonGlow * (1.1f * (1 - .75f * d) * (1 - .85f * d) / MathF.PI) : Vector3.Zero;
        return new EnvironmentFrame(
            Overcast(LerpDay(NightSky, DuskSky, DaySky, d), overcast * .7f, grayGain),
            Overcast(LerpDay(NightHorizon, DuskHorizon, DayHorizon, d), overcast * .7f, grayGain),
            Vector3.Normalize(LerpDay(NightDirection, DuskDirection, DayDirection, d)),
            Overcast(LerpDay(NightSun, DuskSun, DaySun, d), overcast * .45f, 1) * sun,
            .55f + .45f * Smooth(.85f, .2f, d), state.Wetness, state.SnowCover, state.Rain, state.Snow, mist, haze, d,
            hemiSky * hemisphere, hemiGround * hemisphere, cityGlow);
    }

    private static float DaylightAt(float h) => h switch
    {
        >= 5 and < 6.5f => Smooth(5, 6.5f, h),
        >= 6.5f and < 16.5f => 1,
        >= 16.5f and < 17.5f => 1 - (1 - Dusk) * Smooth(16.5f, 17.5f, h),
        >= 17.5f and < 18.5f => Dusk * (1 - Smooth(17.5f, 18.5f, h)),
        _ => 0
    };

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Vector3 LerpDay(Vector3 night, Vector3 dusk, Vector3 day, float light) =>
        light <= Dusk ? Vector3.Lerp(night, dusk, light / Dusk) : Vector3.Lerp(dusk, day, (light - Dusk) / (1 - Dusk));

    private static Vector3 Overcast(Vector3 color, float amount, float grayGain) =>
        Vector3.Lerp(color, new Vector3(Vector3.Dot(color, new(.2126f, .7152f, .0722f)) * grayGain), amount);

    private static Vector3 Color(int hex) => new(ToLinear((byte)(hex >> 16)), ToLinear((byte)(hex >> 8)), ToLinear((byte)hex));

    private static float Approach(float value, float target, float tau, float delta)
    {
        float next = value + (target - value) * (1 - MathF.Exp(-delta / tau));
        return MathF.Abs(next - target) < .001f ? target : next;
    }
}

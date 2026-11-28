namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaMachi
{
    private const int NoNeonBand = -1;
    private const int BassNeonBand = 0;
    private const int MidNeonBand = 1;
    private const int TrebleNeonBand = 2;

    // The packed meshes have no object names. Identify the existing diffuser and sign faces
    // in the pinned exports by material and world coordinates, including a small rounding margin.
    // Only static artwork (material 5) and physical diffusers (material 11) can respond.
    private static int FindNeonBand(string sceneId, in PackedVertex vertex) => (sceneId, vertex.Material) switch
    {
        ("konbini", 11) => vertex switch
        {
            // Keep the front lip steady where it can cross the window poster at narrow aspect ratios.
            // Pulse the cyan return and the sheltered downlights across the entrance and service bay.
            { Y: > 2.42f and < 2.45f, X: > 1.31f and < 1.36f, Z: > -4.49f and < .32f } => MidNeonBand,
            { Y: > 2.455f and < 2.457f, Z: > .69f and < .77f, X: > -3.95f and < .16f } => BassNeonBand,
            { Y: > 2.461f and < 2.463f, X: > 1.06f and < 1.16f, Z: > -3.83f and < -.99f } => MidNeonBand,
            // The corner sidelight is visible even when the entrance downlights are under the lip.
            { X: > -5.11f and < -4.93f, Y: > 1.60f and < 1.98f, Z: > .434f and < .436f } => BassNeonBand,
            // Recessed door jambs and the amber lamp in the vending bay.
            { X: > -1.37f and < .57f, Y: > .52f and < 2.04f, Z: > .514f and < .516f } => TrebleNeonBand,
            { X: > .834f and < .836f, Y: > .59f and < 1.98f, Z: > -5.02f and < -4.94f } => TrebleNeonBand,
            _ => NoNeonBand,
        },
        ("station", 11) => vertex switch
        {
            // Amber and cyan canopy strips and the existing downlights under the roof.
            { Y: > 3.79f and < 3.83f, Z: > -1.392f and < -1.390f, X: > 1.45f and < 5.85f } => BassNeonBand,
            { Y: > 3.79f and < 3.83f, X: > 5.921f and < 5.923f, Z: > -5.61f and < -1.43f } => MidNeonBand,
            { Y: > 3.844f and < 3.846f, X: > 1.79f and < 5.51f, Z: > -4.66f and < -2.34f } => TrebleNeonBand,
            // The column diffusers carry the treble into the foreground at eye level.
            { Y: > 2.15f and < 2.99f, Z: > -1.906f and < -1.904f, X: > 1.98f and < 5.32f } => TrebleNeonBand,
            // Low pump-island guides sit below the pump displays and across from the motel's panels.
            { Y: > .18f and < .24f, X: > 2.58f and < 4.72f, Z: > -4.08f and < -2.45f } => MidNeonBand,
            _ => NoNeonBand,
        },
        ("kowloon", 5) when vertex.Gain > 0 => vertex switch
        {
            // The original 九龍城寨 rooftop sign, 網吧 billboard, and 牙科 vertical sign.
            { X: > -.03f and < 2.43f, Y: > 6.62f and < 7.48f, Z: > -2.147f and < -2.145f } => BassNeonBand,
            { X: > -3.57f and < -1.38f, Y: > 5.18f and < 5.92f, Z: > .633f and < .635f } => MidNeonBand,
            { X: > -3.32f and < -2.70f, Y: > 3.33f and < 5.47f, Z: > -.767f and < -.765f } => TrebleNeonBand,
            _ => NoNeonBand,
        },
        ("kowloon", 11) => vertex switch
        {
            // The emitters under the food stall canopy and the passage lintel sit inside their housings.
            { X: > 4.21f and < 5.73f, Y: > 2.024f and < 2.026f, Z: > 2.63f and < 2.71f } => BassNeonBand,
            { X: > 4.40f and < 5.06f, Y: > 2.162f and < 2.164f, Z: > 1.67f and < 1.75f } => TrebleNeonBand,
            { X: > 5.22f and < 5.95f, Y: > 2.83f and < 2.93f, Z: > -2.266f and < -2.264f } => MidNeonBand,
            _ => NoNeonBand,
        },
        _ => NoNeonBand,
    };
}

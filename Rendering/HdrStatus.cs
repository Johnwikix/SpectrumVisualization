using System;

namespace WinExSpectrumTest.Rendering;

internal enum HdrOutputMode { Aurora, Disabled, Starting, Unavailable, Active, Failed }

/// <summary>UI-thread-only, transient output state. User preferences remain in AppSettings.</summary>
internal static class HdrStatus
{
    public static event Action? Changed;
    public static HdrOutputMode Mode { get; private set; } = HdrOutputMode.Aurora;
    public static void Set(HdrOutputMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        Changed?.Invoke();
    }
}

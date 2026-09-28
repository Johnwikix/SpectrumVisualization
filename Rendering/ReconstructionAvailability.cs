using System;

namespace WinExSpectrumTest.Rendering;

/// <summary>UI-thread projection of the active GPU host's capabilities and actual reconstruction mode.</summary>
internal static class ReconstructionAvailability
{
    internal static ReconstructionStatus Status { get; private set; }
    internal static bool Known { get; private set; }
    internal static event Action? Changed;

    internal static void Set(ReconstructionStatus status)
    {
        if (Known && Status == status) return;
        Status = status;
        Known = true;
        Changed?.Invoke();
    }

    internal static bool Supports(ReconstructionMode mode) => Known && (Status.Capabilities & (1u << (int)mode)) != 0;

    internal static void Clear()
    {
        if (!Known) return;
        Known = false;
        Status = default;
        Changed?.Invoke();
    }
}

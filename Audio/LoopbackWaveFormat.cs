using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using NAudio.Wave;

namespace WinExSpectrumTest.Audio;

// NAudio 3.0.1 marshals WaveFormatExtensible as an inherited formatted class.
// NativeAOT does not preserve that layout. Read the native WAVEFORMATEX header
// directly and request a non-inherited IEEE-float format for our float decoder.
internal static partial class LoopbackWaveFormat
{
    internal static WaveFormat Read(string deviceId)
    {
        Guid clsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        Guid iid = typeof(IDeviceEnumerator).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in iid, out nint pointer));
        var enumerator = Wrap<IDeviceEnumerator>(pointer);
        IDevice? device = null;
        IClient? client = null;
        nint format = 0;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(deviceId, out pointer));
            device = Wrap<IDevice>(pointer);
            iid = typeof(IClient).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(in iid, 23, 0, out pointer));
            client = Wrap<IClient>(pointer);
            Marshal.ThrowExceptionForHR(client.GetMixFormat(out format));
            return FromNativeHeader(format);
        }
        finally
        {
            Marshal.FreeCoTaskMem(format);
            if ((object?)client is ComObject clientObject) clientObject.FinalRelease();
            if ((object?)device is ComObject deviceObject) deviceObject.FinalRelease();
            if ((object)enumerator is ComObject enumeratorObject) enumeratorObject.FinalRelease();
        }
    }

    internal static WaveFormat FromNativeHeader(nint format)
    {
        // WAVEFORMATEX is packed at 2 bytes: channels at 2, sample rate at 4.
        int channels = (ushort)Marshal.ReadInt16(format, 2);
        int sampleRate = Marshal.ReadInt32(format, 4);
        if (channels == 0 || sampleRate <= 0)
            throw new InvalidOperationException("The audio endpoint returned an invalid mix format.");
        return WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    private static T Wrap<T>(nint pointer)
    {
        try
        {
            return (T)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(
                pointer, CreateObjectFlags.UniqueInstance);
        }
        finally { Marshal.Release(pointer); }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [GeneratedComInterface, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal partial interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out nint device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out nint device);
    }

    [GeneratedComInterface, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal partial interface IDevice
    {
        [PreserveSig] int Activate(in Guid iid, uint context, nint parameters, out nint instance);
    }

    [GeneratedComInterface, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    internal partial interface IClient
    {
        [PreserveSig] int Initialize(int shareMode, uint flags, long duration, long period, nint format, in Guid session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);
        [PreserveSig] int GetMixFormat(out nint format);
    }
}

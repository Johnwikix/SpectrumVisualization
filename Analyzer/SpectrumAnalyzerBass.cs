using ManagedBass;
using ManagedBass.Wasapi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Analyzer
{   
    public class SpectrumAnalyzerBass
    {
        private float[] _spectrumData = new float[1024];
        float[] leftSpectrum = new float[512];
        float[] rightSpectrum = new float[512];
        public event Action<float[]> SpectrumDataUpdated;
        private readonly WasapiProcedure _myWasapiProcedure;
        public SpectrumAnalyzerBass() {
            _myWasapiProcedure = OnWasapiProc;
            if (!Bass.Init(-1))
            {
                Debug.WriteLine("Bass Init failed: " + Bass.LastError);
                return;
            }
            if (!BassWasapi.Init(-3, AppSettings.SampleRate, 2, WasapiInitFlags.Shared | WasapiInitFlags.Buffer,0,0, _myWasapiProcedure))
            {
                Debug.WriteLine("WASAPI Init failed: " + Bass.LastError);
                return;
            }
        }

        private int OnWasapiProc(IntPtr buffer, int length, IntPtr user)
        {
            var res = BassWasapi.GetData(_spectrumData, (int)DataFlags.FFTIndividual | (int) DataFlags.FFT1024);
            if (res == -1)
            {
                Debug.WriteLine("Error getting spectrum data: " + Bass.LastError);
            }
            for (int i = 0; i < 512; i++)
            {
                leftSpectrum[i] = _spectrumData[2 * i];
                rightSpectrum[i] = _spectrumData[2 * i + 1];
            }
            Array.Reverse(leftSpectrum);
            Array.Copy(leftSpectrum, 0, _spectrumData, 0, 512);
            Array.Copy(rightSpectrum, 0, _spectrumData, 512, 512);
            SpectrumDataUpdated?.Invoke(_spectrumData);
            return res;
        }

        public void StartCapture()
        {
            if (!BassWasapi.Start()) {
                Debug.WriteLine("WASAPI start failed: " + Bass.LastError);
                return;
            }
        }

        public void Dispose()
        {
            BassWasapi.Stop();  
            BassWasapi.Free();
            Bass.Free();
        }
    }
}

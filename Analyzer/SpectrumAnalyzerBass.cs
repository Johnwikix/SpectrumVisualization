using ManagedBass;
using ManagedBass.Wasapi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WinExSpectrumTest.Analyzer
{   
    public class SpectrumAnalyzerBass
    {
        private float[] _spectrumData = new float[1024];
        public event Action<float[]> SpectrumDataUpdated;
        private readonly WasapiProcedure _myWasapiProcedure;
        public SpectrumAnalyzerBass() {
            _myWasapiProcedure = OnWasapiProc;
            if (!Bass.Init(-1))
            {
                Debug.WriteLine("Bass Init failed: " + Bass.LastError);
                return;
            }
            if (!BassWasapi.Init(-3, 6000, 2, WasapiInitFlags.Shared | WasapiInitFlags.Buffer,0,0, _myWasapiProcedure))
            {
                Debug.WriteLine("WASAPI Init failed: " + Bass.LastError);
                return;
            }
        }

        private int OnWasapiProc(IntPtr buffer, int length, IntPtr user)
        {
            var res = BassWasapi.GetData(_spectrumData, (int)DataFlags.FFT1024);
            if (res == -1)
            {
                Debug.WriteLine("Error getting spectrum data: " + Bass.LastError);
            }
            for (int i = 0; i < _spectrumData.Length; i++)
            {                
                _spectrumData[i] = (float)(Math.Log10(_spectrumData[i] + 1) * 10); // Convert to dB scale
            }
            for (int i = 0; i < 512; i++)
            {
                int sourceIndex = 512 - 1 - i;

                // 目标索引 (顺序): 512, 513, ..., 1022, 1023
                int destIndex = 512 + i;

                // 镜像赋值
                if (destIndex < _spectrumData.Length) // 避免数组越界，虽然 1023 < 2048
                {
                    _spectrumData[destIndex] = _spectrumData[sourceIndex];
                }
            }

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

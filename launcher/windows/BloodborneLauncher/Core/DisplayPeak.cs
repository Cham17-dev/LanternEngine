using System.Runtime.InteropServices;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// The peak luminance Windows reports for a display (DXGI_OUTPUT_DESC1::MaxLuminance, as
    /// RenoDX reads it), only while that display is in HDR: the default Peak brightness, as the
    /// engine's own (gpu/shim/display_peak.cpp, which this follows). DXGI through its vtables:
    /// IDXGIFactory1::EnumAdapters1 (12), IDXGIAdapter1::EnumOutputs (7), IDXGIOutput6::GetDesc1
    /// (27), IUnknown's QueryInterface (0) and Release (2).
    /// </summary>
    public static class DisplayPeak
    {
        /// <summary>Peak brightness default when Windows reports none (bbport_settings.h).</summary>
        public const double Fallback = 1000;

        /// <summary>The peak of the display `device` ("\\.\DISPLAY1"; empty: the primary one)
        /// in nits, 0 when unknown or not in HDR.</summary>
        public static double Nits(string device)
        {
            try
            {
                return Query(device);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>The default Peak brightness for the display chosen in the launcher.</summary>
        public static double Default(string device)
        {
            var nits = Nits(device);
            return nits > 0 ? Math.Clamp(Math.Round(nits), 200, 4000) : Fallback;
        }

        private static double Query(string device)
        {
            var factoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            var output6Id = new Guid("068346e8-aaec-4b84-add7-137f513f77a1"); // IDXGIOutput6
            if (CreateDXGIFactory1(ref factoryId, out var factory) != 0 || factory == IntPtr.Zero) return 0;
            double primary = 0, chosen = 0, any = 0;
            try
            {
                var enumAdapters = Method<EnumFn>(factory, 12);
                for (uint a = 0; enumAdapters(factory, a, out var adapter) == 0; ++a)
                {
                    try
                    {
                        var enumOutputs = Method<EnumFn>(adapter, 7);
                        for (uint o = 0; enumOutputs(adapter, o, out var output) == 0; ++o)
                        {
                            try
                            {
                                if (Method<QueryInterfaceFn>(output, 0)(output, ref output6Id, out var output6) != 0) continue;
                                try
                                {
                                    if (Method<GetDesc1Fn>(output6, 27)(output6, out var desc) != 0) continue;
                                    // The current color space: Windows HDR on for this display.
                                    if (desc.ColorSpace != Hdr10ColorSpace || desc.MaxLuminance <= 0) continue;
                                    double nits = desc.MaxLuminance;
                                    any = Math.Max(any, nits);
                                    if (device.Length > 0 && string.Equals(desc.DeviceName, device, StringComparison.OrdinalIgnoreCase))
                                        chosen = nits;
                                    if (desc.Left == 0 && desc.Top == 0)
                                        primary = nits;
                                }
                                finally { Release(output6); }
                            }
                            finally { Release(output); }
                        }
                    }
                    finally { Release(adapter); }
                }
            }
            finally { Release(factory); }
            return chosen > 0 ? chosen : primary > 0 ? primary : any;
        }

        private const int Hdr10ColorSpace = 12; // DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020

        private static T Method<T>(IntPtr self, int slot) where T : Delegate
        {
            var vtable = Marshal.ReadIntPtr(self);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        }

        private static void Release(IntPtr self)
        {
            if (self != IntPtr.Zero) Method<ReleaseFn>(self, 2)(self);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumFn(IntPtr self, uint index, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceFn(IntPtr self, ref Guid id, out IntPtr result);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDesc1Fn(IntPtr self, out OutputDesc1 desc);

        /// <summary>DXGI_OUTPUT_DESC1.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OutputDesc1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public int Left, Top, Right, Bottom; // DesktopCoordinates
            public int AttachedToDesktop;
            public int Rotation;
            public IntPtr Monitor;
            public uint BitsPerColor;
            public int ColorSpace;
            public float RedX, RedY, GreenX, GreenY, BlueX, BlueY, WhiteX, WhiteY;
            public float MinLuminance, MaxLuminance, MaxFullFrameLuminance;
        }

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1(ref Guid id, out IntPtr factory);
    }
}

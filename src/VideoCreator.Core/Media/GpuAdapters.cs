using System.Runtime.InteropServices;

namespace VideoCreator.Media;

/// <summary>A DXGI adapter; <see cref="Index"/> is the DirectML device_id (DXGI enumeration order — verified on a hybrid Iris Xe + RTX 4060 laptop).</summary>
public sealed record GpuAdapter(int Index, string Name, long DedicatedMemory);

/// <summary>
/// Picks the GPU DirectML should use. device_id 0 is whatever DXGI lists first, which on hybrid laptops is the integrated GPU
/// (measured 1.94 s vs 0.22 s per 720x1280 4x frame against the discrete one), so the adapter with the most dedicated video memory wins.
/// </summary>
public static class GpuAdapters
{
    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIFactory1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();   // IDXGIObject
        void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter();   // IDXGIFactory
        [PreserveSig] int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIAdapter1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();   // IDXGIObject
        void EnumOutputs(); void GetDesc(); void CheckInterfaceSupport();   // IDXGIAdapter
        [PreserveSig] int GetDesc1(out Desc1 desc);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Desc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long Luid;
        public uint Flags;
    }

    const uint SoftwareAdapter = 2;

    /// <summary>Hardware adapters in DXGI order; empty off Windows or when DXGI is unavailable.</summary>
    public static List<GpuAdapter> List()
    {
        var list = new List<GpuAdapter>();
        if (!OperatingSystem.IsWindows()) return list;
        try
        {
            var iid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref iid, out var ptr) < 0) return list;
            var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(ptr);
            try
            {
                for (uint i = 0; factory.EnumAdapters1(i, out var a) >= 0; i++)
                {
                    try
                    {
                        if (a.GetDesc1(out var d) >= 0 && (d.Flags & SoftwareAdapter) == 0) list.Add(new GpuAdapter((int)i, d.Description.Trim(), (long)d.DedicatedVideoMemory));
                    }
                    finally { Marshal.ReleaseComObject(a); }
                }
            }
            finally { Marshal.ReleaseComObject(factory); Marshal.Release(ptr); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException or InvalidCastException) { list.Clear(); }
        return list;
    }

    /// <summary>Adapters to try, best first: most dedicated memory (the discrete GPU), then the rest. Falls back to device 0 when DXGI cannot be read.</summary>
    public static List<GpuAdapter> Preferred()
    {
        var l = Rank(List());
        return l.Count > 0 ? l : [new GpuAdapter(0, "default adapter", 0)];
    }

    public static List<GpuAdapter> Rank(IEnumerable<GpuAdapter> adapters) => adapters.OrderByDescending(a => a.DedicatedMemory).ThenBy(a => a.Index).ToList();
}

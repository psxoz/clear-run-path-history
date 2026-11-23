using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct StatUrl
    {
        public int Size;
        public IntPtr Url;
        public IntPtr Title;
        public FILETIME LastVisited;
        public FILETIME LastUpdated;
        public FILETIME Expires;
        public uint Flags;
    }

    [ComImport, Guid("3C374A42-BAE4-11CF-BF7D-00AA006946EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumStatUrl
    {
        [PreserveSig]
        int Next(uint count, ref StatUrl entry, out uint fetched);
    }

    [ComImport, Guid("3C374A41-BAE4-11CF-BF7D-00AA006946EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUrlHistory
    {
        void AddUrl([MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.LPWStr)] string title, uint flags);
        void DeleteUrl([MarshalAs(UnmanagedType.LPWStr)] string url, uint flags);
        void QueryUrl([MarshalAs(UnmanagedType.LPWStr)] string url, uint flags, ref StatUrl entry);
        void BindToObject([MarshalAs(UnmanagedType.LPWStr)] string url, ref Guid interfaceId, out IntPtr target);
        void EnumUrls(out IEnumStatUrl urls);
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            var classId = new Guid("3C374A40-BAE4-11CF-BF7D-00AA006946EE");
            var history = (IUrlHistory)Activator.CreateInstance(Type.GetTypeFromCLSID(classId, true));
            try
            {
                foreach (string url in ReadLocalFileUrls(history))
                    history.DeleteUrl(url, 0);
            }
            finally
            {
                Marshal.ReleaseComObject(history);
            }

            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(
                "Не удалось очистить историю локальных путей.\n\n" + error.Message,
                "Очистка подсказок «Выполнить»",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static HashSet<string> ReadLocalFileUrls(IUrlHistory history)
    {
        var localFileUrl = new Regex(@"^file:///[A-Z]:/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var localUrls = new HashSet<string>(StringComparer.Ordinal);
        IEnumStatUrl urls;
        history.EnumUrls(out urls);
        try
        {
            int entrySize = Marshal.SizeOf(typeof(StatUrl));
            while (true)
            {
                var entry = new StatUrl { Size = entrySize };
                try
                {
                    uint fetched;
                    Marshal.ThrowExceptionForHR(urls.Next(1, ref entry, out fetched));
                    if (fetched == 0)
                        return localUrls;

                    string url = Marshal.PtrToStringUni(entry.Url);
                    if (url != null && localFileUrl.IsMatch(url))
                        localUrls.Add(url);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(entry.Url);
                    Marshal.FreeCoTaskMem(entry.Title);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(urls);
        }
    }
}


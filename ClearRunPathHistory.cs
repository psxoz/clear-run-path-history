using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Xml;

[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]

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

    [ComImport, Guid("12337D35-94C6-48A0-BCE7-6A9C69D4D600"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationDestinations
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void RemoveDestination([MarshalAs(UnmanagedType.IUnknown)] object destination);
        void RemoveAllDestinations();
    }

    private const string StartSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Start";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr window, uint message, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);

    [STAThread]
    private static void Main()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        while (true)
        {
            Console.WriteLine("ClearRunPathHistory 1.2.0");
            Console.WriteLine("1 - Очистить подсказки после / в Win+R");
            Console.WriteLine("2 - Очистить «Недавние файлы» в Проводнике");
            Console.WriteLine("3 - Скрыть «Недавно добавленные» в «Пуске»");
            Console.WriteLine("4 - Открепить плитки справа в «Пуске» (Windows 10)");
            Console.WriteLine("0 - Выход");
            Console.Write("> ");

            string choice = Console.ReadLine();
            if (choice == null)
                return;

            try
            {
                switch (choice.Trim())
                {
                    case "0":
                        return;
                    case "1":
                        ClearRunPathHistory();
                        Console.WriteLine("Подсказки очищены.");
                        break;
                    case "2":
                        ClearRecentFiles("Microsoft.Windows.Explorer_RecentFiles");
                        Console.WriteLine("Список «Недавние файлы» очищен.");
                        break;
                    case "3":
                        HideRecentlyAddedApps();
                        Console.WriteLine("Показ «Недавно добавленных» отключён в настройках «Пуска».");
                        break;
                    case "4":
                        UnpinStartApps();
                        break;
                    default:
                        Console.WriteLine("Выбери номер от 0 до 4.");
                        break;
                }
            }
            catch (Exception error)
            {
                Console.WriteLine("Ошибка очистки. " + error.Message);
            }

            Console.WriteLine();
        }
    }

    private static void HideRecentlyAddedApps()
    {
        using (RegistryKey startSettings = Registry.CurrentUser.CreateSubKey(StartSettingsKey))
            startSettings.SetValue("ShowRecentList", 0, RegistryValueKind.DWord);

        UIntPtr result;
        SendMessageTimeoutW(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero,
            "Start", 0x0002, 200, out result);
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(uint initializationType);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern void RoUninitialize();

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid interfaceId, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string value, uint length, out IntPtr handle);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetStartSurface(IntPtr factory, out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CheckStartTile(IntPtr surface, IntPtr appId, IntPtr tileId, out byte value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UnpinStartTile(IntPtr surface, IntPtr appId, IntPtr tileId);

    private static void UnpinStartApps()
    {
        XmlDocument layout = ReadStartLayout();
        XmlNodeList tiles = layout.SelectNodes(
            "//*[local-name()='Group']//*[local-name()='Tile' or local-name()='DesktopApplicationTile' or local-name()='SecondaryTile']");
        if (tiles.Count == 0)
        {
            Console.WriteLine("Плиток для открепления нет.");
            return;
        }

        IntPtr className = IntPtr.Zero;
        IntPtr factory = IntPtr.Zero;
        IntPtr surface = IntPtr.Zero;
        int unpinned = 0;
        int skipped = 0;
        Marshal.ThrowExceptionForHR(RoInitialize(0));
        try
        {
            className = NewWindowsString("Windows.Internal.ApplicationModel.StartPinnableSurface");
            var factoryId = new Guid("F27684E4-E634-4807-BE9A-4838381FCBFC");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref factoryId, out factory));
            var getCurrent = (GetStartSurface)GetComMethod(factory, 6, typeof(GetStartSurface));
            Marshal.ThrowExceptionForHR(getCurrent(factory, out surface));

            var isPinned = (CheckStartTile)GetComMethod(surface, 9, typeof(CheckStartTile));
            var canUnpin = (CheckStartTile)GetComMethod(surface, 10, typeof(CheckStartTile));
            var unpinTile = (UnpinStartTile)GetComMethod(surface, 11, typeof(UnpinStartTile));
            foreach (XmlElement tile in tiles)
            {
                string appId = tile.GetAttribute("AppUserModelID");
                if (appId.Length == 0)
                    appId = tile.GetAttribute("DesktopApplicationID");
                string tileId = tile.GetAttribute("TileID");

                if (appId.Length == 0)
                {
                    skipped++;
                    continue;
                }

                if (tile.LocalName == "DesktopApplicationTile")
                {
                    if (UnpinDesktopStartTile(appId))
                        unpinned++;
                    else
                        skipped++;
                    continue;
                }

                IntPtr appName = NewWindowsString(appId);
                IntPtr tileName = IntPtr.Zero;
                try
                {
                    if (tileId.Length != 0)
                        tileName = NewWindowsString(tileId);

                    byte pinned;
                    Marshal.ThrowExceptionForHR(isPinned(surface, appName, tileName, out pinned));
                    if (pinned == 0)
                        continue;

                    byte allowed;
                    Marshal.ThrowExceptionForHR(canUnpin(surface, appName, tileName, out allowed));
                    if (allowed == 0)
                    {
                        skipped++;
                        continue;
                    }

                    Marshal.ThrowExceptionForHR(unpinTile(surface, appName, tileName));
                    unpinned++;
                }
                finally
                {
                    WindowsDeleteString(tileName);
                    WindowsDeleteString(appName);
                }
            }
        }
        finally
        {
            if (surface != IntPtr.Zero)
                Marshal.Release(surface);
            if (factory != IntPtr.Zero)
                Marshal.Release(factory);
            WindowsDeleteString(className);
            RoUninitialize();
        }

        Console.WriteLine("Откреплено плиток - " + unpinned + ".");
        if (skipped != 0)
            Console.WriteLine("Плиток, которые не удалось открепить - " + skipped + ".");
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(
        string source, StringBuilder output, uint outputLength, IntPtr reserved);

    private static bool UnpinDesktopStartTile(string appId)
    {
        var caption = new StringBuilder(256);
        Marshal.ThrowExceptionForHR(SHLoadIndirectString(
            "@shell32.dll,-51394", caption, (uint)caption.Capacity, IntPtr.Zero));
        string unpinCaption = caption.ToString().Replace("&", "");

        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true));
        dynamic app = shell.NameSpace("shell:AppsFolder").ParseName(appId);
        if (app == null)
            return false;

        dynamic verbs = app.Verbs();
        int count = verbs.Count;
        for (int index = 0; index < count; index++)
        {
            dynamic verb = verbs.Item(index);
            string name = verb.Name;
            if (name == null || !String.Equals(name.Replace("&", ""), unpinCaption, StringComparison.Ordinal))
                continue;

            verb.DoIt();
            return true;
        }

        return false;
    }

    private static IntPtr NewWindowsString(string value)
    {
        IntPtr handle;
        Marshal.ThrowExceptionForHR(WindowsCreateString(value, (uint)value.Length, out handle));
        return handle;
    }

    private static Delegate GetComMethod(IntPtr instance, int slot, Type type)
    {
        IntPtr table = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, slot * IntPtr.Size), type);
    }

    private static XmlDocument ReadStartLayout()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
        string command = "Export-StartLayout -UseDesktopApplicationID -LiteralPath '"
            + path.Replace("'", "''") + "' -ErrorAction Stop";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var startInfo = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -EncodedCommand " + encoded)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Не удалось прочитать плитки «Пуска». Этот пункт рассчитан на Windows 10.");
            }

            var layout = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(path))
                layout.Load(reader);
            return layout;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void ClearRecentFiles(string appId)
    {
        var classId = new Guid("86C14003-4D6B-4EF3-A7B4-0506663B2E68");
        var destinations = (IApplicationDestinations)Activator.CreateInstance(Type.GetTypeFromCLSID(classId, true));
        try
        {
            destinations.SetAppID(appId);
            destinations.RemoveAllDestinations();
        }
        finally
        {
            Marshal.ReleaseComObject(destinations);
        }
    }

    private static void ClearRunPathHistory()
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


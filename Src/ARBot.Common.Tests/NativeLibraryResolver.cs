using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ARBot.Common.Algorithms.ComputeUnit;

namespace ARBot.Common.Tests
{
    /// <summary>
    /// Mapuje P/Invoke jmeno "NativeLib.dll" na spravny nativni soubor podle OS:
    /// Windows -> NativeLib.dll, ostatni (Linux/ARM) -> libNativeLib.so.
    /// Diky tomu bezi stejne testy proti x64 DLL i proti ARM .so.
    /// </summary>
    internal static class NativeLibraryResolver
    {
        [ModuleInitializer]
        internal static void Init()
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeComputeUnit).Assembly, Resolve);
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == "NativeLib.dll")
            {
                string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "NativeLib.dll"
                    : "libNativeLib.so";
                string path = Path.Combine(AppContext.BaseDirectory, fileName);
                if (NativeLibrary.TryLoad(path, out var handle))
                    return handle;
            }
            return IntPtr.Zero;
        }
    }
}

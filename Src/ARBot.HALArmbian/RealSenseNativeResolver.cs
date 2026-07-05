using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ARBot.Common.Algorithms.ComputeUnit;

namespace ARBot.HALArmbian
{
    /// <summary>
    /// Mapuje P/Invoke jmena nativnich knihoven na jejich Linux/ARM ekvivalenty:
    ///  - Intel.RealSense wrapper: "realsense2"/"realsense2d"/"-net" -> librealsense2.so
    ///  - NativeComputeUnit (ARBot.Common): "NativeLib.dll" -> libNativeLib.so
    /// D435Camera pouziva NativeComputeUnit.CopyIntPtr/ReverseInt16IntPtr apod. pro kopie snimku,
    /// takze bez tohoto resolveru appka na Pi pada na DllNotFoundException('NativeLib.dll').
    /// Registruje se automaticky pri nacteni ARBot.HALArmbian.
    /// </summary>
    internal static class RealSenseNativeResolver
    {
        [ModuleInitializer]
        internal static void Init()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            NativeLibrary.SetDllImportResolver(typeof(Intel.RealSense.Pipeline).Assembly, ResolveRealSense);
            NativeLibrary.SetDllImportResolver(typeof(NativeComputeUnit).Assembly, ResolveNativeLib);
        }

        private static IntPtr ResolveRealSense(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != null && libraryName.StartsWith("realsense2", StringComparison.Ordinal))
            {
                foreach (var cand in new[] { "realsense2", "librealsense2.so", "librealsense2.so.2.53" })
                {
                    if (NativeLibrary.TryLoad(cand, assembly, searchPath, out var handle))
                        return handle;
                }
            }
            return IntPtr.Zero;
        }

        private static IntPtr ResolveNativeLib(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == "NativeLib.dll")
            {
                var path = Path.Combine(AppContext.BaseDirectory, "libNativeLib.so");
                if (NativeLibrary.TryLoad(path, out var handle))
                    return handle;
                if (NativeLibrary.TryLoad("libNativeLib.so", assembly, searchPath, out handle))
                    return handle;
            }
            return IntPtr.Zero;
        }
    }
}

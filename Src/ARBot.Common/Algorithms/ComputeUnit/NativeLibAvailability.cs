using System;

namespace ARBot.Common.Algorithms.ComputeUnit
{
    /// <summary>
    /// Je nativní knihovna (<c>NativeLib.dll</c> / <c>libNativeLib.so</c>) k dispozici?
    ///
    /// <para><b>Nač to je (nález auditu 15. 9. 2026).</b> Knihovna <b>není v gitu</b> — staví se
    /// z <c>Src/NativeFuncs</c> (MSVC pro x64, ARM64 pro zařízení). Na vývojovém stroji leží
    /// vedle z dřívějšího buildu, takže tam nic nechybí; v <b>čistém klonu</b> a na CI runneru
    /// ale chybí. Testy, které na ni sahají, pak nepadaly na svém tvrzení, ale na
    /// <c>DllNotFoundException</c> — a to vypadá jako regrese, ačkoli je to jen chybějící
    /// závislost.</para>
    ///
    /// <para>Odpověď je <b>přeskočit</b>, ne předstírat úspěch: ta část se opravdu neověřila.
    /// Výsledek se cachuje, protože zjištění je P/Invoke.</para>
    /// </summary>
    public static class NativeLibAvailability
    {
        private static bool? dostupna;

        /// <summary>Jde nativní knihovnu zavolat?</summary>
        public static bool JeDostupna
        {
            get
            {
                if (dostupna.HasValue) return dostupna.Value;

                try
                {
                    // Nejlevnejsi volani, ktere knihovnu opravdu natahne. Nic se nealokuje:
                    // ComputeFree(IntPtr.Zero) je no-op, ale P/Invoke uz musi knihovnu najit.
                    NativeComputeUnit.ZkusNacist();
                    dostupna = true;
                }
                catch (DllNotFoundException) { dostupna = false; }
                catch (EntryPointNotFoundException) { dostupna = false; }
                catch (BadImageFormatException) { dostupna = false; }

                return dostupna.Value;
            }
        }

        /// <summary>Vysvětlení pro přeskočený test.</summary>
        public const string Duvod =
            "Nativni knihovna (NativeLib.dll / libNativeLib.so) neni k dispozici - stavi se "
            + "z Src/NativeFuncs a v gitu neni. Test se preskakuje, nepada.";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// <b>Hledání zařízení ve výčtu, které přežije chybu jednoho zařízení</b> (13. 10. 2026,
    /// <c>hw-vetev-usb-2-1-3</c>). Jádro <c>RealSenseShared.Query</c>, vytažené sem kvůli testům
    /// (bez kamery a bez nativní knihovny).
    ///
    /// <para><b>Proč:</b> dotaz do 13. 10. 2026 končil PRVNÍ výjimkou. Čtení údajů D435, která právě
    /// streamuje, hází „failed to set power state" — takže kamera, která po restartu pipeline hledala
    /// sama sebe a byla ve výčtu ZA streamující kamerou, se nenašla nikdy a zůstala viset, dokud
    /// supervizor nezboural všechny pipeline (tvrdý zásek podoby A). Ze záznamů 2. 9.–1. 10. to sedí:
    /// zasekávala se vždy ta kamera, která byla na vyšším portu hubu, a jen když druhá streamovala.
    /// Teď se zařízení s chybou přeskočí a hledá se dál.</para>
    ///
    /// <para><b>Výsledek:</b> <c>true</c> = nalezeno (i když jiné zařízení hodilo), <c>false</c> = prošlo
    /// se všechno bez chyby a hledané mezi nimi není (opravdu chybí), <c>null</c> = nenalezeno a aspoň
    /// jedno zařízení hodilo — pak se neví, jestli hledané nebylo právě to.</para>
    /// </summary>
    public static class VycetZarizeni
    {
        /// <summary>Jedna chyba při procházení: kolikáté zařízení (od 0), v jakém kroku, s jakou zprávou.</summary>
        public readonly struct Chyba
        {
            public readonly int Index;
            public readonly string Krok;
            public readonly string Zprava;
            public Chyba(int index, string krok, string zprava) { Index = index; Krok = krok; Zprava = zprava; }
        }

        /// <summary>Výsledek hledání.</summary>
        public sealed class Vysledek
        {
            /// <summary>true / false / null - viz třída.</summary>
            public bool? Nalezeno;
            /// <summary>Kolikáté zařízení bylo to hledané (od 0); -1 = nenalezeno.</summary>
            public int IndexNalezeneho = -1;
            /// <summary>Počet zařízení ve výčtu.</summary>
            public int Pocet;
            /// <summary>Chyby jednotlivých zařízení v pořadí, jak přišly.</summary>
            public readonly List<Chyba> Chyby = new List<Chyba>();

            /// <summary>Popis chyb pro Trace: „zarizeni 1/2 (cteni udaju): zprava; ...".</summary>
            public string PopisChyb()
                => string.Join("; ", Chyby.Select(c => $"zarizeni {c.Index + 1}/{Pocet} ({c.Krok}): {c.Zprava}"));
        }

        /// <summary>Krok, ve kterém zařízení hodilo: vytvoření objektu zařízení ze seznamu.</summary>
        public const string KrokVytvoreni = "vytvoreni zarizeni";
        /// <summary>Krok, ve kterém zařízení hodilo: čtení jeho údajů (sériové číslo, jméno) při porovnání.</summary>
        public const string KrokShoda = "cteni udaju";

        /// <summary>
        /// Projde zařízení 0..<paramref name="pocet"/>-1 a vrátí, jestli je mezi nimi hledané. Výjimka
        /// z <paramref name="vytvor"/> nebo <paramref name="shoda"/> se zapíše a pokračuje se dalším
        /// zařízením; <paramref name="uvolni"/> se volá u každého vytvořeného zařízení. Končí prvním
        /// nalezeným.
        /// </summary>
        public static Vysledek Najdi<T>(int pocet, Func<int, T> vytvor, Func<T, bool> shoda, Action<T> uvolni)
        {
            var v = new Vysledek { Pocet = Math.Max(0, pocet) };
            for (int i = 0; i < v.Pocet; i++)
            {
                T d;
                try { d = vytvor(i); }
                catch (Exception ex) { v.Chyby.Add(new Chyba(i, KrokVytvoreni, ex.Message)); continue; }

                bool je = false;
                try { je = shoda(d); }
                catch (Exception ex) { v.Chyby.Add(new Chyba(i, KrokShoda, ex.Message)); }
                finally { try { uvolni?.Invoke(d); } catch { /* uvolneni nesmi shodit hledani */ } }

                if (je)
                {
                    v.Nalezeno = true;
                    v.IndexNalezeneho = i;
                    return v;
                }
            }
            v.Nalezeno = v.Chyby.Count > 0 ? (bool?)null : false;
            return v;
        }
    }
}

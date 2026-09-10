using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// <b>Stav kalibrace magnetometru</b> za jeden interval sberu (~1 s).
    ///
    /// <para><b>Proc zprava a ne jen text na strance.</b> Ve streamu jde soucasne do webu (zivy
    /// ukazatel pro obsluhu, ktera stoji u robota) i do <b>zaznamu</b> — takze verdikt z pole je
    /// pozdeji dohledatelny a <c>ARBot.Analyze magcal</c> ho umi postavit vedle vlastniho
    /// prepoctu ze surovych <c>IMUState</c>. Kdyz se ta dve cisla rozejdou, je chyba v KODU,
    /// ne v senzoru — a pozna se to.</para>
    ///
    /// <para><see cref="Reg23Before"/> a <see cref="BRefG"/> se nesou proto, ze bez nich se
    /// vysledek nedá vylozit: <c>BRefG</c> je meritko, na ktere se normovalo (cte se ze
    /// senzoru, neni to konstanta), a <c>Reg23Before</c> rika, co v senzoru bylo pred merenim.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    [Serializable()]
    public class MagCalMsg : Message, IHasCaptureTime
    {
        /// <summary>
        /// Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).
        ///
        /// <para><b>2</b> (10. 9. 2026): pribyla <see cref="Grid"/> s aktualni bunkou a cisla
        /// z prolozeni koule. Verze 1 se cte dal, jen s prazdnou mrizkou.</para>
        /// </summary>
        public const int FormatVersion = 2;

        /// <summary>Faze mise (<c>MagCalPhase</c> jako int, aby zprava prezila doplneni hodnot).</summary>
        public int Phase;

        /// <summary>Podminenost navrhove matice — <b>urcenost</b> soustavy, nutna podminka.</summary>
        public double Condition;

        /// <summary>Rozptyl <c>|B|</c> po korekci [G] — kvalita, ne urcenost.</summary>
        public double SdMagnitudeG;

        /// <summary>Rozptyl sklonu po korekci [deg]; <see cref="double.NaN"/> bez akcelerometru.</summary>
        public double SdInclinationDeg;

        /// <summary>
        /// Dvanact cisel ke zkopirovani za <c>VNWRG,23,</c>; prazdne, dokud se neprolozilo.
        /// </summary>
        public string Vnwrg23;

        /// <summary>Verdikt pro cloveka: co udelat dal, nebo ze je hotovo.</summary>
        public string Verdict;

        /// <summary>Co jeste chybi v pokryti; prazdne = nic.</summary>
        public string MissingText;

        /// <summary>Kolik azimutovych kosu ma dost vzorku.</summary>
        public int FilledAzimuthBins;

        /// <summary>Naklonove skupiny s dostatecnym azimutovym pokrytim.</summary>
        public int TiltGroups;

        /// <summary>Z nich odklonene aspon o prah.</summary>
        public int TiltedGroups;

        /// <summary>
        /// Je robot naklonen na dve RUZNE strany? Bez toho je soustava skoro tak degenerovana
        /// jako pri rotaci na rovine — zmereno, viz <c>MagCalThresholds.MaxCondition</c>.
        /// </summary>
        public bool HasOppositeTilts;

        /// <summary>Kolik vzorku se nasbiralo.</summary>
        public int Samples;

        /// <summary>
        /// Referencni <c>|B|</c> [G] z registru 21 — <b>ctene ze senzoru</b>, ne konstanta.
        /// Na tuhle hodnotu se normuje, takze bez ni se vysledek neda porovnat.
        /// </summary>
        public double BRefG;

        /// <summary>Registr 23 pri zacatku mise (dvanact cisel), nebo prazdne.</summary>
        public string Reg23Before;

        /// <summary>
        /// <b>Mrizka pokryti</b> — radek = poloha robota (rovina a ctyri smery podlozeni),
        /// sloupec = azimutovy kos, hodnota = pocet vzorku. Prazdne u verze 1.
        ///
        /// <para>Nese se do zaznamu, ne jen na stranku: bez toho by pozdeji neslo dohledat,
        /// co obsluha v poli videla, a <c>ARBot.Analyze magcal</c> by to nemel z ceho postavit.</para>
        /// </summary>
        public int[][] Grid = Array.Empty<int[]>();

        /// <summary>Radek mrizky, ve kterem robot prave je; <c>-1</c> = nezname.</summary>
        public int CurrentRow = -1;

        /// <summary>Azimutovy kos, ve kterem robot prave je; <c>-1</c> = nezname.</summary>
        public int CurrentAzimuthBin = -1;

        /// <summary>
        /// Aktualni odklon od svislice [deg]. Nese se proto, ze <b>v mrizce videt neni</b> —
        /// radky se klicuji jen smerem, ne velikosti.
        /// </summary>
        public double CurrentTiltDeg = double.NaN;

        /// <summary>
        /// Rozptyl <c>|B|</c> po korekci <b>samotnou kouli</b> [G] — podle nej se pozna, jestli
        /// se behem mereni menilo pole. Viz <c>MagCalThresholds.MaxSphereSdMagnitudeG</c>.
        /// </summary>
        public double SphereSdMagnitudeG = double.NaN;

        /// <summary>
        /// Dvanact cisel kalibrace <b>jen tvrdeho zeleza</b>; prazdne, dokud se koule neurci.
        /// </summary>
        public string SphereVnwrg23;

        /// <summary>Da se zapsat aspon tvrde zelezo? Slabsi brana nez plna pouzitelnost.</summary>
        public bool CanWriteHardIron;

        /// <summary>Cas posledniho zpracovaneho vzorku (hodiny DAT, ne stroje).</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public MagCalMsg() : base("MagCalMsg", FormatVersion) { }

        /// <inheritdoc/>
        public override Message Build() => new MagCalMsg();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Phase);
            bw.Write(Condition);
            bw.Write(SdMagnitudeG);
            bw.Write(SdInclinationDeg);
            bw.Write(Vnwrg23 ?? string.Empty);
            bw.Write(Verdict ?? string.Empty);
            bw.Write(MissingText ?? string.Empty);
            bw.Write(FilledAzimuthBins);
            bw.Write(TiltGroups);
            bw.Write(TiltedGroups);
            bw.Write(HasOppositeTilts);
            bw.Write(Samples);
            bw.Write(BRefG);
            bw.Write(Reg23Before ?? string.Empty);
            Write(bw, TimeStamp);

            // --- od verze 2 ---
            var g = Grid ?? Array.Empty<int[]>();
            bw.Write(g.Length);
            foreach (var radek in g)
            {
                var r = radek ?? Array.Empty<int>();
                bw.Write(r.Length);
                foreach (int v in r) bw.Write(v);
            }
            bw.Write(CurrentRow);
            bw.Write(CurrentAzimuthBin);
            bw.Write(CurrentTiltDeg);
            bw.Write(SphereSdMagnitudeG);
            bw.Write(SphereVnwrg23 ?? string.Empty);
            bw.Write(CanWriteHardIron);
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            Phase = br.ReadInt32();
            Condition = br.ReadDouble();
            SdMagnitudeG = br.ReadDouble();
            SdInclinationDeg = br.ReadDouble();
            Vnwrg23 = br.ReadString();
            Verdict = br.ReadString();
            MissingText = br.ReadString();
            FilledAzimuthBins = br.ReadInt32();
            TiltGroups = br.ReadInt32();
            TiltedGroups = br.ReadInt32();
            HasOppositeTilts = br.ReadBoolean();
            Samples = br.ReadInt32();
            BRefG = br.ReadDouble();
            Reg23Before = br.ReadString();
            TimeStamp = ReadDateTime(br);

            // Verze 1 (zaznamy z 8.-10. 9. 2026) mrizku nenesla. Necha se PRAZDNA - stranka
            // pak mrizku nekresli, coz je poctivejsi nez ji dopocitat z FilledAzimuthBins
            // a tvarit se, ze vime, kde ty vzorky byly.
            if (Verze < 2) return;

            int radku = br.ReadInt32();
            Grid = new int[radku][];
            for (int i = 0; i < radku; i++)
            {
                int sloupcu = br.ReadInt32();
                Grid[i] = new int[sloupcu];
                for (int j = 0; j < sloupcu; j++) Grid[i][j] = br.ReadInt32();
            }
            CurrentRow = br.ReadInt32();
            CurrentAzimuthBin = br.ReadInt32();
            CurrentTiltDeg = br.ReadDouble();
            SphereSdMagnitudeG = br.ReadDouble();
            SphereVnwrg23 = br.ReadString();
            CanWriteHardIron = br.ReadBoolean();
        }
    }
}

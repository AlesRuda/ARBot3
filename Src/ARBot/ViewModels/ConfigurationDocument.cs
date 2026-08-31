using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ARBot.Common.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>Jeden radek tabulky parametru.</summary>
    public partial class ParamRow : ObservableObject
    {
        /// <summary>Deklarace, ze ktere radek vznikl - drzi se kvuli validaci hodnoty.</summary>
        public ParamDef Def { get; init; }

        public string Name => Def?.Name;
        public string Category => Def?.Category;
        public string Description => Def?.Description;

        /// <summary>Vychozi hodnota k zobrazeni; u DefaultFromCode je to popis, ne hodnota.</summary>
        public string DefaultText => Def == null
            ? null
            : (Def.DefaultFromCode ? Def.DefaultDescription : (Def.Default ?? "(nenastaveno)"));

        /// <summary>
        /// Text bubliny nad celym radkem: popis (sloupec Popis je uzky, takze se do nej dlouhy
        /// popis nevejde) plus typ a vychozi hodnota. <b>Typ nikde jinde videt neni</b> - sloupec
        /// pro nej v tabulce zamerne neni, protoze by ubral misto hodnote, ale pri psani hodnoty
        /// je to prave ta informace, kterou clovek potrebuje.
        /// </summary>
        public string Tooltip
        {
            get
            {
                if (Def == null) return null;
                string hlavicka = $"{Def.Name}  ({Def.Type}, výchozí: {DefaultText})";
                return string.IsNullOrWhiteSpace(Def.Description)
                       ? hlavicka
                       : hlavicka + "\n\n" + Def.Description;
            }
        }

        /// <summary>
        /// Odkud pochazi hodnota, ktera plati - "vychozi" / "profil" / "prikazova radka".
        /// Je to pulka objevitelnosti: "proc to ma tuhle hodnotu" je stejne casta otazka jako
        /// "co to vubec je".
        /// </summary>
        [ObservableProperty] private string origin;

        [ObservableProperty] private string value;

        /// <summary>Neprazdne, kdyz hodnota neprojde validaci podle typu - chybu ma clovek videt
        /// hned, ne az pri pristim startu.</summary>
        [ObservableProperty] private string error = null;

        partial void OnValueChanged(string value)
        {
            if (Def == null) return;        // Value se muze nastavit driv nez Def
            Error = string.IsNullOrEmpty(value) || Def.IsValidValue(value)
                    ? null
                    : $"neni platna hodnota typu {Def.Type}";
        }
    }

    /// <summary>
    /// Dokument „Konfigurace": vypis VSECH parametru z <see cref="ParamRegistry"/> s popisem,
    /// ucinnou hodnotou a jejim puvodem; editace a ulozeni profilu.
    ///
    /// <para><b>Proc je videt cely registr, a ne jen to, co se v tomhle behu precetlo.</b> Pri
    /// <c>mission=robotour</c> se klice FreeRunu vubec neprectou - a prave je clovek hleda, kdyz
    /// chce misi prepnout. Viz doc/configuration.md.</para>
    ///
    /// <para><b>Zmena plati az po restartu.</b> Skoro vsechny parametry se ctou pri konstrukci
    /// runtimu, takze editor je ulozistem hodnot pro pristi start - proto tlacitko
    /// „Ulozit a restartovat".</para>
    /// </summary>
    public partial class ConfigurationDocument : DocumentBase
    {
        public override Type ViewType => typeof(ARBot.Views.ConfigurationDocumentView);

        private readonly List<ParamRow> allRows = new List<ParamRow>();

        /// <summary>Radky po filtru.</summary>
        public ObservableCollection<ParamRow> Rows { get; } = new ObservableCollection<ParamRow>();

        [ObservableProperty] private string filter = string.Empty;

        /// <summary>Cesta, kam se naposledy ukladalo (predvyplnena pro dalsi ulozeni).</summary>
        [ObservableProperty] private string profilePath;

        /// <summary>Posledni hlaska pro uzivatele (ulozeno / chyba).</summary>
        [ObservableProperty] private string status;

        public ConfigurationDocument()
        {
            Id = "Configuration";
            Title = "Konfigurace";

            // Design-time: navrhar nesmi sahat na runtime store ani na soubory.
            if (Avalonia.Controls.Design.IsDesignMode)
            {
                allRows.Add(new ParamRow
                {
                    Def = new ParamDef
                    {
                        Name = "mapcorr", Category = "Fuze a lokalizace", Type = ParamType.Bool,
                        Default = "false",
                        Description = "Zapina korelaci occupancy gridu s mapou.",
                    },
                    Origin = "vychozi",
                    Value = "false",
                });
                ApplyFilter();
                return;
            }

            var store = ParamStore.Current;
            ProfilePath = store.ConfigPath;

            foreach (var def in ParamRegistry.All)
            {
                allRows.Add(new ParamRow
                {
                    Def = def,
                    Origin = OriginText(store.OriginOf(def.Name)),
                    Value = store.Get(def.Name) ?? string.Empty,
                });
            }

            ApplyFilter();
        }

        private static string OriginText(ParamOrigin o) => o switch
        {
            ParamOrigin.File => "profil",
            ParamOrigin.CommandLine => "prikazova radka",
            _ => "vychozi",
        };

        /// <summary>
        /// Nacte profil do tabulky (NIC nespousti - hodnoty zacnou platit az po restartu).
        ///
        /// <para><b>Hodnoty, ktere v profilu nejsou, se vrati na vychozi.</b> Tabulka pak ukazuje
        /// presne to, jak by aplikace s timhle profilem startovala - kdyby se profil jen
        /// "primichal" k soucasnemu stavu, vysledek by neodpovidal zadne skutecne konfiguraci
        /// a ulozeni by zapsalo neco jineho, nez clovek videl.</para>
        ///
        /// <para><b>Sloupec Puvod po nacteni nepopisuje bezici aplikaci</b> - ta jede porad se
        /// starou konfiguraci. Proto se pise „profil (nacteno)", ne „profil“, a stav to rekne
        /// vetou. Bez toho by sloupec tise lhal.</para>
        /// </summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task Load()
        {
            string path = null;
            try
            {
                var top = App.MainTopLevel;
                if (top?.StorageProvider is { } sp)
                {
                    var picks = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                    {
                        Title = "Nacist konfiguracni profil",
                        AllowMultiple = false,
                        FileTypeFilter = new[]
                        {
                            new Avalonia.Platform.Storage.FilePickerFileType("Profil ARBot")
                            {
                                Patterns = new[] { "*.cfg" },
                            },
                            new Avalonia.Platform.Storage.FilePickerFileType("Vše")
                            {
                                Patterns = new[] { "*.*" },
                            },
                        },
                    });
                    if (picks != null && picks.Count > 0)
                        path = picks[0].Path?.LocalPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            // Bez dostupneho dialogu (nebo po jeho zavreni) se pouzije cesta z pole - na zarizeni
            // bez spravce souboru je to jedina cesta, jak profil nacist.
            if (string.IsNullOrWhiteSpace(path))
                path = string.IsNullOrWhiteSpace(ProfilePath) ? null : RepoPaths.Resolve(ProfilePath);
            if (string.IsNullOrWhiteSpace(path))
            {
                Status = "Nacteni zruseno: neni vybrany zadny soubor.";
                return;
            }

            LoadFrom(path);
        }

        /// <summary>Nacte profil z dane cesty do tabulky; vadny profil hlasi a nic nemeni.</summary>
        private void LoadFrom(string path)
        {
            List<KeyValuePair<string, string>> dvojice;
            try
            {
                dvojice = ParamFile.Read(path);
            }
            catch (ParamFileException ex)
            {
                Status = "Profil se nepodarilo nacist: " + ex.Message;
                return;
            }
            catch (Exception ex)
            {
                Status = "Profil se nepodarilo nacist: " + ex.Message;
                return;
            }

            // Validace PRED zapisem do tabulky: vadny profil nesmi nechat tabulku napul prepsanou.
            // Pravidla drzi ParamRegistry.Validate - tentyz kod, jaky pouzije ParamStore.Build pri
            // startu, aby panel nenacetl profil, ktery by aplikace odmitla.
            var vady = ParamRegistry.Validate(dvojice);
            if (vady.Count > 0)
            {
                Status = $"Profil '{path}' se nenacetl - " + string.Join("; ", vady) + ".";
                return;
            }

            var zProfilu = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in dvojice)
                zProfilu[pair.Key] = pair.Value;

            foreach (var r in allRows)
            {
                if (r.Def == null) continue;
                if (zProfilu.TryGetValue(r.Def.Name, out string hodnota))
                {
                    r.Value = hodnota;
                    r.Origin = "profil (nacteno)";
                }
                else
                {
                    r.Value = r.Def.DefaultFromCode ? string.Empty : (r.Def.Default ?? string.Empty);
                    r.Origin = "vychozi";
                }
            }

            ProfilePath = path;
            Status = $"Nacteno z {path} ({zProfilu.Count} parametru). Tabulka ted ukazuje NAVRH "
                     + "pro pristi start - bezici aplikace jede porad se starou konfiguraci. "
                     + "Uplatni ho tlacitkem Ulozit a restartovat.";
        }

        partial void OnFilterChanged(string value) => ApplyFilter();

        private void ApplyFilter()
        {
            Rows.Clear();
            string f = (Filter ?? string.Empty).Trim();
            foreach (var r in allRows)
            {
                if (f.Length > 0
                    && (r.Name ?? string.Empty).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                    && (r.Description ?? string.Empty).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Rows.Add(r);
            }
        }

        /// <summary>
        /// Hodnoty, ktere se maji zapsat: jen ty odlisne od vychoziho stavu. Kratky soubor, ze
        /// ktereho je videt, co se na tomhle behu meni; uplny vycet je uloha panelu, ne profilu.
        /// </summary>
        private Dictionary<string, string> ValuesToWrite()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in allRows)
            {
                if (r.Def == null || string.IsNullOrEmpty(r.Value)) continue;
                if (!r.Def.DefaultFromCode
                    && string.Equals(r.Def.Default ?? string.Empty, r.Value,
                                     StringComparison.OrdinalIgnoreCase))
                    continue;
                result[r.Name] = r.Value;
            }
            return result;
        }

        /// <summary>
        /// Ulozi profil do znameho souboru bez ptani. Kdyz zadny znamy neni (aplikace nebezi
        /// s <c>config=</c> a nic se nenacetlo), chova se jako „Ulozit jako" a zepta se - stejne
        /// jako to dela kazdy editor.
        /// </summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task Save()
        {
            string path = string.IsNullOrWhiteSpace(ProfilePath)
                          ? await PickSavePathAsync()
                          : RepoPaths.Resolve(ProfilePath);
            if (!string.IsNullOrWhiteSpace(path))
                WriteTo(path);
        }

        /// <summary>Vzdy se zepta na cestu a ulozi tam.</summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task SaveAs()
        {
            string path = await PickSavePathAsync();
            if (!string.IsNullOrWhiteSpace(path))
                WriteTo(path);
        }

        /// <summary>
        /// Dialog pro vyber ciloveho souboru; vraci <c>null</c>, kdyz clovek dialog zavrel.
        ///
        /// <para>Bez dostupneho dialogu se pouzije cesta z pole, a kdyz je i to prazdne, vychozi
        /// <c>config/profil.cfg</c> - ulozeni nesmi selhat jen proto, ze prostredi nema spravce
        /// souboru.</para>
        /// </summary>
        private async System.Threading.Tasks.Task<string> PickSavePathAsync()
        {
            try
            {
                var top = App.MainTopLevel;
                if (top?.StorageProvider is { } sp)
                {
                    var file = await sp.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                    {
                        Title = "Ulozit konfiguracni profil",
                        SuggestedFileName = string.IsNullOrWhiteSpace(ProfilePath)
                                            ? "profil.cfg"
                                            : Path.GetFileName(ProfilePath),
                        DefaultExtension = "cfg",
                        FileTypeChoices = new[]
                        {
                            new Avalonia.Platform.Storage.FilePickerFileType("Profil ARBot")
                            {
                                Patterns = new[] { "*.cfg" },
                            },
                        },
                    });

                    string picked = file?.Path?.LocalPath;
                    if (!string.IsNullOrWhiteSpace(picked))
                        return picked;
                    // Dialog byl dostupny a clovek ho zavrel -> ulozeni se ma zrusit, ne spadnout
                    // na nahradni cestu (jinak by „Zrusit" tise nekam zapsalo).
                    Status = "Ulozeni zruseno.";
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            return string.IsNullOrWhiteSpace(ProfilePath)
                   ? RepoPaths.Resolve(Path.Combine("config", "profil.cfg"))
                   : RepoPaths.Resolve(ProfilePath);
        }

        /// <summary>
        /// Vlastni zapis; vraci <c>true</c> pri uspechu. Oddeleny od prikazu proto, aby se
        /// „Ulozit a restartovat" nemuselo rozhodovat podle TEXTU hlasky.
        /// </summary>
        private bool WriteTo(string path)
        {
            var vadne = allRows.Where(r => !string.IsNullOrEmpty(r.Error)).Select(r => r.Name).ToList();
            if (vadne.Count > 0)
            {
                Status = "Neplatne hodnoty: " + string.Join(", ", vadne);
                return false;
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, ParamFile.Format(ValuesToWrite()));
                ProfilePath = path;
                Status = "Ulozeno do " + path;
                return true;
            }
            catch (Exception ex)
            {
                Status = "Ulozeni selhalo: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Ulozi profil a restartuje aplikaci s nim.
        ///
        /// <para><b>Predava se JEN config=, puvodni argumenty se zahodi.</b> Kdyby se prenesly,
        /// prebily by podle precedence prave ulozenou hodnotu a tlacitko by nedelalo, co slibuje.
        /// Bezpecne to je proto, ze se do profilu ukladaji UCINNE hodnoty (tedy i to, co prislo
        /// z prikazove radky) - nic se ztratit nemuze.</para>
        ///
        /// <para><b>Past se systemd:</b> pod sluzbou s <c>Restart=always</c> by spusteni vlastni
        /// kopie vyrobilo DVE instance. Promennou <c>INVOCATION_ID</c> nastavuje systemd kazde
        /// jednotce, takze podle ni se pozna, ze staci skoncit a restart nechat na nem. K 31. 8.
        /// 2026 zadna jednotka aplikace neexistuje (na Pi se spousti rucne), takze tahle vetev
        /// zatim nikdy nenastane - je to obrana do budoucna. Viz doc/configuration.md.</para>
        /// </summary>
        [RelayCommand]
        private async System.Threading.Tasks.Task SaveAndRestart()
        {
            // Stejne pravidlo jako u „Ulozit": znamou cestu pouzij, jinak se zeptej.
            string path = string.IsNullOrWhiteSpace(ProfilePath)
                          ? await PickSavePathAsync()
                          : RepoPaths.Resolve(ProfilePath);
            if (string.IsNullOrWhiteSpace(path) || !WriteTo(path))
                return;

            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                Status = "Restart neni mozny: neznam cestu k vlastnimu procesu.";
                return;
            }

            bool podSystemd = !string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable("INVOCATION_ID"));

            try
            {
                if (!podSystemd)
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(exe)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(exe),
                    };
                    psi.ArgumentList.Add("config=" + ProfilePath);
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Status = "Restart selhal: " + ex.Message;
                return;
            }

            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                life.Shutdown();
        }
    }
}

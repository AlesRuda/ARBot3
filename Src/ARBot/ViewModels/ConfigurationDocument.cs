using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ARBot.Common.Configuration;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Jeden radek tabulky parametru. <b>Neinstanciuje se primo</b> - vzdy vznikne jako
    /// <see cref="ChoiceParamRow"/> nebo <see cref="TextParamRow"/> pres <see cref="Create"/>.
    ///
    /// <para><b>Proc dva typy - a je to oprava VADY, ne uklid.</b> Prvni verze mela v bunce OBA
    /// prvky nad sebou (ComboBox + TextBox) a prepinala je pres <c>IsVisible</c>. Oba byly
    /// obousmerne navazane na tutez <c>Value</c>. Kdyz DataGrid pri virtualizaci RECYKLOVAL
    /// kontejner z radku S vyctem na radek BEZ nej, dostal skryty ComboBox
    /// <c>ItemsSource = null</c>, v prazdnem seznamu svou hodnotu nenasel, nastavil
    /// <c>SelectedItem = null</c> - a obousmerny binding to zapsal ZPATKY do <c>Value</c>.
    /// Hodnota se tim skutecne ztratila (ne jen prestala byt videt): ulozeny profil by ji uz
    /// neobsahoval.</para>
    ///
    /// <para>Rozdelenim na dva typy vybira sablonu Avalonia podle typu dat, takze v bunce je vzdy
    /// PRAVE JEDEN prvek, ComboBox nikdy nedostane prazdny seznam a neni co prepinat.
    /// Nalezeno 31. 8. 2026; reprodukce: NEmaximalizovane okno + scroll na dotcenty radek
    /// (v maximalizovanem se recyklace nekona a vada se neprojevi). Viz doc/configuration.md.</para>
    /// </summary>
    public partial class ParamRow : ObservableObject, System.ComponentModel.INotifyDataErrorInfo
    {
        /// <summary>Vyrobi radek spravneho typu podle toho, jestli ma parametr uplny vycet hodnot.</summary>
        public static ParamRow Create(ParamDef def, string origin, string value)
        {
            bool vycet = def?.AllowedValues != null && def.AllowedValues.Length > 0;
            ParamRow row = vycet ? new ChoiceParamRow { Def = def } : new TextParamRow { Def = def };
            row.Origin = origin;
            row.Value = value;
            return row;
        }

        /// <summary>Deklarace, ze ktere radek vznikl - drzi se kvuli validaci hodnoty.</summary>
        public ParamDef Def { get; init; }

        public string Name => Def?.Name;
        public string Category => Def?.Category;
        public string Description => Def?.Description;

        /// <summary>Vychozi hodnota k zobrazeni (default z kodu - Profile, konfiguracni tridy - je od
        /// 4. 9. 2026 v registru jako skutecna hodnota, takze zvlastni popis neni potreba).</summary>
        public string DefaultText => Def == null ? null : (Def.Default ?? "(nenastaveno)");

        /// <summary>Povolene hodnoty pro rozbalovaci seznam; u <see cref="TextParamRow"/> null.</summary>
        public string[] Choices => Def?.AllowedValues;

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

        /// <summary>Duvod odmitnuti hodnoty, nebo <c>null</c>. Drzi se kvuli
        /// <see cref="GetErrors"/> a kvuli kontrole pred ulozenim.</summary>
        private string error;

        /// <summary>Je hodnota vadna? (<see cref="INotifyDataErrorInfo"/>)</summary>
        public bool HasErrors => error != null;

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        /// <summary>
        /// Chyby vlastnosti pro <see cref="INotifyDataErrorInfo"/>. Hlasi se jen u
        /// <see cref="Value"/> - jen ona se edituje.
        ///
        /// <para>Odtud si to bere Avalonia: pole dostane pseudotridu <c>:error</c> a
        /// <c>DataValidationErrors.Errors</c>, a Styles/Validation.axaml z toho udela cerveny
        /// ramecek s bublinou. Panel uz na chybu nema vlastni sloupec.</para>
        /// </summary>
        public System.Collections.IEnumerable GetErrors(string propertyName)
        {
            if (error == null) return Array.Empty<string>();
            if (propertyName != null && propertyName != nameof(Value)) return Array.Empty<string>();
            return new[] { error };
        }

        partial void OnValueChanged(string value)
        {
            if (Def == null) return;        // Value se muze nastavit driv nez Def

            // Duvod odmitnuti bere z ParamDef.Validate, at bublina rekne, co se cekalo
            // („cekam dve cisla oddelena carkou (vlevo,vpravo)"), ne jen ze je hodnota spatne.
            string novaChyba = string.IsNullOrEmpty(value)
                               ? null
                               : (Def.Validate(value) is { Ok: false } v ? v.Error : null);

            if (novaChyba == error) return;
            error = novaChyba;
            OnPropertyChanged(nameof(HasErrors));
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Value)));
        }
    }

    /// <summary>Radek parametru s uplnym vyctem hodnot - v bunce dostane rozbalovaci seznam.</summary>
    public sealed class ChoiceParamRow : ParamRow { }

    /// <summary>Radek parametru bez vyctu - v bunce dostane textove pole.</summary>
    public sealed class TextParamRow : ParamRow { }

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
                allRows.Add(ParamRow.Create(
                    new ParamDef
                    {
                        Name = "mapcorr", Category = "Fuze a lokalizace", Type = ParamType.Bool,
                        Default = "false",
                        Description = "Zapina korelaci occupancy gridu s mapou.",
                    },
                    "vychozi", "false"));
                ApplyFilter();
                return;
            }

            var store = ParamStore.Current;
            ProfilePath = store.ConfigPath;

            foreach (var def in ParamRegistry.All)
            {
                allRows.Add(ParamRow.Create(
                    def,
                    OriginText(store.OriginOf(def.Name)),
                    // Canonical: hodnota z profilu smi mit jinou velikost pismen (validace je
                    // case-insensitive), ale rozbalovaci seznam porovnava presne.
                    def.Canonical(store.Get(def.Name)) ?? string.Empty));
            }

            ApplyFilter();
        }

        private static string OriginText(ParamOrigin o) => o switch
        {
            ParamOrigin.File => "profil",
            ParamOrigin.CommandLine => "prikazova radka",
            ParamOrigin.Runtime => "zvoleno za behu",
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
                    r.Value = r.Def.Canonical(hodnota);
                    r.Origin = "profil (nacteno)";
                }
                else
                {
                    r.Value = r.Def.Default ?? string.Empty;
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
                if (string.Equals(r.Def.Default ?? string.Empty, r.Value,
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
            var vadne = allRows.Where(r => r.HasErrors).Select(r => r.Name).ToList();
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

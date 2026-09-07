# models/testset/ — testovací sada s ground truth

Pevná **50snímková** sada s ručně označenou pravdou, proti které se měří kvalita sjízdnosti
z RGB. Je to **tatáž sada**, na které vznikla čísla v trénovacím notebooku (Model61.1 = 0,9546
per-pixel proti ~0,80 histogramu), takže naše výsledky jsou s nimi srovnatelné.

**Proč v repu:** bez pravdy se dá měřit jen *shoda dvou metod*, a ta neřekne, která se mýlí —
každá změna modelu by byla hádání. Podrobně: [doc/semantic-segmentation.md](../../doc/semantic-segmentation.md).

## Jak ji sem dostat

Spustit [`Src/Colab/ExportTestSet.ipynb`](../../Src/Colab/ExportTestSet.ipynb) v Colabu
(potřebuje `LABELBOX_API_KEY` v Colab Secrets) a stažený `testset.zip` rozbalit sem.

## Jak ji použít

```bash
ARBot.Analyze backproject --truth=models/testset
```

Vypíše přesnost per-pixel, IoU a precision/recall pro **síť i histogram**, per snímek i celkově,
a s `--png=<prefix>` uloží srovnání nejhoršího snímku (vstup | pravda | histogram | síť).

## Formát (na tom závisí čtení)

| | |
|---|---|
| `img/<id>.jpg` | snímek, JPEG s **výchozí kvalitou PIL** — schválně stejně jako `ds_train` v notebooku, aby se měřilo na týchž datech, na kterých vznikla původní čísla |
| `gt/<id>.png` | maska, **0 = nesjízdno, 255 = sjízdno**, stejný rozměr jako snímek |
| `manifest.csv` | `name,width,height,road_fraction` — kontrola, že export je úplný, a rozlišení snímků (dosud se jen předpokládalo 640×480) |

⚠️ **Maska je 0/255, ne 0/1**, ačkoli notebook ukládá do `ds_train/gt` indexy 0/1. Důvod: masku
jde otevřít a očima zkontrolovat; při 0/1 vypadá černá a neúplný export by se poznal až podle
nesmyslných čísel. Notebooková cesta `datasetFromFilenames3` už dělí 255, takže je s tím
kompatibilní. Čtení v `ARBot.Analyze` prahuje na 128 jako všude jinde.

**Pravda je „přesně bílá"** — maska vzniká testem na barvu `(255,255,255,255)`, stejně jako
`LabelIndexImage` v notebooku. Volnější práh by dal jinou pravdu, než na které se měřilo.

## Vlastnosti sady, na které je potřeba myslet

- **Průměrný podíl sjízdné plochy je 68,7 %** (medián 82,8 %, 54 % snímků nad 80 %). To je
  zároveň **přesnost triviální odpovědi „všechno je cesta"** — bez téhle hranice se každé číslo
  přesnosti přečte příliš optimisticky.
- **Snímky jsou všechny 640×480** (dosud se to jen předpokládalo, teď je to změřené).
- **Krajní případy jsou v sadě záměrně:** jeden snímek má pravdu 0 % (listnatý lesní podklad,
  kde není cesta — a histogram na něm tvrdí 93 %) a jeden 100 % (celý snímek cesta). Nejsou to
  rozbité masky.
- **U pěti labelů LabelBox při exportu zahodil anotaci** nástrojem, který dnes není v ontologii
  projektu (`SchemaInconsistencyException`). Maska cesty je nedotčená — `Road` v ontologii je —
  takže na ground truth to vliv nemá. Ontologii proto **není potřeba opravovat**.

## Původ

LabelBox projekt `ck2iqtrixgi6z08113jpu9imr`, anotace `Road`. Snímky jsou z **kamery ARBot2,
roky 2019–2022** — tedy **ne** z dnešní D435. To je zároveň hranice toho, co tahle sada dokáže
říct: měří kvalitu modelu na datech, na kterých se učil, ne na tom, co robot vidí dnes.

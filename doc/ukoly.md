# Úkoly projektu — registr

<!-- GENEROVÁNO z doc/ukoly.yaml nástrojem tools/ukoly.cs — needitovat ručně. -->

**Generováno** z [ukoly.yaml](ukoly.yaml) příkazem `dotnet run tools/ukoly.cs`; ruční úpravy
přepíše další běh. Pravidla a schéma: [plan-ukoly.md](plan-ukoly.md). Totéž pro web:
[web/pages/historie.html](../web/pages/historie.html).

Témat celkem **210**: otevřeno **44** · v kódu, na HW neověřeno **45** · hotovo **112** · odloženo **6** · zamítnuto **3**.

## Otevřené a v kódu (kde jsme)

| stav | oblast | téma | nalezeno | čeká na |
|---|---|---|---|---|
| otevřeno | Vidění | [Prahy klasifikace a šumový model gridu sjízdnosti nejsou laděné na reálných datech](#vid-grid-prahy-realna-data) | 30. 7. 2026 | [vid-polarni-grid-sjizdnosti](#vid-polarni-grid-sjizdnosti) |
| otevřeno | Navigace po mapě | [Odkud se berou a jak se aktualizují výřezy `.osm`](#nav-zdroj-osm-dat) | 4. 8. 2026 |  |
| otevřeno | Lokální mapa a plánování | [Výkon řetězu hloubka → grid → EDT → A* na ARM není změřený](#lp-vykon-retezu-na-arm) | 10. 8. 2026 |  |
| otevřeno | Lokalizace a fúze senzorů | [Náklon robota jde mimo fúzi a nezná svůj zdroj](#lok-ekf-pitch-roll-stav) | 11. 8. 2026 |  |
| otevřeno | Lokální mapa a plánování | [Koridor trasy jako měkká cena v lokálním A*](#lp-koridor-trasy-jako-cena) | 12. 8. 2026 |  |
| otevřeno | Mise | [Vizuální dojezd posledních metrů podle QR kódu](#mise-vizualni-dojezd-na-cil) | 12. 8. 2026 |  |
| otevřeno | Navigace po mapě | [Uzavřené hrany sítě a stav lokalizace nepřežijí restart](#nav-uzavreni-hran-pres-restart) | 12. 8. 2026 |  |
| otevřeno | Navigace po mapě | [Detektor přehrazení bez průřezu koridorem (fáze 4b)](#nav-prurez-koridorem) | 13. 8. 2026 |  |
| otevřeno | Vidění | [Okluzní pravidlo zahazuje většinu barevných vzorků](#vid-inshadow-zahazuje-vzorky) | 14. 8. 2026 |  |
| otevřeno | Lokalizace a fúze senzorů | [Určená osa korelace je vychýlená o 6°](#lok-tight-axis-angle-vychylena) | 19. 8. 2026 |  |
| otevřeno | Lokalizace a fúze senzorů | [Eskalace stavu „lokalizace nepodložená mapou" a znovunalezení po ztrátě](#lok-korelace-eskalace-bez-shody) | 20. 8. 2026 |  |
| otevřeno | Lokalizace a fúze senzorů | [Tři podmínky, než korekce z mapy pustit naostro](#lok-korelace-tri-podminky-naostro) | 20. 8. 2026 | [lok-kurz-koridor-prehlasovan-kompasem](#lok-kurz-koridor-prehlasovan-kompasem), [lok-kompas-sigma-podlaha](#lok-kompas-sigma-podlaha) |
| otevřeno | Nástroje, záznam a analýza | [Hlášky ze startu runtime se do záznamu nedostanou](#nast-hlasky-startu-do-zaznamu) | 20. 8. 2026 |  |
| otevřeno | Vidění | [Chybná kalibrace kamer je bias, který lokalizace integruje](#vid-kalibrace-kamer-bias) | 20. 8. 2026 |  |
| otevřeno | Lokalizace a fúze senzorů | [RANSAC je nedeterministický, replay hranové lokalizace není reprodukovatelný](#lok-ransac-nedeterministicky) | 23. 8. 2026 |  |
| otevřeno | Lokalizace a fúze senzorů | [Chyby senzorů (bias kompasu a gyra) jako stavy EKF](#lok-bias-senzoru-jako-stav-ekf) | 25. 8. 2026 | [lok-gps-kurz-do-fuze](#lok-gps-kurz-do-fuze), [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) |
| otevřeno | Navigace po mapě | [Délka trasy k cíli nepočítala poslední úsek](#nav-delka-trasy) | 26. 8. 2026 |  |
| otevřeno | Hardware a senzory | [Kamera D435 se za provozu odmlčí](#hw-d435-vypadky-za-provozu) | 31. 8. 2026 | [hw-vetev-usb-2-1-3](#hw-vetev-usb-2-1-3) |
| otevřeno | Nástroje, záznam a analýza | [Headless testy UI v Avalonii — ověřeno spikem, nezavedeno](#nast-avalonia-headless-testy) | 1. 9. 2026 |  |
| otevřeno | Provoz na zařízení | [Měření výkonu řízení — stíhá řídicí smyčka svou periodu?](#prov-perf-monitoring) | 1. 9. 2026 |  |
| otevřeno | Provoz na zařízení | [Řídicí smyčka na Windows zamešká 3–4 takty za sekundu, ačkoli práce trvá pod 1 ms](#prov-zameskane-takty-windows) | 1. 9. 2026 |  |
| otevřeno | Hardware a senzory | [Kamery se po bootu vyčetly jen na USB 2.0 a robot byl bez vidění](#hw-kamery-usb2-po-bootu) | 2. 9. 2026 |  |
| otevřeno | Provoz na zařízení | [Start služby po skutečném rebootu Orange Pi se nezkoušel](#prov-start-po-rebootu) | 5. 9. 2026 | [hw-kamery-usb2-po-bootu](#hw-kamery-usb2-po-bootu) |
| otevřeno | Hardware a senzory | [Když se kamera nedá znovu vyčíst, proces roste v paměti](#hw-d435-query-pamet) | 6. 9. 2026 |  |
| otevřeno | Hardware a senzory | [Akcelerometr VN100 měří o 7 % víc než g](#hw-vn100-akcelerometr-7pct) | 6. 9. 2026 |  |
| otevřeno | Lokalizace a fúze senzorů | [Chyba GPS fixu je korelovaná ~40 s, filtr ji bere jako nezávislou](#lok-gps-casova-korelace) | 6. 9. 2026 | [lok-bias-senzoru-jako-stav-ekf](#lok-bias-senzoru-jako-stav-ekf) |
| otevřeno | Vidění | [Dopad výpočtu ve 128×128 na hustotu dat pro grid a hranice cesty](#vid-segmentace-rozliseni-128) | 6. 9. 2026 |  |
| otevřeno | Vidění | [Lepší model Model96.2 dává 96,7 %, ale půlí snímkovou frekvenci](#vid-model96-2-na-npu) | 7. 9. 2026 |  |
| otevřeno | Vidění | [Kvalita segmentační sítě na dnešních snímcích D435 je bez ground truth neznámá](#vid-segmentace-pravda-d435) | 7. 9. 2026 |  |
| otevřeno | Vidění | [Trénink segmentace se dnes nedá zopakovat jedním kliknutím](#vid-trenink-nejde-zopakovat) | 7. 9. 2026 |  |
| otevřeno | Vidění | [U Model96.2 chybí float checkpoint lepších vah, optimalizace je nevyužitelná](#vid-model96-float-checkpoint) | 9. 9. 2026 |  |
| otevřeno | Hardware a senzory | [Po kalibraci zbývá konstantní posun kurzu −3,7°, který nejde rozložit](#hw-kurz-zbytek-konstanta) | 12. 9. 2026 | [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) |
| otevřeno | Lokalizace a fúze senzorů | [Chyba kurzu z GPS není bílý šum a GPS běží 10 Hz, ne 5](#lok-gps-kurz-korelovana-chyba) | 12. 9. 2026 | [lok-bias-senzoru-jako-stav-ekf](#lok-bias-senzoru-jako-stav-ekf) |
| otevřeno | Hardware a senzory | [Odpojená T265: runtime ji dál hledá a zahlcuje journal](#hw-t265-odpojena-natrvalo) | 13. 9. 2026 |  |
| otevřeno | Hardware a senzory | [Tvrdé záseky kamer na větvi USB `2-1.3`](#hw-vetev-usb-2-1-3) | 13. 9. 2026 |  |
| otevřeno | Web a dokumentace | [Web arbot.cz převeden z Google Sites na GitHub Pages](#web-arbot-cz-github-pages) | 13. 9. 2026 |  |
| otevřeno | Provoz na zařízení | [Nativní pád (SIGSEGV) při zastavování runtime je častý](#prov-sigsegv-pri-stop) | 14. 9. 2026 |  |
| otevřeno | Hardware a senzory | [`GPSState.FixTime` je nesmysl — ovladač u-bloxu skládá ITOW špatně](#hw-gps-fixtime-rozbity) | 17. 9. 2026 |  |
| otevřeno | Lokalizace a fúze senzorů | [`MinInliers=25` škrtí koridor 1,4–4× a proti čemu vznikl, to nechytá](#lok-koridor-prah-inlieru-prisny) | 17. 9. 2026 |  |
| otevřeno | Mise | [Kalibrace magnetometru se po zápisu sama znehodnotí — kolektor sbírá dál](#mise-magcal-sber-po-zapisu) | 17. 9. 2026 |  |
| otevřeno | Provoz na zařízení | [Runtime zatuhl 4 s po odjezdu mise Track a hlídač ho nechytil](#prov-zatuhnuti-za-behu-mise) | 17. 9. 2026 |  |
| otevřeno | Provoz na zařízení | [V terénu není poznat, jestli se běh nahrává a kam](#prov-zaznam-nevidet-ze-nebezi) | 17. 9. 2026 |  |
| otevřeno | Lokální mapa a plánování | [Tabulky v `path-following.md` počítají se starými limity (0,8 m/s a 0,2 m/s²)](#lp-path-following-stara-cisla) | 18. 9. 2026 |  |
| otevřeno | Lokální mapa a plánování | [Robot 18. 9. dvakrát stál minuty před blokovanou lokální mapou — popsané, ne vysvětlené](#lp-zasek-v-blokovane-mape) | 18. 9. 2026 |  |
| v kódu, na HW neověřeno | Hardware a senzory | [Driver NeoPixel (WS2812) přes SPI na Armbianu](#hw-neopixel-armbian) | 7. 7. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Mise Robotour jako stavový automat s QR kódy](#mise-robotour) | 11. 8. 2026 | [nav-globalni-navigace-runtime](#nav-globalni-navigace-runtime), [mise-nouzove-zastaveni-controlloop](#mise-nouzove-zastaveni-controlloop) |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [Robot by zatáčel dvakrát rychleji, než regulátor chce](#lp-omega-dif-faktor-a-znamenko) | 12. 8. 2026 |  |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [Robot jel 0,1 m/s, i když bylo povoleno 1,2 m/s](#lp-regulator-zapadka-lookahead) | 14. 8. 2026 |  |
| v kódu, na HW neověřeno | Hardware a senzory | [Nulové nebo záporné zrychlení motorů prošlo do řadiče](#hw-pojistka-zrychleni-motoru) | 18. 8. 2026 |  |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [Únik z blokované buňky pod robotem](#lp-unik-z-blokovane-bunky) | 18. 8. 2026 |  |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Korelace occupancy gridu s mapou jako oprava polohy a kurzu](#lok-korelace-gridu-s-mapou) | 19. 8. 2026 | [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Sigma korelace je slepá k množství důkazu](#lok-korelace-sigma-nepoctiva) | 19. 8. 2026 |  |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Lokalizace z hran cesty místo z plochy](#lok-koridor-hranova-lokalizace) | 21. 8. 2026 | [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) |
| v kódu, na HW neověřeno | Vidění | [Zpětná projekce pixelu ignorovala hloubku](#vid-zpetna-projekce-hloubka) | 21. 8. 2026 |  |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Hranová lokalizace za běhu zahazovala 77 % korekcí a hlásila měření i metr mimo cestu](#lok-koridor-gating-a-gate-mimo-koridor) | 22. 8. 2026 | [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Kompas si věří 60–90× víc, než jaký je](#lok-kompas-sigma-podlaha) | 25. 8. 2026 | [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Cykly korelace s mapou nejsou nezávislé — odstup je dekorelační čas 3 s](#lok-korelace-dekorelacni-cas) | 25. 8. 2026 | [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Tvrdý gate korekcí z mapy zahazoval právě ty korekce, které byly potřeba](#lok-mapcorr-tvrdy-gate) | 25. 8. 2026 | [lok-kompas-sigma-podlaha](#lok-kompas-sigma-podlaha) |
| v kódu, na HW neověřeno | Mise | [Zkouška dosažitelnosti cíle z QR kódu nebyla důvěryhodná](#mise-cil-dosazitelnost) | 26. 8. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Čtení QR kódů z kamery (ZXing.Net místo ZBaru)](#mise-qr-cteni) | 26. 8. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Mise by se v depu nezarmovala nikdy — práh rozptylu fixů byl pod šumem GPS](#mise-robotour-armovani-rozptyl) | 26. 8. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Mise Robotour běží bez operátora — potvrzování cíle zrušeno](#mise-robotour-bez-operatora) | 26. 8. 2026 |  |
| v kódu, na HW neověřeno | Hardware a senzory | [Chybový rámec motorového driveru se tvářil jako měření](#hw-motor-chybovy-ramec) | 27. 8. 2026 |  |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [První FreeRun na železe ve stísněném prostoru skončil nárazem](#lp-freerun-stisnene-podminky) | 2. 9. 2026 | [lp-cil-astar-zona](#lp-cil-astar-zona) |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [Rychlostní obálka lokálního plánovače v přímé jízdě vůbec neřídila](#lp-rychlostni-obalka-neridila) | 2. 9. 2026 | [lp-robot-se-plazi-vyhlazovani](#lp-robot-se-plazi-vyhlazovani) |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [Robot se venku plazil rychlostí 0,05 m/s — může za to vyhlazování dráhy](#lp-robot-se-plazi-vyhlazovani) | 7. 9. 2026 | [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) |
| v kódu, na HW neověřeno | Mise | [Mise Track — objezd míst ze souboru](#mise-track) | 8. 9. 2026 |  |
| v kódu, na HW neověřeno | Vidění | [Pravděpodobnost cesty 128×128 se kreslila jen přes střed snímku](#vid-prob-overlay-128) | 10. 9. 2026 |  |
| v kódu, na HW neověřeno | Hardware a senzory | [„Surové" pole magnetometru je kompenzované, druhá kalibrace by tu první přepsala](#hw-magcal-uncompmag-kompenzovany) | 11. 9. 2026 | [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) |
| v kódu, na HW neověřeno | Nástroje, záznam a analýza | [Panel Konfigurace tiše mazal z profilu klíče shodné s defaultem](#nast-panel-konfigurace-mazal-klice) | 12. 9. 2026 |  |
| v kódu, na HW neověřeno | Provoz na zařízení | [Půdorys náhledu ukazuje, co se robot chystá udělat, a zóny k dosažení](#prov-pudorys-umysl-a-zony) | 12. 9. 2026 |  |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [Řídicí smyčka umí držené zastavení (StopHold)](#lp-drzene-zastaveni-stophold) | 13. 9. 2026 |  |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [Klín mezi zornými poli barevných kamer brzdí robota](#lp-klin-mezi-zornymi-poli) | 13. 9. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Mise Track přichycuje všechna místa na síť předem, při odjezdu](#mise-track-prichyceni-predem) | 13. 9. 2026 |  |
| v kódu, na HW neověřeno | Lokální mapa a plánování | [Cíl lokálního plánovače je zóna, ne jediná buňka](#lp-cil-astar-zona) | 14. 9. 2026 | [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) |
| v kódu, na HW neověřeno | Provoz na zařízení | [Služba se po pěti restartech v pěti minutách vzdá a robot je mrtvý](#prov-start-limit-sluzba) | 14. 9. 2026 |  |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Koridor v měřicím režimu — odtlumení a první měřicí jízda](#lok-koridor-merici-rezim) | 15. 9. 2026 | [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Naučená šířka cesty jde dál do mapy — korelaci i kreslení](#lok-naucena-sirka-do-mapy) | 15. 9. 2026 | [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Šířková brána koridoru se ptala dřív, než se bylo z čeho učit](#lok-sirka-odhad-bez-brany) | 15. 9. 2026 |  |
| v kódu, na HW neověřeno | Provoz na zařízení | [Externí audit — bezpečnostní vrstva řízení měla čtyři díry](#prov-audit-bezpecnost-rizeni) | 15. 9. 2026 |  |
| v kódu, na HW neověřeno | Provoz na zařízení | [Externí audit, druhá dávka — tichý senzor, zatuhlý Stop, razítka kamer, CI, licence](#prov-audit-druha-davka) | 15. 9. 2026 |  |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Polovina cyklů koridoru se párovala na příčnou ulici](#lok-prirazeni-hrany-chi2) | 16. 9. 2026 | [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) |
| v kódu, na HW neověřeno | Provoz na zařízení | [Deadlock mezi zámkem mise a zámkem stránky při volbě mise](#prov-deadlock-mise-webstatus) | 17. 9. 2026 |  |
| v kódu, na HW neověřeno | Lokalizace a fúze senzorů | [Příčná brána koridoru zahazovala 81,8 % cyklů — zrušena bez náhrady](#lok-koridor-pricna-brana) | 18. 9. 2026 | [lok-prirazeni-hrany-chi2](#lok-prirazeni-hrany-chi2) |
| v kódu, na HW neověřeno | Navigace po mapě | [Mapa z JOSM nese smazané cesty (`action='delete'`) a čtečka je brala jako živé](#nav-osm-josm-action-delete) | 18. 9. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Skener QR na robotu nedostal jediný snímek — jméno kamery „Right" vs. „Right 740112071021"](#mise-qr-jmeno-kamery) | 19. 9. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Změna pravidel Robotour 2026 — po vykládce další nakládka místo jízdy do depa](#mise-robotour-dalsi-nakladka) | 19. 9. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Mise se v depu nezarmovala — práh HDOP 2,0 mezi budovami nesplnitelný](#mise-robotour-depothdop) | 19. 9. 2026 |  |
| v kódu, na HW neověřeno | Mise | [Kód se četl a mise ho zamítala „nevede trasa“ — robot stál na náměstí spojeném se sítí jen schody](#mise-robotour-mapa-ostrov) | 19. 9. 2026 |  |
| odloženo | Nástroje, záznam a analýza | [Režim Simulate — věrný přepočet běhu nad záznamem](#nast-rezim-simulate) | 27. 7. 2026 |  |
| odloženo | Navigace po mapě | [Recovery manévr při záseku](#nav-recovery-manevr) | 13. 8. 2026 |  |
| odloženo | Lokalizace a fúze senzorů | [Posun mapa–GPS jako stav filtru](#lok-korelace-posun-jako-stav-ekf) | 20. 8. 2026 |  |
| odloženo | Nástroje, záznam a analýza | [Vrstva hranic občas shodí Mapsui při přehrávání](#nast-mapsui-pad-vrstvy-hranic) | 23. 8. 2026 |  |
| odloženo | Lokální mapa a plánování | [Izolované skvrny `Blocked` do 4 buněk brzdí robota jako zeď](#lp-filtr-izolovanych-bunek) | 7. 9. 2026 | [lp-robot-se-plazi-vyhlazovani](#lp-robot-se-plazi-vyhlazovani), [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) |
| odloženo | Lokalizace a fúze senzorů | [Fúze extrapoluje bez omezení — ztráta GPS i IMU robota nezastaví](#lok-fuze-extrapoluje-bez-omezeni) | 15. 9. 2026 |  |

## Lokalizace a fúze senzorů

<a id="lok-ekf-pitch-roll-stav"></a>
### ⬜ Náklon robota jde mimo fúzi a nezná svůj zdroj

`lok-ekf-pitch-roll-stav` · vada · **otevřeno** · nalezeno 11. 8. 2026

Řídicí smyčka bere roll a pitch z posledního došlého IMU vzorku, který nenese identitu zdroje - při dvou IMU (VN100 a T265) může náklon mezi tiky přeskakovat mezi čidly s jinou montáží a kvalitou. Obchází to fúzi: bez gatingu, bez kovariance a bez dopředikování do času tiku, zatímco zbytek stavu robota fúzovaný je. Návrh je přidat náklon do stavového vektoru EKF jako regulérní měření; je to zásah do filtru a zatím se neudělal.

[ekf-fusion.md](ekf-fusion.md), [imu-and-frames.md](imu-and-frames.md) · DevLog [2026-08-11](devlog.md#2026-08-11)

<a id="lok-tight-axis-angle-vychylena"></a>
### ⬜ Určená osa korelace je vychýlená o 6°

`lok-tight-axis-angle-vychylena` · vada · **otevřeno** · nalezeno 19. 8. 2026

Směr lépe určené osy (`TightAxisAngle`) vychází soustavně o −6,3° vedle kolmice na cestu, protože fit kvadratiky na skóre tvaru „stan" ho ohýbá. Kdo podle ní rozkládá hlášený posun, dostane u velké podélné nejednoznačnosti o 40 % víc; rozklad se proto převedl na kurz robota. Vada sama zůstává nedotčená.

[map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-08-19](devlog.md#2026-08-19), [2026-08-25](devlog.md#2026-08-25)

<a id="lok-korelace-eskalace-bez-shody"></a>
### ⬜ Eskalace stavu „lokalizace nepodložená mapou" a znovunalezení po ztrátě

`lok-korelace-eskalace-bez-shody` · záměr · **otevřeno** · nalezeno 20. 8. 2026

Když korelace occupancy gridu s mapou shodu nenajde, korelátor jen mlčí — stav „lokalizace nepodložená mapou" si nikdo nečte a navigace věří mrkvi dál stejně. Chybí i schopnost se znovu najít: záchytný rozsah skenu je jen ±2,5 m a ±8°, takže po delším výpadku GNSS nebo po přenesení robota hierarchický sken principiálně nedosáhne. Kandidát na hrubý inicializátor s širokým záběrem je Fourier–Mellinova transformace (jeden výstřel, nízká přesnost, sken to dojemní) — jako náhrada skenu byla 20. 8. 2026 zamítnuta, jako inicializátor sedí. Až bude z dat vidět, jak dlouhé úseky bez shody v praxi vznikají, může `GlobalNavigator` ubrat nebo mrkvi přestat věřit.

- [ ] Změřit z dat, jak dlouhé úseky bez shody v praxi vznikají
- [ ] Eskalace stavu do `GlobalNavigator` (ubrat, přestat věřit mrkvi)
- [ ] Hrubý inicializátor pro široký záběr (kandidát Fourier–Mellin)

[map-correlation-localization.md](map-correlation-localization.md), [MapCorrelator.cs](../Src/ARBot.Common/Localization/MapCorrelator.cs), [GlobalNavigator.cs](../Src/ARBot.Common/Maps/OsmNav/Navigation/GlobalNavigator.cs) · DevLog [2026-08-20](devlog.md#2026-08-20)

<a id="lok-korelace-tri-podminky-naostro"></a>
### ⬜ Tři podmínky, než korekce z mapy pustit naostro

`lok-korelace-tri-podminky-naostro` · záměr · **otevřeno** · nalezeno 20. 8. 2026

Korelace s naměřenou sigmou 0,1 m přehlasuje GPS zhruba 400 : 1, takže záchyt na souběžné cestě by unesl pózu a nikdo by to nezastavil. Než se korekce pustí do řízení, musí platit tři věci: honestní sigma, rychlostní limit na aplikovanou korekci a strop na nesouhlas s GPS; k tomu měkký gating místo tvrdého zamítání. První podmínka je od 25. 8. splněná, druhá nemá naměřenou naléhavost, třetí je naměřeně nutná a chybí.

- [x] Podmínka 1 — honestní sigma (25. 8. 2026)
- [x] `GateMode.Soft` místo `Reject` (tvrdý gate zahazoval právě potřebné korekce) (25. 8. 2026)
- [ ] Podmínka 2 — rychlostní limit na aplikovanou korekci (proměřit v běhu bez GPS)
- [ ] Podmínka 3 — strop na kumulovaný nesouhlas s GPS

čeká na [lok-kurz-koridor-prehlasovan-kompasem](#lok-kurz-koridor-prehlasovan-kompasem), [lok-kompas-sigma-podlaha](#lok-kompas-sigma-podlaha) · [rozhodnutí 20. 8. 2026](decisions.md), [map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-08-20](devlog.md#2026-08-20), [2026-08-25](devlog.md#2026-08-25)

<a id="lok-ransac-nedeterministicky"></a>
### ⬜ RANSAC je nedeterministický, replay hranové lokalizace není reprodukovatelný

`lok-ransac-nedeterministicky` · vada · **otevřeno** · nalezeno 23. 8. 2026

`RANSAC.Compute` používá neseedovaný `new Random()`, takže tentýž vstup dá pokaždé jiný výsledek (±8 přijatých ze 421 dvojic). Než se to zjistilo, vyšly z jednotlivých běhů dva závěry, které neplatily; od té doby se každá varianta estimátoru měří 12× a porovnávají se rozpětí. Jde to proti zbytku projektu (`DeterministicNoise`, `ComparisonTarget`) a je podezřelé i z jednoho nestabilního běhu testovací sady. Zaseedování je drobnost, zatím neudělaná — v kódu je pořád `new Random()`.

- [ ] Naseedovat `RANSAC` (reprodukovatelný replay, jedno měření na variantu)

[map-correlation-localization.md](map-correlation-localization.md), [RANSAC.cs](../Src/ARBot.Common/Algorithms/ML/RANSAC.cs) · DevLog [2026-08-23](devlog.md#2026-08-23), [2026-08-24](devlog.md#2026-08-24)

<a id="lok-bias-senzoru-jako-stav-ekf"></a>
### ⬜ Chyby senzorů (bias kompasu a gyra) jako stavy EKF

`lok-bias-senzoru-jako-stav-ekf` · záměr · **otevřeno** · nalezeno 25. 8. 2026

Podnět autora: místo přidávání dalších referencí kurzu odhadovat bias kompasu a gyra přímo ve stavovém vektoru filtru — dvě nezávislé absolutní reference (kompas, GPS kurz) na to stačí, mapa není potřeba. Úkol byl 25. 8. gatovaný potvrzením, že skutečný VN100 nějaký bias vůbec má (ten 3° v simulaci vnutil člověk). To se potvrdilo v terénu 7. 9. (kurz o 24° vedle, po kalibraci magnetometru zbývá konstanta −3,7°). Zůstává cílem; 12. 9. se místo něj sáhlo po podlaze sigmy a škrcení kompasu, „na stav EKF teď není prostor". ✅ **Potvrzení z HW 18. 9. 2026:** s kalibrovaným magnetometrem má kompas proti GPS kurzu bias −2,5 / −1,6° (sd ~4°), na protilehlých kurzech stejný, ale mezi běhy jiný — tedy reálný, malý a pomalu proměnný bias, přesně případ pro stav EKF, ne pro konstantu v konfiguraci. Brána „potvrdit na reálném HW" je tím splněná.

- [x] Přístroj na rozpor dvou absolutních referencí bez ground truth (`ARBot.Analyze heading --nogt`) (25. 8. 2026)
- [x] Potvrdit bias kompasu na skutečném senzoru záznamem se smyčkou (7. 9. 2026)
- [ ] Bias kompasu a gyra jako stavy EKF (varianta B)

čeká na [lok-gps-kurz-do-fuze](#lok-gps-kurz-do-fuze), [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [ekf-fusion.md](ekf-fusion.md) · DevLog [2026-08-25](devlog.md#2026-08-25), [2026-09-07](devlog.md#2026-09-07), [2026-09-12](devlog.md#2026-09-12), [2026-09-18](devlog.md#2026-09-18)

<a id="lok-gps-casova-korelace"></a>
### ⬜ Chyba GPS fixu je korelovaná ~40 s, filtr ji bere jako nezávislou

`lok-gps-casova-korelace` · vada · **otevřeno** · nalezeno 6. 9. 2026

Nový `ARBot.Analyze gps` využívá, že stojící robot dává pravdu zadarmo: chyba fixu má p50 4,4 m a dekorelační čas ~40 s, takže průměrování nepomůže vůbec, a filtr přitom bere 10 fixů za sekundu jako nezávislé — odhad ujíždí 5,5 m/min s časovou konstantou 57 s a hlásí sigmu 0,074 m. Prozatímní léčba: `gpsposstd` je parametr a v provozním profilu je 30 m (20× ze √400 korelovaných vzorků), jen pro Pi, ne jako default. Skutečná léčba je decimace nebo offset GPS jako stav EKF; za jízdy dekorelační čas změřit nešlo, nejdelší souvislá jízda v záznamech je 45 s. 17. 9. 2026 se za jízdy změřit DAL: souvislý úsek 215 s a 96 m dal dekorelační čas ~40 s (zbytek po zarovnání proti mrtvému odhadu z kol p50 3,58 m, p90 7,58 m) — tedy stejný řád jako ze stání, ale pořád jen ~5 nezávislých vzorků, takže je to orientační číslo. Ze stání (55 s) měl fix odchylku od průměru segmentu p50 9,68 m a maximum 17,3 m, průměrovací křivka je plochá (činitel nadsazení 10,6× při N = 100) a změřené Kalmanovo zesílení 0,32/0,29 říká, že GPS odhad pořád táhne. Tím je vysvětlené, co obsluha viděla na stránce po kalibraci magnetometru — poloha o „3–4 metry ujetá": chyba fixu té velikosti drží desítky sekund, takže nevypadá jako šum, ale jako posunutá stopa. Podmínky byly toho dne slabé: 4–6 družic a DOP 6,8–12, tedy na hraně brány `gpsmaxdop=10` (na startu se zamítlo 136, resp. 205 fixů po sobě).

- [x] `ARBot.Analyze gps` (drift, K, tau, autokorelace, blok A2b za jízdy) (6. 9. 2026)
- [x] `gpsposstd=` jako parametr, 30 m v `pi-provoz.cfg` (6. 9. 2026)
- [ ] Ověřit násobek na novém záznamu ze stání (drift a tau se mají posunout 20×)
- [ ] Souvislá jízda 5–10 minut bez zastavení pro dekorelační čas za jízdy
- [x] První měření za jízdy (17. 9., úsek 215 s / 96 m → T_d ~40 s); na 5–10 min to nestačí (17. 9. 2026)
- [ ] Decimace nebo offset GPS jako stav EKF

čeká na [lok-bias-senzoru-jako-stav-ekf](#lok-bias-senzoru-jako-stav-ekf) · [ekf-fusion.md](ekf-fusion.md), [configuration.md](configuration.md), [GpsReport.cs](../Src/ARBot.Analyze/GpsReport.cs) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-17](devlog.md#2026-09-17)

<a id="lok-gps-kurz-korelovana-chyba"></a>
### ⬜ Chyba kurzu z GPS není bílý šum a GPS běží 10 Hz, ne 5

`lok-gps-kurz-korelovana-chyba` · vada · **otevřeno** · nalezeno 12. 9. 2026

Dotaz autora na původ σ kurzu z GPS vedl k měření: sousední fixy se liší jen o ~1°, ale chyba s odstupem roste a usadí se na 8,4° / 4,1° při dekorelačním čase 10 / 5 s. Filtr bere fixy jako nezávislé, takže počtivá σ je 83° / 29°; model `atan2(0,3; v)` dává 23°, tedy trefuje zhruba správně, ale ze špatného důvodu (příčný šum rychlosti je ve skutečnosti 0,007 m/s). Zároveň se opravilo, že GPS chodí 10 Hz — všechny přepočty byly dvakrát vedle, frekvence se teď měří. Důsledek pro kompas: jeho bias je korelovaný na stovky sekund, počtivá σ by byla ~1 200°, takže podlaha 5° je pořád řádově málo. Léčba (podlaha v desítkách stupňů, další škrcení, nebo bias jako stav EKF) není rozhodnutá.

- [x] Korelace chyby GPS kurzu změřena (`ARBot.Analyze heading`) (12. 9. 2026)
- [x] Frekvence GPS se měří (`FixRateHz`), tři místa s natvrdo 5 Hz opravena (12. 9. 2026)
- [ ] Rozhodnout, jak σ obou referencí kurzu narovnat

čeká na [lok-bias-senzoru-jako-stav-ekf](#lok-bias-senzoru-jako-stav-ekf) · [ekf-fusion.md](ekf-fusion.md) · DevLog [2026-09-12](devlog.md#2026-09-12)

<a id="lok-koridor-prah-inlieru-prisny"></a>
### ⬜ `MinInliers=25` škrtí koridor 1,4–4× a proti čemu vznikl, to nechytá

`lok-koridor-prah-inlieru-prisny` · vada · **otevřeno** · nalezeno 17. 9. 2026

`MinInliers` je největší ztrátová brána proložení koridoru (17. 9. zahodila 3 987 z 6 173 cyklů). Práh 25 vznikl na starším záznamu odjinud a jeho zdůvodnění je konkrétní: bez něj se do statistiky míchaly přímky proložené 3–6 body, které vyjdou **kolmo na cestu** (šířka až 10 m, směr −88°), a šířka měla sd 3,3 m místo 0,45 m. Změřeno ze záznamů (nový blok *PRAH INLIERU* v `ARBot.Analyze corridor`, který dopočítá geometrii z uložených úseček, takže **nový výjezd netřeba**): nad `20260917-160558.rec` by práh 15 dal **2 513 koridorů místo 635 (4,0×)** a podíl nesmyslné šířky (mimo 1–8 m) by šel 0,0 → 2,0 %; nad `20260916-164926.rec` by dal **3 399 místo 2 354 (1,4×)** a podíl nesmyslné šířky 2,0 → 2,6 %. ⚠️ **Podstatné je to druhé číslo: při dnešním prahu 25 už je nesmyslná šířka 2,0 %**, a při prahu **20 je jen 1,7 %**, tedy MÉNĚ než při 25. Ta závislost je nemonotónní, což znamená, že **práh tu vadu neřídí** — případy s kolmou přímkou nejsou soustředěné v cyklech s málo inliery. Rozdělení šířky je přes všechny prahy prakticky stejné (p50 3,00–3,16 m). Druhý signál týmž směrem: cykly, které by práh navíc pustil, procházejí testem rovnoběžnosti **častěji** než ty dnes přijaté (65 % proti 47 % nad 17. 9.) — přesný opak toho, co by „málo inlierů = šum" předpovídalo. ⚠️ **Neříká to, že by se tím něco spravilo.** Měří se jen stupeň *proložení*; jestli by ty koridory navíc prošly přiřazením hrany a příčnou bránou, se z těchto záznamů říct nedá — 17. 9. byl rozbitý kurz a póza 2,5–4,5 m mimo mapovanou vozovku, takže tam se stejně ztrácelo všechno až dál. A „šířka v 1–8 m" je slabé měřítko kvality: přímka proložená na špatnou hranu (obrubník místo trávy) dá věrohodnou šířku taky. ✅ **Od 17. 9. 2026 je to parametr `corridormininliers=`** (výchozí 25, tedy beze změny chování), takže A/B na zařízení jde pustit z profilu. Výchozí hodnota se ZÁMĚRNĚ nemění, dokud se neprojede na robotu: měření výš je jen o stupni proložení, ne o tom, co doteče do fúze. Spodní mez validátoru je 3 — dvěma body jde přímku proložit vždy, takže 2 by bránu fakticky vypnulo. 18. 9. 2026 s kalibrovaným kompasem: zisk prahu 20 je jen **+6–7 %** koridorů (17. 9. při rozbitém kurzu 4×) a nesmyslná šířka roste — ztráty už nesedí na tomhle prahu. Výchozích 25 zůstává; A/B na zařízení tím ztrácí naléhavost.

- [x] Blok *PRAH INLIERU* v reportu (geometrie z uložených úseček; měřidlo ověřené proti známé odpovědi) (17. 9. 2026)
- [x] Změřeno na dvou záznamech (17. 9. a 16. 9.), nemonotónní závislost potvrzena (17. 9. 2026)
- [x] Vystavit `MinInliers` jako parametr `corridormininliers=` (validátor 3–500, guard test hlídá, že se čte) (17. 9. 2026)
- [ ] Projet A/B na zařízení (25 proti 20 proti 15) a rozhodnout výchozí hodnotu
- [x] Změřit s dobrým kurzem — 18. 9.: práh 20 dá jen +6 / +7 % koridorů (3 414 proti 3 221; 3 426 proti 3 211) při horší nesmyslné šířce (1,7 → 2,5 %; 0,6 → 0,9 %); přiřazení hrany pouští 98–99 %, takže by skoro všechny došly do fúze — zisk malý, 25 zůstává (18. 9. 2026)

[map-correlation-localization.md](map-correlation-localization.md), [CorridorConfig.cs](../Src/ARBot.Common/Localization/CorridorConfig.cs), [CorridorReport.cs](../Src/ARBot.Analyze/CorridorReport.cs) · DevLog [2026-09-17](devlog.md#2026-09-17), [2026-09-18](devlog.md#2026-09-18)

<a id="lok-korelace-gridu-s-mapou"></a>
### 🧪 Korelace occupancy gridu s mapou jako oprava polohy a kurzu

`lok-korelace-gridu-s-mapou` · záměr · **v kódu, na HW neověřeno** · nalezeno 19. 8. 2026 · vyřešeno 19. 8. 2026

Semantický kanál lokální mapy (co kamera vidí jako cestu) se porovnává s vozovkou podle OSM a z posunu se odhaduje chyba polohy a kurzu, která jde do fúze jako dvě osová měření a kurz. Jádro, napojení do runtime a 17 telemetrických sloupců vznikly podle dvanáctidílného plánu; první měření hlásilo cyklus 126 ms, 25. 8. se ukázalo, že stojí 1,31 s, celé jádro (Debug 5,5× pomalejší a k měření bezcenný). Ve výchozím stavu se korelátor vůbec nezakládá (`mapcorr=false`) — nic neřídí a stál by celé jádro (při odstupu 3 s ~40 %); naostro ho pustí až tři podmínky. Na zařízení neběželo.

- [x] Jádro, napojení do runtime a telemetrie (plán fáze 1–3) (19. 8. 2026)
- [x] První spuštění v simulaci a naměřená doba cyklu (19. 8. 2026)
- [x] Přepínač `Enabled` nevypínal výpočet, jen posílání — nový `mapcorr=` (výchozí false) a přejmenování na `SendCorrections` (20. 8. 2026)
- [x] Přepínač `mapcorrsend=` pro A/B se stejnou zátěží (21. 8. 2026)
- [ ] Měření na OrangePi (fáze 5 plánu)

čeká na [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) · [map-correlation-localization.md](map-correlation-localization.md), [plan-map-correlation.md](plan-map-correlation.md) · DevLog [2026-08-19](devlog.md#2026-08-19), [2026-08-20](devlog.md#2026-08-20), [2026-08-21](devlog.md#2026-08-21)

<a id="lok-korelace-sigma-nepoctiva"></a>
### 🧪 Sigma korelace je slepá k množství důkazu

`lok-korelace-sigma-nepoctiva` · vada · **v kódu, na HW neověřeno** · nalezeno 19. 8. 2026 · vyřešeno 25. 8. 2026

Nejistota korelace se počítala ze zakřivení normalizovaného skóre, takže nevěděla, kolik buněk za ní stojí — malý oblak důkazu hlásil větší jistotu než velký a na cestě rovnoběžné s osou gridu vycházela falešná podélná jistota. Případ „malý oblak obelže hlídač volné osy" se ukázal být touž vadou a vědomě se neopravoval zvlášť. Léčba přišla 25. 8.: sigma se škáluje vahou informativního důkazu (`ReferenceInformativeEvidence`), měřeno proti tuze posunuté mapě. Po odečtení chyby fúze v měřidle (samostatné téma) vyšla σ naopak ~1,25× konzervativní a vědomě se neopravuje; na zařízení neběželo.

- [x] Falešná podélná jistota potvrzena za běhu (SigmaLoose konečná ve všech cyklech) (19. 8. 2026)
- [x] Malý oblak obelže hlídač — změřeno, že je to táž vada, rozhodnuto neopravovat zvlášť (20. 8. 2026)
- [x] Rozvaha korelace přes FFT / Fourier-Mellin — jako jiný estimátor by pomohla, odloženo do rozhodnutí o přestavbě (20. 8. 2026)
- [x] Honestní sigma změřena nástrojem `ARBot.Analyze sigma` a opravena (25. 8. 2026)

[map-correlation-localization.md](map-correlation-localization.md), [rozhodnutí 25. 8. 2026](decisions.md) · DevLog [2026-08-19](devlog.md#2026-08-19), [2026-08-20](devlog.md#2026-08-20), [2026-08-25](devlog.md#2026-08-25)

<a id="lok-koridor-hranova-lokalizace"></a>
### 🧪 Lokalizace z hran cesty místo z plochy

`lok-koridor-hranova-lokalizace` · záměr · **v kódu, na HW neověřeno** · nalezeno 21. 8. 2026 · vyřešeno 21. 8. 2026

Plošná korelace platí za informaci, kterou vnitřek cesty nenese; stačí najít hranici cesty, proložit ji přímkou a v místě robota změřit šířku, příčnou polohu a odchylku osy. Spike nad záznamem dal příčnou polohu na 3 cm a směr na 0,8° za 0,1 ms na snímek proti 62–104 ms plošného skenu. Vznikl `CorridorFinder`, mapová protistrana `RoadAxis` a stupeň `CorridorLocalizer` (`corridor=`), který posílá do fúze příčné a kurzové měření. Od 15. 9. běží v provozním profilu v měřicím režimu (bez vlivu na řízení); první jízda ze zařízení 16. 9. (2 256 cyklů, 424 přijatých). **18. 9. 2026 první jízdy s korekcemi naostro** (`20260918-154028.rec`, `-155329.rec`): za jízdy měření v 52–54 % cyklů (rezidua 7 cm, ~43 inlierů na stranu, úsečky 7 m), ve stání 0,6 %; do fúze odešlo 2 821 + 2 889 měření a fúze se jimi řídí (`odhad − IMU yaw` z ±0,06° na ±1,9°). ⚠️ Přesnost pózy tím ověřená není — s korekcemi naostro je příčný nesouhlas (p50 0,06 m) self-konzistence.

- [x] Spike nad záznamem z virtuálních kamer (21. 8. 2026)
- [x] `CorridorFinder`, `RoadAxis`, `CorridorLocalizer` a `RoadCorridorMsg` v repozitáři (21. 8. 2026)
- [x] Ověřeno během v simulaci (178 měření za 40 s, chyba polohy 0,027 m, kurzu 0,18°) (23. 8. 2026)
- [x] Kvalita hranice na reálné D435 (18. 9.: rezidua 7 cm, ~43 inlierů, dosah 7 m; každá kamera vidí jen svou hranu — křižovatka na trati nebyla) (18. 9. 2026)
- [x] Zapnout posílání korekcí naostro (autor, `config/pi-provoz.cfg`, commit `a44b4f4`) (17. 9. 2026)
- [x] První běh na zařízení v měřicím režimu (`20260916-164926.rec`) (16. 9. 2026)
- [ ] Ověřit přesnost pózy s korekcemi (A/B `corridorsend=false` po téže trati, nebo pravda)
- [ ] Změřit, jak rychle póza po výpadku koridoru (stání, jedna kamera) spadne na GPS při `gpsposstd=30`

čeká na [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) · [map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-08-21](devlog.md#2026-08-21), [2026-08-23](devlog.md#2026-08-23), [2026-09-15](devlog.md#2026-09-15), [2026-09-16](devlog.md#2026-09-16), [2026-09-18](devlog.md#2026-09-18)

<a id="lok-koridor-gating-a-gate-mimo-koridor"></a>
### 🧪 Hranová lokalizace za běhu zahazovala 77 % korekcí a hlásila měření i metr mimo cestu

`lok-koridor-gating-a-gate-mimo-koridor` · vada · **v kódu, na HW neověřeno** · nalezeno 22. 8. 2026 · vyřešeno 22. 8. 2026

Při prvním běhu hranové lokalizace (`corridor=`) vyplavaly dvě vady. Tvrdý gate `Reject` pustil jen 65 z 280 měření, protože měření tvrdilo 3 cm jistoty a nesouhlasilo o 55 cm; přepnuto na `GateMode.Soft`, jak předepsalo rozhodnutí z 20. 8. Stupeň navíc hlásil platné měření i při příčné poloze 2,1 m od osy koridoru širokého 2 m — doplněn gate „jsem uvnitř koridoru" (`OutsideCorridor`) a počítadlo, kolik korekcí fúze skutečně přijala (`DroppedByFusion`), protože „poslali jsme" není totéž co „došlo to". Přitom se ukázalo, že rig dvou map ani `poseerror=` nemohou ověřit konvergenci — vkládají chybu do pozorování, ne do pózy. Na zařízení koridor běží od 15. 9. jen v měřicím režimu, korekce se do fúze neposílají.

- [x] Počítadlo `DroppedByFusion` v `RoadCorridorMsg` (22. 8. 2026)
- [x] Výchozí gating `Soft` místo `Reject` (22. 8. 2026)
- [x] Gate `MaxOutsideCorridorM` → `CorridorFixReason.OutsideCorridor` (22. 8. 2026)
- [ ] Ověřit gating s korekcemi naostro na zařízení

čeká na [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) · [map-correlation-localization.md](map-correlation-localization.md), [CorridorLocalizer.cs](../Src/ARBot.Common/Localization/CorridorLocalizer.cs) · DevLog [2026-08-22](devlog.md#2026-08-22)

<a id="lok-kompas-sigma-podlaha"></a>
### 🧪 Kompas si věří 60–90× víc, než jaký je

`lok-kompas-sigma-podlaha` · vada · **v kódu, na HW neověřeno** · nalezeno 25. 8. 2026 · vyřešeno 12. 9. 2026

Senzor VN100 hlásí nejistotu kurzu 0,06°, ale proti kurzu z GPS se trvale mýlí o 3–5°. Fúze proto věřila kompasu asi 4 000× víc než GPS a žádná druhá reference kurzu (GPS kurz, korelace s mapou) neměla šanci cokoli opravit — odhad kurzu seděl na kompasu na 100 % i se zapnutými korekcemi. Jádro je v tom, co sigma kompasu popisuje: krátkodobý šum, ne bias. Od 12. 9. má sigma kurzu z kompasu podlahu 5° (`imuheadingstd=`) a absolutní kurz se navíc škrtí na 1 Hz (`imuheadinghz=`); gyro jede dál v plné kadenci. Poměr informace spadl na ~2,2 : 1, ale poctivý filtr z toho není — bias je časově korelovaný a filtr ho bere jako bílý šum. 18. 9. 2026 (kalibrovaný kompas): chyba kompasu proti GPS kurzu má bias do 3,5° a sd ~4° (včetně šumu GPS kurzu), takže podlaha 5° je správného řádu. A/B `imuheadingstd=5` proti `=0` pořád není.

- [x] 'Změřit poměr informace kompas : GPS kurz (~4 000 : 1) a že odhad sedí na kompasu na 100 %' (25. 8. 2026)
- [x] Podlaha sigmy kurzu z kompasu `imuheadingstd=` (výchozí 5°, skládá se kvadraticky s `YprU`) (12. 9. 2026)
- [x] Škrcení absolutního kurzu z kompasu `imuheadinghz=` (výchozí 1 Hz, gyro neomezeno) (12. 9. 2026)
- [ ] Záznam na zařízení s `imuheadingstd=5` a `=0` nad týmž úsekem (A/B)

čeká na [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [ekf-fusion.md](ekf-fusion.md), [rozhodnutí 12. 9. 2026](decisions.md) · DevLog [2026-08-25](devlog.md#2026-08-25), [2026-09-12](devlog.md#2026-09-12), [2026-09-18](devlog.md#2026-09-18)

<a id="lok-korelace-dekorelacni-cas"></a>
### 🧪 Cykly korelace s mapou nejsou nezávislé — odstup je dekorelační čas 3 s

`lok-korelace-dekorelacni-cas` · vada · **v kódu, na HW neověřeno** · nalezeno 25. 8. 2026 · vyřešeno 25. 8. 2026

Fúze bere každé měření jako nezávislé, ale korelace čte tentýž grid s pamětí ~2,5 s, takže dva cykly po sobě říkají skoro totéž a informace se započítá dvakrát (činitel nadsazení 1,9–2,4). Dekorelační čas vyšel 2,85 / 2,93 / 3,31 s na třech bězích s periodou lišící se o 42 %, tedy je to konstanta scény, ne artefakt měření. Léčba je odstup konstrukcí: nejmenší perioda korelace 400 ms → 3 s, po změně je korelace sousedních cyklů záporná a činitel 1,00. Při tom se opravil o řádek špatný údaj o ceně — cyklus stojí 1,31 s (celé jádro), ne ~126 ms; hranice 400 ms byla v praxi mrtvá. Změřeno v simulaci, na zařízení korelace neběžela (`mapcorr=false`).

- [x] Autokorelace reziduí a dekorelační čas v `ARBot.Analyze sigma` (tři běhy) (25. 8. 2026)
- [x] `MinPeriod` 400 ms → 3 s (rozhodnutí autora), ověřeno dvěma běhy (25. 8. 2026)
- [ ] Ověřit odstup a cenu cyklu na Orange Pi (se zapnutým `mapcorr=`)

čeká na [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) · [map-correlation-localization.md](map-correlation-localization.md), [rozhodnutí 25. 8. 2026](decisions.md), [SigmaReport.cs](../Src/ARBot.Analyze/SigmaReport.cs) · DevLog [2026-08-25](devlog.md#2026-08-25)

<a id="lok-mapcorr-tvrdy-gate"></a>
### 🧪 Tvrdý gate korekcí z mapy zahazoval právě ty korekce, které byly potřeba

`lok-mapcorr-tvrdy-gate` · vada · **v kódu, na HW neověřeno** · nalezeno 25. 8. 2026 · vyřešeno 25. 8. 2026

Korekce polohy z korelace occupancy gridu s mapou se poprvé pustily naostro a změřily (`ARBot.Analyze corrections`): s tvrdým gatem byl výsledek horší, než když se nekorigovalo vůbec (příčná chyba p50 0,67 → 0,85 m), protože gate zamítal 42–46 % korekcí podle velikosti innovace — tedy přesně ty velké, které měly chybu stáhnout. Korelátor přitom hlásil správně. Měkký gate (`GateMode.Soft`) je od té doby výchozí (0,59 m), `mapcorrgate=reject` vrací staré chování. Zisk je ale jen 6–13 %, dokud kurz drží kompas; celá korelace je navíc ve výchozím stavu vypnutá (`mapcorr=false`), takže na zařízení nikdy neběžela.

- [x] Přístroj `ARBot.Analyze corrections` (krok pózy, gating, NIS podle zdroje, chyba proti pravdě) (25. 8. 2026)
- [x] Změřit tvrdý proti měkkému gatu na scéně se skutečným driftem (dva běhy na variantu) (25. 8. 2026)
- [x] `GateMode.Soft` výchozí (25. 8. 2026)
- [ ] Ověřit se zapnutou korelací na zařízení

čeká na [lok-kompas-sigma-podlaha](#lok-kompas-sigma-podlaha) · [map-correlation-localization.md](map-correlation-localization.md), [rozhodnutí 25. 8. 2026](decisions.md) · DevLog [2026-08-25](devlog.md#2026-08-25)

<a id="lok-koridor-merici-rezim"></a>
### 🧪 Koridor v měřicím režimu — odtlumení a první měřicí jízda

`lok-koridor-merici-rezim` · záměr · **v kódu, na HW neověřeno** · nalezeno 15. 9. 2026 · vyřešeno 15. 9. 2026

Koridor měří snímek co snímek týž okraj cesty, takže jeho chyba je časově korelovaná — táž past jako u GPS a kompasu. Přibyly `corridorstd=`, `corridorheadingstd=` a `corridorhz=` (sigmy kvadraticky, škrtí se jen posílání), výchozí 0 schválně, protože dekorelační čas koridoru změřený nebyl; provozní profil měl běžet s `corridor=true`, `corridorsend=false` — plná zátěž, nulový vliv na řízení. ⚠️ **Jenže ten řádek se do profilu nikdy nedostal** a default je `true`, takže korekce se posílaly; viz `lok-koridorsend-nebyl-vypnuty`. První měřicí jízda 16. 9. (první záznam ze zařízení, kde koridor vůbec běžel) dala první střízlivé číslo: koridor je dobrá reference kurzu (+0,42° proti GPS, robustní sd 2,30°), ale proložení hlásí 0,50°, tedy je ~5–9× optimističtější. Poloha byla celou jízdu mimo mapovanou vozovku (3–5 m), takže do fúze neodešlo 0 ze 424 přijatých měření — a report to do té doby neuměl říct (`Ok` znamenalo „prošlo branami“, ne „došlo do fúze“). 18. 9. 2026: `corridorsend=true` je v profilu vědomě od 17. 9. a první jízdy naostro ho potvrdily; σ kurzu z proložení je potřetí ~4× optimistická (robustní sd 2,4–2,6° proti 0,6°), takže číslo pro `corridorheadingstd=` je ~2,5°, ale pořád nenastavené. **18. 9. večer, pro soutěž:** autor chtěl koridor „trošku“ oslabit; v profilu je `corridorstd=0.1`, `corridorheadingstd=2.5`, `corridorhz=2` (~15× méně příčné informace, ~90× méně o kurzu než dnes), podloženo A/B v simulaci s pravdou (příčně 0,018 m, kurz 0,41° proti 0,84 m / 2,4° bez korekcí). Na zařízení s tím nejelo.

- [x] Parametry odtlumení a měřicí profil `pi-provoz.cfg` (15. 9. 2026)
- [x] Měřicí jízda na zařízení (`20260916-164926.rec`) (16. 9. 2026)
- [x] Report `corridor` — trychtýř ztrát, „došlo to do fúze?“, neuťatá příčná statistika, koridor jako reference kurzu (16. 9. 2026)
- [x] Nastavit `corridorheadingstd=` z měření — 18. 9.: 2,5° (robustní sd koridor − GPS kurz 2,4–2,6°), s `corridorstd=0.1` a `corridorhz=2` v profilu pro soutěž; A/B v simulaci příčně 0,003 → 0,018 m, kurz 0,13 → 0,41° (18. 9. 2026)
- [ ] Ověřit odtlumené hodnoty na zařízení (záznam ze soutěže 19. 9., `ARBot.Analyze corridor` + `corrections`)
- [x] Rozvolnit příčnou bránu — vyřešeno zrušením brány, viz `lok-koridor-pricna-brana` (18. 9. 2026)
- [x] `corridorsend=true` v profilu — autor 17. 9. (commit `a44b4f4`) po kalibraci kompasu 12. 9. a přiřazení hrany 16. 9.; první jízdy 18. 9. (17. 9. 2026)

čeká na [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) · [map-correlation-localization.md](map-correlation-localization.md), [pi-provoz.cfg](../config/pi-provoz.cfg) · DevLog [2026-09-15](devlog.md#2026-09-15), [2026-09-16](devlog.md#2026-09-16), [2026-09-18](devlog.md#2026-09-18)

<a id="lok-naucena-sirka-do-mapy"></a>
### 🧪 Naučená šířka cesty jde dál do mapy — korelaci i kreslení

`lok-naucena-sirka-do-mapy` · záměr · **v kódu, na HW neověřeno** · nalezeno 15. 9. 2026 · vyřešeno 15. 9. 2026

Odhad šířky zůstával uvnitř koridoru a nedosáhl na korelaci s mapou (jediný algoritmický dopad) ani na kreslení. Teď je to neměnný překryv `uzel → šířka` (`roadwidthmap=`, výchozí false), graf sítě se nemění, protože ho drží i navigace; scéna korelátoru se atomicky zamění za prahem 0,25 m a odstupem 10 s. Scéna virtuální kamery překryv nedostane, jinak by koridor měřil sám sebe. Autor hned při prvním proklikání našel vadu: do maxima na sdíleném uzlu přispívala i nezměřená cesta svou mapovou hodnotou, takže se naučené zúžení přehlasilo prakticky na celé síti — mapová šířka nezměřené cesty je default, ne důkaz. Perzistence vědomě není; prahy jsou odhad a jdou doladit offline. Na zařízení neběželo.

- [x] `RoadWidthOverrides`, `RoadWidthMapUpdater`, vrstva v mapě i na půdorysu (15. 9. 2026)
- [x] Oprava maxima na sdíleném uzlu, práh proti účinné šířce, hláška s velikostí změny (15. 9. 2026)
- [ ] Doladit `RebuildThresholdM` a `MinRebuildPeriodSec` z prvního záznamu
- [ ] Ověřit na zařízení

čeká na [lok-korelace-tri-podminky-naostro](#lok-korelace-tri-podminky-naostro) · [plan-naucena-sirka-do-mapy.md](plan-naucena-sirka-do-mapy.md), [map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-09-15](devlog.md#2026-09-15)

<a id="lok-sirka-odhad-bez-brany"></a>
### 🧪 Šířková brána koridoru se ptala dřív, než se bylo z čeho učit

`lok-sirka-odhad-bez-brany` · vada · **v kódu, na HW neověřeno** · nalezeno 15. 9. 2026 · vyřešeno 15. 9. 2026

Porovnání naměřené šířky cesty s odhadem běželo před jeho aktualizací a při prvním kontaktu s hranou vracelo mapovou šířku — u cest bez tagu `width` jen default 3 m. Na cestě širší než 4,5 m se první měření nepřijalo nikdy a hrana zůstala němá navždy; v ostré mapě nemá tag `width` ani jedna z 412 cest, takže na vozovce by koridor nezměřil nic a vypadalo by to jako porucha detektoru. `RoadWidthEstimator` běží bez brány (okno, medián, kvalita z MAD) a dokud si měření nesednou, řekne „nevím“; šířka na póze nezávisí, takže kvalitu měří shoda měření mezi sebou, ne shoda s mapou. Při měřicí jízdě 16. 9. na zařízení šířka seděla (nesouhlas p50 0,057 m); prahy zůstávají odhad.

- [x] `RoadWidthEstimator` s verdiktem kvality, důvod `WidthNotTrusted` (15. 9. 2026)
- [x] Rozpad `FixReason` nad měřicí jízdou (16. 9. šířka sedí, nesouhlas p50 0,057 m) (16. 9. 2026)
- [ ] Doladit prahy kvality offline z posloupnosti `Width`

[map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-09-15](devlog.md#2026-09-15), [2026-09-16](devlog.md#2026-09-16)

<a id="lok-prirazeni-hrany-chi2"></a>
### 🧪 Polovina cyklů koridoru se párovala na příčnou ulici

`lok-prirazeni-hrany-chi2` · vada · **v kódu, na HW neověřeno** · nalezeno 16. 9. 2026 · vyřešeno 16. 9. 2026

Koridor bral nejbližší hranu sítě podle vzdálenosti, kurz do výběru nevstupoval — při chybě pózy 3–4 m vyhrála u křižovatky příčná ulice (1 114 z 2 256 cyklů nad měřicí jízdou) a šířková brána ji nechytila, protože v té mapě mají všechny cesty tutéž šířku. Teď vybírá `EdgeAssociator` přes χ² (Mahalanobis z příčné odchylky a azimutu, každá dělená svou σ — váhy se neodhadují, měří se), s podlahou na σ (bez ní by hlášená σ kurzu 1,1° proti skutečné chybě 15–20° zamítla všechno — počtvrté táž past), tvrdým vetem ±45° na azimut a odstupem od druhého kandidáta; při nejednoznačnosti se neposílá nic. Kandidáti se sbírají jako hypotézy, ne hrany, jinak by kolineární segment téže cesty zamítl každou rovnou cestu. Přitom se opravila živá vada: `Send()` odečítal směr přímky bez rozhodnutí o smyslu, takže u cesty kolmé na kurz šel do fúze kurz otočený (9,4 % přijatých cyklů); cena je, že koridor už nikdy neřekne „jsi otočený o 180°“. Ověřeno testy a simulací, na zařízení neběželo. 18. 9. 2026 po kalibraci kompasu: chyba kurzu odhadu proti GPS je sd 8–11° včetně stání a ~2–4° za jízdy, takže `assocfloorhdg` jde stáhnout spíš na **5°** než na 3°; před soutěží nezměněno.

- [x] Nález nad `20260916-164926.rec` a podklad pro přiřazení (χ² s hlášenými σ zamítne vše) (16. 9. 2026)
- [x] `EdgeAssociator`, parametry `assoc*`, `RoadCorridorMsg` verze 6 se skóre (16. 9. 2026)
- [x] Oprava orientace kurzu v `Send()` (složení na ±90°, smysl podle kurzu) (16. 9. 2026)
- [ ] Přeměřit nad měřicí jízdou (odhad „2× víc měření“ je z rozdělení, ne z běhu)
- [ ] Po opravě magnetometru snížit `assocfloorhdg` z 10° na ~3°
- [x] Ověřit na zařízení — 18. 9.: běží (χ² vítěze p50 0,004, nejednoznačných 3 z 3 147), ale trať je jediná OSM cesta, takže křižovatku to neotestovalo (18. 9. 2026)

čeká na [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-09-16](devlog.md#2026-09-16), [2026-09-18](devlog.md#2026-09-18)

<a id="lok-koridor-pricna-brana"></a>
### 🧪 Příčná brána koridoru zahazovala 81,8 % cyklů — zrušena bez náhrady

`lok-koridor-pricna-brana` · vada · **v kódu, na HW neověřeno** · nalezeno 18. 9. 2026 · vyřešeno 18. 9. 2026

Z dotazu autora „na kolik je nastaven parametr ovlivňující příčnou bránu?“ nad `20260917-160558.rec`. `MaxLateralDisagreementM = 1,5 m` byla hardcoded konstanta bez klíče — a hlavně testovala doslova tutéž veličinu jako `EdgeAssociator` o pár řádků výš (`dLat = corridor.Lateral − axis.Lateral`), jen pevným pravítkem místo χ² škálovaného kovariancí pózy, a stála až ZA ním. Nad tím záznamem zahodila 81,8 % cyklů, které dostaly hranu, při σ polohy z fúze 3,73 m — tedy brána na 0,4 σ vlastní nejistoty. Je to táž vada, jaká se u `MapCorrelator`u změřila 25. 8. 2026 (tvrdý `Reject` dělal výsledek horší než nekorigovat vůbec): tvrdý strop na innovaci zahazuje právě ty velké korekce, které jsou potřeba, takže chyba pózy zůstane nad stropem navždy a hrana je němá. Zrušeno, ne přenastaveno: rozsah `dLat` je omezený konstrukcí (≲ 10 m), takže práh „jen jako pojistka“ by byl mrtvý kód a nižší řeže do živého. Odemklo to i učení šířky (`widths.Add` stálo až za branou) — týž zámek, jaký se 15. 9. odstraňoval o patro níž. Hodnota enumu `LateralDisagreement = 6` zůstává kvůli čtení starších `.rec`.

- [x] Nález — brána testuje tutéž veličinu jako `EdgeAssociator` a stojí za ním (18. 9. 2026)
- [x] Brána i `MaxLateralDisagreementM` odstraněny, tři testy otočené/nové (18. 9. 2026)
- [x] Report `corridor` — „nad bývalou branou“ + varování u starších záznamů (18. 9. 2026)
- [ ] Přeměřit nad `20260917-160558.rec` (kolik cyklů projde, rozdělení `AssocChi2`)
- [ ] Ověřit na zařízení

čeká na [lok-prirazeni-hrany-chi2](#lok-prirazeni-hrany-chi2) · [map-correlation-localization.md](map-correlation-localization.md), [decisions.md](decisions.md) · DevLog [2026-09-18](devlog.md#2026-09-18)

<a id="lok-korelace-posun-jako-stav-ekf"></a>
### ⏸ Posun mapa–GPS jako stav filtru

`lok-korelace-posun-jako-stav-ekf` · záměr · **odloženo** · nalezeno 20. 8. 2026

Návrh autora: kamera neměří polohu, ale vztah k cestě, takže by posun mezi rámcem GPS a rámcem mapy mohl být samostatný stav EKF krmený korelací. Týž den se závěr otočil — přímá korekce pózy stačí, protože všechny cíle robota jsou mapově relativní a absolutní přesnost je stejně omezená chybou mapy. Stav bude potřeba až pro použití nezávislé na mapě (návrat do depa podle GNSS, jiný zdroj mapy).

[rozhodnutí 20. 8. 2026](decisions.md), [map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-08-20](devlog.md#2026-08-20)

<a id="lok-fuze-extrapoluje-bez-omezeni"></a>
### ⏸ Fúze extrapoluje bez omezení — ztráta GPS i IMU robota nezastaví

`lok-fuze-extrapoluje-bez-omezeni` · vada · **odloženo** · nalezeno 15. 9. 2026

Nález V2 externího auditu: fúze nemá práh na stáří posledního měření, takže po výpadku GPS i IMU při živých kamerách jede odhad polohy dál z predikce a robot nezastaví. Autor 15. 9. 2026 rozhodl neřešit: pevný práh by robota zastavil i v legitimních případech a jeho hodnota se má vzít ze záznamu, ne odhadnout.

[ekf-fusion.md](ekf-fusion.md) · DevLog [2026-09-15](devlog.md#2026-09-15)

<a id="lok-ekf-fuze-od-nuly"></a>
### ✅ EKF senzorická fúze napsaná od nuly s asynchronním zpracováním měření

`lok-ekf-fuze-od-nuly` · záměr · **hotovo** · nalezeno 7. 7. 2026 · vyřešeno 28. 7. 2026

Původní EKF z ARBot2 nešel přeložit (vazby na WPF, chybějící matice) a měl pevný takt 10 Hz. Nová fúze v `ARBot.Common/Fusion` má generický `Ekf` (Joseph form, čisté kroky pro replay), model stavu `[X, Y, θ, v, ω]` s rychlostmi jako stavy (robustní vůči smyku) a `AsyncFusionEngine`, který zpracovává měření podle času pořízení s checkpointy a líným přepočtem — senzory s různou kadencí a latencí (kamery) se skládají správně. Druhý den přibylo NIS gating v měkkém režimu: odlehlému měření se nafoukne `R` místo zahození, takže se filtr z dlouhého výpadku vždy zotaví (tvrdý `Reject` umí filtr trvale zaseknout). Adaptivní odhad R/Q z reziduí se vědomě odložil — je stavový a kolidoval by s bezstavovým přehráváním. Od 28. 7. je engine thread-safe a řídicí smyčka z něj vzorkuje odhad přes `GetStateAt`.

- [x] `Ekf`, `EKFModel`, měřicí modely, `AsyncFusionEngine`, `GeoReference` (7. 7. 2026)
- [x] NIS + měkký gating proti lockoutu (8. 7. 2026)
- [x] Thread-safe engine, řízení vzorkuje odhad nezávisle na měřeních (28. 7. 2026)

[ekf-fusion.md](ekf-fusion.md) · DevLog [2026-07-07](devlog.md#2026-07-07), [2026-07-08](devlog.md#2026-07-08), [2026-07-28](devlog.md#2026-07-28)

<a id="lok-vn100-driver-ramce"></a>
### ✅ Binární driver VN100 a sjednocení souřadnicových rámců (FLU / ENU)

`lok-vn100-driver-ramce` · záměr · **hotovo** · nalezeno 10. 7. 2026 · vyřešeno 10. 7. 2026

Druhý driver VN100 čte binární výstup včetně nejistoty orientace (`YprU`) jako zdroj kovariance pro fúzi. Zároveň se pevně stanovily rámce projektu — tělo FLU, svět ENU s matematickou orientací — a rozhodlo se, že montáž senzoru (X vzad) řeší reference frame rotation uložená v senzoru, ne softwarový offset; driver převádí jen FRD → FLU a azimut → ENU. Čtení ověřeno na skutečném senzoru: dřívější „180° na severu" byla stará konfigurace senzoru, ne chyba kódu. Pozdější potíže s kurzem (heading mode Relative, kalibrace magnetometru) jsou samostatná témata.

- [x] `VN100IMUBinary` s `YprU` → `IMUState.OrientationUncertainty` (10. 7. 2026)
- [x] Rámce FLU / ENU, reference frame rotation `diag(-1,1,-1)` v senzoru (10. 7. 2026)
- [x] Ověřeno na HW (heading OK po factory resetu) (10. 7. 2026)

[imu-and-frames.md](imu-and-frames.md) · DevLog [2026-07-10](devlog.md#2026-07-10)

<a id="lok-gps-odometrie-do-fuze"></a>
### ✅ Fúze neměla žádné měření polohy ani rychlosti

`lok-gps-odometrie-do-fuze` · vada · **hotovo** · nalezeno 11. 8. 2026 · vyřešeno 12. 8. 2026

Při návrhu globální navigace se ukázalo, že mapper měření krmí filtr jen kurzem a gyrem z IMU: GPS ani odometrie do fúze netekly a referenční bod roviny nikdo nenastavoval, takže stav EKF neměl žádné měření polohy ani rychlosti. Doplnilo se mapování GPS na polohu a rychlost, odometrie na rychlost a úhlovou rychlost, počátek roviny z mapy (`map=`) a inicializace polohy z prvního fixu - bez ní by první fix stovky metrů od počátku prošel jen s malým ziskem a po zapnutí gatingu by ho filtr zahodil navždy. Testy přitom chytily, že GPS stav nese stupně a mapper je bral jako radiány.

- [x] GPS a odometrie v `DefaultMeasurementMapper`, `GeoReference` z mapy (12. 8. 2026)
- [x] `InitializePosition` z prvního fixu (i `start=` na reálném HW) (13. 8. 2026)

[ekf-fusion.md](ekf-fusion.md), [global-navigation-runtime.md](global-navigation-runtime.md) · DevLog [2026-08-11](devlog.md#2026-08-11), [2026-08-12](devlog.md#2026-08-12), [2026-08-13](devlog.md#2026-08-13)

<a id="lok-gpsstate-stupne-vs-radiany"></a>
### ✅ GPS stav nesl stupně, všechno ostatní radiány

`lok-gpsstate-stupne-vs-radiany` · vada · **hotovo** · nalezeno 12. 8. 2026 · vyřešeno 26. 8. 2026

`GPSState` byl jediný geografický typ ve stupních (u-blox posílá 1e-7 stupně), kdežto `LLA` i `GeoReference` počítají v radiánech. První verze mapperu měření předala stupně jako radiány a chytil to až test - tichá past stejného druhu jako národní prostředí při parsování. Od 26. 8. 2026 je i `GPSState` v radiánech; převod na stupně patří jen na okraje (driver při parsování, zobrazení). Naostro to kouslo 26. 8.: mise Robotour z něj stavěla `LLA` bez převodu, body v okně fixů byly desítky radiánů od sebe a armování v depu se nikdy nedočkalo; testy vadu potvrzovaly, protože jejich pomocník převáděl stejně špatně.

- [x] Test `Gps_IsInterpretedAsDegrees_NotRadians` a převod v mapperu (12. 8. 2026)
- [x] `GPSState` sjednocen na radiány (26. 8. 2026)
- [x] Dohledat zbylé převody ze stupňů (`WorldViewDocument`) (27. 8. 2026)

[imu-and-frames.md](imu-and-frames.md), [rozhodnutí 26. 8. 2026](decisions.md) · DevLog [2026-08-12](devlog.md#2026-08-12), [2026-08-26](devlog.md#2026-08-26), [2026-08-27](devlog.md#2026-08-27)

<a id="lok-korekce-zahozene-neviditelne"></a>
### ✅ Fúze zahazovala opožděné korekce a nebylo to vidět

`lok-korekce-zahozene-neviditelne` · vada · **hotovo** · nalezeno 13. 8. 2026 · vyřešeno 21. 8. 2026

Měření starší než okno historie EKF se tiše zahodilo — hláška šla jen do `Debug`, které v Release neexistuje, a telemetrie dál hlásila „Ok". V Debug buildu tak propadlo 51 z 55 korekcí (latence 1,4 s proti oknu 1 s), přičemž skutečná příčina byla latence, ne velikost okna. Přibylo počítadlo zahozených podle zdroje, hláška přes `Trace` s údajem „o kolik pozdě", verdikt měření ve zprávě `MeasurementDiagMsg` (do té doby mrtvé DTO) a sloupce v telemetrii. Potřeba vidět, co fúze s měřením udělala, byla zapsaná už 13. 8. jako otevřený úkol; tahle vada ji proměnila v konkrétní zprávu.

- [x] Zapsáno jako otevřený úkol „diagnostika EKF do streamu a záznamu“ (13. 8. 2026)
- [x] Změřena latence korekce proti oknu (Debug vs. Release) (20. 8. 2026)
- [x] Počítadlo `DroppedTooOld` a hláška přes `Trace` s typem, hodnotou a zpožděním (20. 8. 2026)
- [x] `MeasurementDiagMsg` se publikuje (`measdiag=`), verdikt Accepted / GatedOut / TooOld (21. 8. 2026)
- [x] Sloupec „zahozeno fúzí" v telemetrii pro korelaci i koridor (21. 8. 2026)

[map-correlation-localization.md](map-correlation-localization.md), [record-replay.md (kdy Trace a kdy Debug)](record-replay.md) · DevLog [2026-08-13](devlog.md#2026-08-13), [2026-08-20](devlog.md#2026-08-20), [2026-08-21](devlog.md#2026-08-21)

<a id="lok-odometrie-do-fuze-vady"></a>
### ✅ Odometrie do fúze byla dvakrát vedle - i na skutečném robotu

`lok-odometrie-do-fuze-vady` · vada · **hotovo** · nalezeno 13. 8. 2026 · vyřešeno 13. 8. 2026

Uzavřená simulační smyčka odhalila dvě vady, které platily i pro reálný hardware. Rozchod ve fúzi byl natvrdo 0,5 m proti 0,41 m z profilu, takže odometrická úhlová rychlost byla o 18 % podhodnocená. A rychlost kol se počítala z přírůstku enkodéru a doby od posledního vyzvednutí stavu - které v runtime nikdo nedělal, takže bez otevřeného okna motorů tekla do EKF trvale nula a s otevřeným závisela na překreslování UI. Rychlost je od té doby vlastní pole plněné driverem (`MotorStateBase` verze 2), enkodéry kumulativní.

- [x] `FusionConfig.WheelBase` z profilu, regresní test (13. 8. 2026)
- [x] `MotorStateBase` verze 2: rychlost z driveru, kumulativní enkodéry (13. 8. 2026)

[ekf-fusion.md](ekf-fusion.md), [virtual-hw.md](virtual-hw.md) · DevLog [2026-08-13](devlog.md#2026-08-13), [2026-08-18](devlog.md#2026-08-18)

<a id="lok-kurz-inicializace-ekf"></a>
### ✅ První korelace byla chybná, protože se kurz neinicializoval

`lok-kurz-inicializace-ekf` · vada · **hotovo** · nalezeno 19. 8. 2026 · vyřešeno 19. 8. 2026

Fúze při startu inicializovala jen polohu, kurz startoval na nule a ke skutečnému kurzu dojížděl přes měření; lokální mapa se mezitím zapisovala pootočená až o 170° a první korekce z ní měla opačné znaménko. Pojistka proti skoku pózy byla o argument krátká — rotaci stojícího robota neviděla. Kurz se teď inicializuje (`InitializeHeading`) a detektor skoku hlídá i rotaci; první cyklus vychází správně ve čtyřech ze čtyř běhů.

- [x] Příčina dohledána (grid znečištěný zápisem s kurzem u nuly) (19. 8. 2026)
- [x] `PoseJumpDetector` hlídá i rotaci (tolerance 5°) (19. 8. 2026)
- [x] `AsyncFusionEngine.InitializeHeading` místo startovního měření (19. 8. 2026)

[rozhodnutí 19. 8. 2026](decisions.md), [ekf-fusion.md](ekf-fusion.md) · DevLog [2026-08-19](devlog.md#2026-08-19)

<a id="lok-koridor-prah-inlieru-podle-vzdalenosti"></a>
### ✅ Hranice se s dálkou rozmazává a RANSAC ji měřil jedním metrem

`lok-koridor-prah-inlieru-podle-vzdalenosti` · vada · **hotovo** · nalezeno 22. 8. 2026 · vyřešeno 24. 8. 2026

Měření 12 631 hraničních bodů proti mapě ukázalo, že medián sedí na okraji vozovky v každé vzdálenosti, ale rozptyl roste z ±5 cm na metru na −0,6/+0,4 m na deseti metrech. RANSAC měl jeden práh inlieru 0,10 m pro celou hranici, takže se u vzdálených bodů chytal náhodného zarovnání. Práh je od 23. 8. úměrný vzdálenosti (optimum 0,15 m/m, +11 % přijatých). Zbytek kandidátů neobstál a je to změřené, ne názor: vážené proložení 1/σ² (vzdálené body jediné určují směr), velikost vzorku hypotézy (bez vlivu), ortogonální regrese (numericky bezvýznamná), Huberova váha (při normalizaci tolerancí principiálně no-op) i přehradlování konsenzuální sady (`RegatePasses`, ráno zapnuto na kruhové referenci, večer vráceno na 0 — práh inlieru je 10× volnější než rezidua). Přepínače v kódu zůstaly. Dvě poučení: rezidua nejsou přesnost a méně přijatých při lepší geometrii není zlepšení.

- [x] Změřit odchylky hraničních bodů proti mapě po pásmech vzdálenosti (22. 8. 2026)
- [x] Práh inlieru úměrný vzdálenosti (`InlierThresholdPerMeter`, `corridortol=`) (23. 8. 2026)
- [x] Vážené proložení a velikost vzorku hypotézy zamítnuty měřením (23. 8. 2026)
- [x] Ortogonální regrese, Huber a `RegatePasses` zamítnuty měřením (24. 8. 2026)

[map-correlation-localization.md](map-correlation-localization.md), [CorridorFinder.cs](../Src/ARBot.Common/Localization/CorridorFinder.cs) · DevLog [2026-08-22](devlog.md#2026-08-22), [2026-08-23](devlog.md#2026-08-23), [2026-08-24](devlog.md#2026-08-24)

<a id="lok-koridor-za-jizdy-propada"></a>
### ✅ Koridor za jízdy propadal v 92 % cyklů

`lok-koridor-za-jizdy-propada` · vada · **hotovo** · nalezeno 22. 8. 2026 · vyřešeno 24. 8. 2026

Za 40 s jízdy dalo měření jen 35 ze 411 cyklů. Rozpad ukázal tři různé věci: ~60 % cyklů nemělo snímek druhé kamery v okně 60 ms (`NoPair`), část byla stání v cíli na konci cesty (zamítnutí správně) a zbytek symetrické sbíhání hranic ~11° těsně nad prahem 10°. Hypotéza ohybu ze zpětné projekce padla testem `BoundaryStraightnessTests`. `NoPair` vyřešila kompenzace pohybu mezi snímky (`Reproject`, párování se do té doby dívalo jen dozadu): `NoPair` 260 → 20, přijatých 76 → 159. Sbíhání ~11° nakonec žádná vada nebyla — byla to nálevka v testovací mapě (rozšíření 1 → 3 m na 10 m dává přesně 11,42°); nad mapou s konstantní šířkou je 100 % cyklů přijato po prvních 60 s. Proložené přímky kreslené v mapě navíc ukázaly, že většina zamítnutí padá na křižovatku a slepý konec, kde koridor existovat nemá.

- [x] Rozpad příčin (`NoPair`, stání v cíli, sbíhání 11°) a diagnostika v `RoadCorridorMsg` v2/v3 (22. 8. 2026)
- [x] Hypotéza ohybu zpětné projekce vyvrácena testem (22. 8. 2026)
- [x] Kompenzace pohybu mezi snímky (`CorridorLocalizer.Reproject`), okno 400 ms (23. 8. 2026)
- [x] Sbíhání 11° vysvětleno jako nálevka v testovací mapě (24. 8. 2026)

[map-correlation-localization.md](map-correlation-localization.md), [BoundaryStraightnessTests.cs](../Src/ARBot.Common.Tests/Vision/BoundaryStraightnessTests.cs), [CorridorLocalizer.cs](../Src/ARBot.Common/Localization/CorridorLocalizer.cs) · DevLog [2026-08-22](devlog.md#2026-08-22), [2026-08-23](devlog.md#2026-08-23), [2026-08-24](devlog.md#2026-08-24), [2026-08-27](devlog.md#2026-08-27)

<a id="lok-kurz-koridor-prehlasovan-kompasem"></a>
### ✅ Korekci kurzu z koridoru přehlasuje kompas ~200:1

`lok-kurz-koridor-prehlasovan-kompasem` · vada · **hotovo** · nalezeno 22. 8. 2026 · vyřešeno 18. 9. 2026

Autor se ptal, proč korekce z koridoru chybu kurzu nezmenšila. Nejdřív se zjistilo, že kurz nebylo co opravovat (robot stál a virtuální IMU dává absolutní kurz s efektivní σ 0,1°). Když se chyba vnutila (`imubias=5,0`), koridor ji změřil správně (4,8°), ale fúze zůstala na 4,96°: informace kompasu při 100 Hz přehlasuje koridor ~200:1 a měkký gating navíc u velké chyby σ koridoru nafoukne (sebemařící). S oslabeným kompasem korekce funguje (4,76° → 0,58°). Důsledek pro robot: σ kompasu musí být podstatně větší než jeho krátkodobý šum, nebo musí bias kurzu přibýt do stavu EKF. Od 12. 9. má σ kompasu podlahu 5° a škrtí se na 1 Hz, takže 15. 9. výpočet ukázal, že poměr se překlopil na koridor ~150–1000:1 nad kompasem — je to ale výpočet, ne měření, a na zařízení to neběželo. ✅ **Změřeno na zařízení 18. 9. 2026** (první jízdy s `corridorsend=true`): fúze kurz z kompasu už neopisuje — `odhad − IMU yaw` je +0,39 ± 1,88° (dřív −0,01 ± 0,06°) a odhad je ke GPS kurzu blíž než kompas sám (v jízdních minutách −1,0 až −1,8° proti −2,7 až −4,7°). Koridor kurz reálně táhne; kolik přesně a s jakou σ, zůstává u `lok-koridor-merici-rezim` (σ proložení 4× optimistická, kadence 10/s).

- [x] Změřit poměr informace kompas : koridor v simulaci (~200:1) (22. 8. 2026)
- [x] Důkaz, že korekce kurzu funguje se slabým kompasem (22. 8. 2026)
- [x] Přepočet poměru po podlaze σ kompasu a škrcení na 1 Hz (koridor ~150–1000:1) (15. 9. 2026)
- [x] Změřit korekci kurzu z koridoru na zařízení — 18. 9.: `odhad − IMU yaw` z −0,01 ± 0,06° na +0,39 ± 1,88°, odhad ke GPS kurzu blíž než kompas (−1,0° proti −3°) (18. 9. 2026)

čeká na [lok-bias-senzoru-jako-stav-ekf](#lok-bias-senzoru-jako-stav-ekf) · [virtual-hw.md](virtual-hw.md), [map-correlation-localization.md](map-correlation-localization.md), [ekf-fusion.md](ekf-fusion.md) · DevLog [2026-08-22](devlog.md#2026-08-22), [2026-08-23](devlog.md#2026-08-23), [2026-09-15](devlog.md#2026-09-15), [2026-09-18](devlog.md#2026-09-18)

<a id="lok-sirkovy-nesouhlas-proti-filtru"></a>
### ✅ Šířkový nesouhlas vyskočil na 0,23 m — měřil se proti filtru, ne proti mapě

`lok-sirkovy-nesouhlas-proti-filtru` · vada · **hotovo** · nalezeno 23. 8. 2026 · vyřešeno 23. 8. 2026

Po kompenzaci pohybu mezi snímky vyskočil šířkový nesouhlas z 0,046 na 0,230 m a vypadalo to jako regrese. Nebyla: `MapWidth` ve zprávě není šířka z mapy, ale výstup filtru šířky, který se z měření učí a za rozšiřující se cestou trvale zaostává (šířka uzlu je maximum z okolních cest, takže na styku 1m a 3m cesty se úzká rozevírá). Kamera proti mapě souhlasí na centimetry. Přejmenováno, aby číslo dál nemystifikovalo, a zapsáno poučení: osm běhů téže konfigurace dalo p50 0,028–0,259 m, rozptyl mezi běhy je větší než zkoumaný rozdíl. Filtr šířky nahradil 15. 9. estimátor s verdiktem kvality (jiné téma).

- [x] Rozbor po cestách, test `NaRozsirujiciSeCeste_filtrTrvaleZaostava`, přejmenování `WidthDisagreement` (23. 8. 2026)

[map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-08-23](devlog.md#2026-08-23), [2026-08-24](devlog.md#2026-08-24)

<a id="lok-gps-kurz-do-fuze"></a>
### ✅ Kurz z GPS jako druhá absolutní reference kurzu

`lok-gps-kurz-do-fuze` · záměr · **hotovo** · nalezeno 25. 8. 2026 · vyřešeno 25. 8. 2026

Přijímač GPS hlásí kurz nad zemí a reálné drivery ho plnily, ale fúze ho nepoužívala vůbec a virtuální GPS ho ani nevysílala. Od 25. 8. jde `GPS/heading` do fúze jako druhá reference kurzu vedle kompasu (sigma roste s klesající rychlostí, jízda vzad vyloučená) a virtuální GPS ho hlásí se šumem v příčné složce rychlosti. Nový rozbor `ARBot.Analyze heading` měří rozpor `IMU yaw − GPS kurz` i bez ground truth, takže běží nad záznamy ze zařízení — právě jím se pak 7. 9. našel kurz VN100 o 24° vedle. Samo to ale chybu kurzu nezmění, dokud si kompas věří tisíckrát víc. Od 12. 9. je změřeno, že chyba kurzu z GPS není bílý šum (poctivá sigma by byla 29–83°) a že GPS jede 10 Hz, ne 5 — model sigmy trefuje realitu spíš náhodou.

- [x] Virtuální GPS hlásí kurz nad zemí (šum v příčné rychlosti, 12° při 0,5 m/s, 3,7° při 3 m/s) (25. 8. 2026)
- [x] 'Mapper bere `GPS/heading` do fúze (varianta A: druhá reference, žádné nové stavy)' (25. 8. 2026)
- [x] `ARBot.Analyze heading` i bez ground truth (`--nogt`), ověřeno proti známé odpovědi (25. 8. 2026)

[ekf-fusion.md](ekf-fusion.md), [imu-and-frames.md](imu-and-frames.md) · DevLog [2026-08-25](devlog.md#2026-08-25), [2026-09-07](devlog.md#2026-09-07), [2026-09-12](devlog.md#2026-09-12)

<a id="lok-korelace-mericidlo-chyba-fuze"></a>
### ✅ Měřidlo poctivosti σ účtovalo korelátoru vlastní chybu fúze

`lok-korelace-mericidlo-chyba-fuze` · vada · **hotovo** · nalezeno 25. 8. 2026 · vyřešeno 25. 8. 2026

Korelátor hlásí posun proti odhadu pózy, takže správná odpověď proti tuze posunuté mapě není konstantní posun mapy, ale posun mapy plus chyba fúze — a ten druhý člen (p50 0,105 m) v měřidle `ARBot.Analyze sigma` chyběl. Po jeho odečtení padly dvě vedené vady: „σ je 1,28–1,43× optimistická" (zbylo 1,03–1,17× a přísnější `sd(z)` = 0,78–0,87 říká, že je naopak ~1,25× konzervativní) a „systematické vychýlení +0,10 m" (bylo to vychýlení fúze, hlášené správně). Aby se póza nedohledávala podle razítka (nepřežije seek), nese ji zpráva `MapCorrelationMsg` (verze 5); rozdíl obou cest je 0–4 mm, takže dřívější závěry platí. Konzervativní σ se vědomě neopravuje — zmenšit ji znamená zvětšit autoritu korelátoru proti GPS, což gatují tři podmínky. Čistě softwarové, ověřeno testy a pěti běhy v simulaci.

- [x] Chyba fúze odečtena v `sigma`, metrika `sd(z)` místo poměru souhrnů (25. 8. 2026)
- [x] `MapCorrelationMsg` verze 5 s pózou, proti které se korelovalo (25. 8. 2026)

[map-correlation-localization.md](map-correlation-localization.md), [MapCorrelationMsg.cs](../Src/ARBot.Common/Logs/MapCorrelationMsg.cs) · DevLog [2026-08-25](devlog.md#2026-08-25)

<a id="lok-odometrie-pod-stopem"></a>
### ✅ Robot na mapě poskakoval, protože fúze pod nouzovým zastavením zahazovala odometrii

`lok-odometrie-pod-stopem` · vada · **hotovo** · nalezeno 27. 8. 2026 · vyřešeno 27. 8. 2026

Po přijetí QR kódu se poloha robota na mapě rozjela o metry. Nebyla to mise: mapper pod drženým nouzovým zastavením odometrii zahazoval, takže fúze ve stání neměla žádnou vazbu na rychlost a polohu tahal šum GPS. Autor zdůvodnění té výjimky vyvrátil (motory jsou pod stopem řízené pozičně, kola nemohou hlásit nic než nulu; odnesení robota je možné i bez stopu), výjimka byla zrušena a odometrie teče normálně. Odhalilo to zároveň, že chybový rámec motorového driveru se tváří jako měření (samostatné téma).

- [x] Změřit, že fúze je za jízdy zdravá (chyba pózy p50 0,16 m) a problém je jen pod stopem (27. 8. 2026)
- [x] Zrušit výjimku pro odometrii pod stopem (27. 8. 2026)

[rozhodnutí 27. 8. 2026](decisions.md), [ekf-fusion.md](ekf-fusion.md) · DevLog [2026-08-27](devlog.md#2026-08-27)

<a id="lok-hlaska-zahozene-mereni"></a>
### ✅ Hláška fúze o zahozeném měření vinila okno historie, které za to nemohlo

`lok-hlaska-zahozene-mereni` · vada · **hotovo** · nalezeno 1. 9. 2026 · vyřešeno 1. 9. 2026

Autor si všiml, že fúze hlásí zahození odometrie „starší než okno historie", ačkoli měření bylo zpožděné jen 7 ms a okno je 3 s. Testem se potvrdilo, že chování filtru je správné: po inicializaci se zahodí každé měření starší než základ filtru, a to bez ohledu na velikost okna. Vadná byla jen hláška — vinila okno a slovem „opožděno" posílala hledání chyby do doručování měření místo k základu filtru. Hláška rozlišuje obě situace a test hlídá i její znění.

- [x] Rozlišit „za oknem historie" od „před základem filtru" a opravit znění (1. 9. 2026)

[ekf-fusion.md](ekf-fusion.md) · DevLog [2026-09-01](devlog.md#2026-09-01)

<a id="lok-ekf-sigma-telemetrie"></a>
### ✅ Nejistota EKF tekla do záznamu od začátku, ale nikde nebyla vidět

`lok-ekf-sigma-telemetrie` · záměr · **hotovo** · nalezeno 2. 9. 2026 · vyřešeno 2. 9. 2026

Na dotaz, jak pozorovat vývoj filtru, se ukázalo, že kovariance stavu jde do streamu i do záznamu od začátku, jen z ní telemetrie nedělala ani jeden sloupec. Přibylo šest sloupců (σ polohy, σx, σy, σ kurzu, σv, σω) do tabulky i grafu; protože se nic nového neposílá, funguje to i nad staršími záznamy. Chybějící nebo rozpadlá kovariance dá prázdno, ne nulu. Zjištění od autora: `EKFStepMsg` není cesta k vnitřku filtru — engine je asynchronní s okny historie, „jeden krok" v něm neexistuje.

- [x] Šest sloupců σ, výpočet v `StateSigma` s testy (2. 9. 2026)

[ekf-fusion.md](ekf-fusion.md), [telemetry-view.md](telemetry-view.md) · DevLog [2026-09-02](devlog.md#2026-09-02)

<a id="lok-gps-kvalita-fixu"></a>
### ✅ Fúze brala každý GPS fix s toutéž sigmou

`lok-gps-kvalita-fixu` · vada · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 6. 9. 2026

Na náhledu ujela poloha o 570 m, zatímco robot stál. Fúze brala každý fix s `IsFixed` a vždy se sigmou 1,5 m, ačkoli zpráva nese počet družic i DOP; u-blox navíc jen přetypovával `fixType`, takže i samotný mrtvý odhad bez družic vypadal jako platný fix. Teď se sigma násobí DOP (`gpsdopsigma=`) a volná brána (`gpsminsat=`, `gpsmaxdop=`) odmítne nesmysl — neznámá hodnota projde. Kvalita GPS je i na stránce náhledu. Na stojícím robotu ověřeno: dokud fix projde, odhad ujede ~1 m za 30 s; po odmítnutí zamrzne na centimetr. Příčina těch 570 m potvrzená není (robot byl v době opravy vypnutý).

- [x] Sigma podle DOP, brána na družice a DOP, oprava mapování `fixType` u u-bloxu (6. 9. 2026)
- [x] Kvalita GPS na stránce náhledu (6. 9. 2026)
- [x] Ověřeno na stojícím robotu (brána zastaví ujíždění) (6. 9. 2026)

[ekf-fusion.md](ekf-fusion.md), [rozhodnutí 6. 9. 2026](decisions.md) · DevLog [2026-09-06](devlog.md#2026-09-06)

<a id="lok-magmodel-deklinace"></a>
### ✅ Deklinace se nezapočítávala nikde — model pole se teď nastaví sám

`lok-magmodel-deklinace` · záměr · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 12. 9. 2026

Yaw z VN100 byl azimut k magnetickému severu: registr 21 měl východní složku 0, model pole vypnutý a náš kód deklinaci nepřidával. Jediná metoda, která by model zapnula, nikdy nemohla fungovat (čtecí příkaz místo zápisu, oddělovač tisíců v čísle, radiány místo stupňů). Od 8. 9. `MagModelInit` (`magmodel=`) zapíše registr 83 jednorázově po prvním kvalitním fixu; deklinace i sklon reference se dopočítají z WMM. Na živém senzoru 12. 9. potvrzeno, že se deklinace aplikuje (3,42°) — vestavěný model VN je ale zastaralý o ~1,9°.

- [x] `VnCommands` + `IMagneticModel`, oprava tří chyb v `SetModelParams` (6. 9. 2026)
- [x] `MagModelInit` po prvním kvalitním fixu (`magmodel=`) (8. 9. 2026)
- [x] Ověřeno čtením registrů 83 a 21 na živém senzoru (12. 9. 2026)

[imu-and-frames.md](imu-and-frames.md) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-08](devlog.md#2026-09-08), [2026-09-12](devlog.md#2026-09-12)

<a id="lok-t265-napojeni"></a>
### ✅ T265 běžela, ale její data neměla kam téct

`lok-t265-napojeni` · vada · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 12. 9. 2026

Kamera se zakládala, otáčela pipeline a soupeřila na USB, ale `BuildSensorSources` ji nikdy nedrátoval — v záznamu po ní nebyla ani stopa a na stránce vypadala jako porucha. Napojení není jednořádkové: bez magnetometru je její yaw o neznámou konstantu vedle severu, proto jde do fúze jen úhlová rychlost z rozdílu yaw na nepřekrývajícím se okně 0,5 s (`IMUState.HasAbsoluteHeading`, verze 3). Na robotu pak nesla 19 % informace o úhlové rychlosti. Absolutní kurz z ní nevznikne bez offsetu yaw jako stavu EKF — a od 14. 9. je to jedno, protože autor T265 kvůli výpadkům kamer odpojil natrvalo.

- [x] Napojení jako relativní yaw → úhlová rychlost, filtr zdrojů v `ARBot.Analyze` (6. 9. 2026)
- [x] Podíl T265 na informaci změřen na záznamu z robota (12. 9. 2026)

[ekf-fusion.md](ekf-fusion.md), [hardware.md](hardware.md), [rozhodnutí 14. 9. 2026 (T265 odpojena)](decisions.md) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-12](devlog.md#2026-09-12), [2026-09-14](devlog.md#2026-09-14)

<a id="lok-koridorsend-nebyl-vypnuty"></a>
### ✅ Měřicí režim koridoru nebyl měřicí — `corridorsend` v profilu chybí

`lok-koridorsend-nebyl-vypnuty` · vada · **hotovo** · nalezeno 17. 9. 2026 · vyřešeno 18. 9. 2026

`lok-koridor-merici-rezim` i CLAUDE.md tvrdí, že provozní profil běží s `corridorsend=false`, tedy „plná zátěž, nulový vliv na řízení", a týž úkol vede jako OTEVŘENÝ krok „`corridorsend=true` — až po opravě magnetometru a přiřazení hrany". V `config/pi-provoz.cfg` ale ten řádek **vůbec není**, takže platí default `true` a korekce se posílaly do fúze celou dobu. Potvrzuje to i účinná konfigurace v záznamech ze 17. 9. (`corridorsend=true (default)`) a report: *poslana pricna korekce 16 z 16 Ok, poslana korekce kurzu 16 z 16 Ok*. ⚠️ **Podstatné je, co tím prošlo:** těch 16 korekcí kurzu odešlo v běhu, kde měl koridor `abs nesouhlas kurzu` p50 **23,4°** (nezkalibrovaný kompas, viz `hw-zelezo-od-kabelu-kamer`) — tedy přesně ta situace, kvůli které měla brána zůstat zavřená. `GateMode.Soft` je nezahodí, jen odtlumí. Je to vada **dokumentace proti skutečnosti**, ne v kódu: chybí jeden řádek v profilu. ⚠️ A ukazuje na obecnější past — „výchozí hodnota je bezpečná" tu neplatí, protože default `corridorsend` je `true`; bezpečný stav se musí do profilu napsat, ne předpokládat. ✅ **Uzavřeno 18. 9. 2026 opačně, než nález navrhoval:** bezpečný stav se do profilu nezapsal — zapsal se ostrý. Autor `corridorsend=true` zapnul vědomě 17. 9. (`a44b4f4`) a první jízdy 18. 9. ukázaly, že koridor za jízdy dává měření v každém druhém cyklu a fúze se jím řídí (viz `lok-koridor-hranova-lokalizace`). Odstavec v `CLAUDE.md` o „měřicím režimu" přepsán.

- [x] Nález (účinná konfigurace v záznamu proti tvrzení v úkolu i v CLAUDE.md) (17. 9. 2026)
- [x] Vyřešeno OPAČNĚ: autor 17. 9. zapsal do profilu `corridorsend=true` vědomě (commit `a44b4f4`); dokumentace (`CLAUDE.md`, registr) srovnána 18. 9. (17. 9. 2026)
- [x] Projít profil, jestli takhle „nezapsaným defaultem" nevisí i jiná brána — 18. 9.: `mapcorr` v profilu není a jeho default je `false` (bezpečný), `corridorstd/headingstd/hz` 0 = bez odtlumení (zapsané jako komentář); jediná brána s ostrým defaultem byla `corridorsend` (18. 9. 2026)

[pi-provoz.cfg](../config/pi-provoz.cfg), [map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-09-17](devlog.md#2026-09-17), [2026-09-18](devlog.md#2026-09-18)

<a id="lok-sirka-koridoru-plus-18-mm"></a>
### ❌ Šířka koridoru vychází o 18 mm větší — nejmenší kvadráty sledují průměr, medián sedí

`lok-sirka-koridoru-plus-18-mm` · vada · **zamítnuto** · nalezeno 24. 8. 2026 · vyřešeno 18. 9. 2026

Nad rovnou mapou proti pravdě vyšla šířka 2,018 m místo 2,000 (filtr šířky tu odchylku schovával devítinásobně). Surové body chybu nemají — medián sedí na okraji — ale rozdělení je zešikmené s dlouhým chvostem ven z cesty, a proložení nejmenšími kvadráty sleduje průměr. Příčinou je drsnost trávy v simulaci: bez ní je vychýlení −1,7 mm, při 0,12 m už +54 mm. Proložení cílící medián (`LineFitMode.OrthogonalL1`) srazí vychýlení na 1,4 mm (−92 %) a klesne i rozptyl; Huber s MAD je slabší, Tukey nestabilnější. Nezapnuto — autor 27. 8. rozhodl počkat na měření na reálné kameře, protože to zešikmení je artefakt simulace a na skutečné trávě se může ztratit v šumu. Zapínat léčbu vady, o které se neví, jestli na železe existuje, by znamenalo ladit simulaci. ❌ **Zamítnuto 18. 9. 2026:** na reálných datech (`20260918-155329.rec`, 10 340 dvojic) dávají všechny varianty proložení týž výsledek v rámci rozpětí opakování; pravda k dispozici není, ale rozdíl, který by šlo obhajovat, nevzniká. `LeastSquares` zůstává. Otevře se znovu jen se záznamem s pravdou.

- [x] Nález proti pravdě (`corridorfit --truewidth --axisy`) (24. 8. 2026)
- [x] Příčina dohledána (`edgebias`, zešikmení z drsnosti trávy) (24. 8. 2026)
- [x] `OrthogonalL1` / `OrthogonalHuber` / `OrthogonalTukey` naměřeny, `LeastSquares` zůstává (24. 8. 2026)
- [x] Rozhodnutí autora odložit do měření na HW (27. 8. 2026)
- [x] Změřit na záznamu ze skutečné kamery — 18. 9.: `corridorfit --limit=0 --rep=3` nad 10 340 dvojicemi, LS/L1/Huber/Tukey nerozlišitelné (Ok 3 176–3 232, rezidua 0,076–0,079 m, vychýlení proti filtru −0,006 až −0,010 m u všech) (18. 9. 2026)

[map-correlation-localization.md](map-correlation-localization.md), [LineFit.cs](../Src/ARBot.Common/Common/LineFit.cs), [EdgeBiasReport.cs](../Src/ARBot.Analyze/EdgeBiasReport.cs) · DevLog [2026-08-24](devlog.md#2026-08-24), [2026-08-27](devlog.md#2026-08-27), [2026-09-18](devlog.md#2026-09-18)

## Navigace po mapě

<a id="nav-zdroj-osm-dat"></a>
### ⬜ Odkud se berou a jak se aktualizují výřezy `.osm`

`nav-zdroj-osm-dat` · záměr · **otevřeno** · nalezeno 4. 8. 2026

Síť cest se čte z výřezů OpenStreetMap v adresáři `OSM/`. Soubory jsou verzované v gitu, ale stahují se ručně a nikde není zapsáno, čím, z jaké oblasti a kdy — takže výřez nejde obnovit ani říct, jak je starý proti skutečnosti. Otevřené je i to, kdy se má síť (`RoadNetwork`) a cílové pole (`GoalField`) stavět a přeplánovávat; tady rozhodnutí padlo už v návrhu (síť vlastní runtime, jedno pole na misi). Zapsáno jako další krok hned při integraci OsmNav, od té doby jen upřesněno („`OSM/` je verzované, otevřené zůstává odkud").

[osm-nav.md](osm-nav.md), [global-navigation-runtime.md](global-navigation-runtime.md), [OSM/](../OSM) · DevLog [2026-08-04](devlog.md#2026-08-04), [2026-08-18](devlog.md#2026-08-18)

<a id="nav-uzavreni-hran-pres-restart"></a>
### ⬜ Uzavřené hrany sítě a stav lokalizace nepřežijí restart

`nav-uzavreni-hran-pres-restart` · záměr · **otevřeno** · nalezeno 12. 8. 2026

Globální navigace si za jízdy uzavírá hrany sítě, které se ukázaly neprůjezdné, a korelace s mapou si ladí korekci pózy — obojí žije jen v paměti procesu. Po pádu nebo restartu aplikace (při soutěžní jízdě reálná situace) začíná robot od nuly: může se vrátit do téže slepé uličky a filtr startuje od GPS. Je to jiná otázka než přežití mise Robotour, které autor 27. 8. 2026 vědomě zrušil (mise se po restartu spouští od začátku) — tady jde o stav navigační vrstvy (uzavření mají trvalou identitu `EdgeKey`, takže by se ukládat daly) a lokalizace, ne o fázi mise. Zda to vůbec dělat, rozhodnuto není.

[global-navigation-runtime.md](global-navigation-runtime.md), [map-correlation-localization.md](map-correlation-localization.md), [rozhodnutí 27. 8. (mise restart přežít nemusí)](decisions.md), [EdgeClosure.cs](../Src/ARBot.Common/Maps/OsmNav/Navigation/EdgeClosure.cs) · DevLog [2026-08-27](devlog.md#2026-08-27)

<a id="nav-prurez-koridorem"></a>
### ⬜ Detektor přehrazení bez průřezu koridorem (fáze 4b)

`nav-prurez-koridorem` · záměr · **otevřeno** · nalezeno 13. 8. 2026

Nejsilnější důkaz, že je cesta přehrazená, je „všechny buňky napříč koridorem jsou blokované" — plán nevznikne a globální vrstva hranu zavře. Lokální navigace má na to připravený parametr (`corridorWidthM` v `SetGoal`), ale samotný test průřezu nedělá, takže detektor přehrazení rozhoduje jen z toho, že plán nevznikl, a nerozliší přehrazenou cestu od dočasně neznámé mapy. Zapsáno jako zbývající krok globální navigace 13. 8., od té doby nedotčeno; rozhodnutí odložit není zapsané.

- [ ] Průřez napříč koridorem v `LocalNavigatoru` → `CorridorBlocked`

[global-navigation-runtime.md (fáze 4b)](global-navigation-runtime.md), [LocalNavigator.cs](../Src/ARBot.Common/Occupancy/LocalNavigator.cs) · DevLog [2026-08-13](devlog.md#2026-08-13)

<a id="nav-delka-trasy"></a>
### ⬜ Délka trasy k cíli nepočítala poslední úsek

`nav-delka-trasy` · vada · **otevřeno** · nalezeno 26. 8. 2026

Při vložení cíle do sítě se rozřízne nejbližší hrana; obě půlky dostávaly správnou cenu, ale nulovou délku, takže „vzdálenost do cíle" v záznamu i v panelu mise byla podhodnocená o celý poslední úsek (test: 100 m místo 130 m). Opraveno 26. 8. Zůstává známá nepřesnost na druhém konci: plánovač vrací celé hrany, takže první hrana se započítá i tou částí, která je už za robotem — délka je na startu nadhodnocená, u cíle přesná.

- [x] Délky půlek rozříznuté hrany u cíle (26. 8. 2026)
- [ ] Odečíst část první hrany za robotem (trasa jako polyline, ne seznam hran)

[global-navigation-runtime.md](global-navigation-runtime.md) · DevLog [2026-08-26](devlog.md#2026-08-26), [2026-08-27](devlog.md#2026-08-27)

<a id="nav-osm-josm-action-delete"></a>
### 🧪 Mapa z JOSM nese smazané cesty (`action='delete'`) a čtečka je brala jako živé

`nav-osm-josm-action-delete` · vada · **v kódu, na HW neověřeno** · nalezeno 18. 9. 2026 · vyřešeno 18. 9. 2026

Při přepnutí provozního profilu na soutěžní mapu `OSM/Robotour2026-ver1.osm` (večer před Robotourem) se ukázalo, že soubor uložený z JOSM obsahuje i objekty, které v něm autor smazal — zůstávají v XML s `action='delete'` až do uploadu nebo Purge. V té mapě je to 142 z 195 cest a 2 127 uzlů, z cest s `highway` 57 z 95. `OsmXmlReader` atribut `action` neznal, takže by robot navigoval po síti, kterou autor v mapě odstranil, a z aplikace by to nebylo poznat. Čtečka teď smazaný uzel, cestu i relaci přeskočí (subtree odkonzumuje), `action='modify'` bere normálně. Mapa po opravě: 209 uzlů, 226 hran, načteno v simulaci. Totéž platí pro `modrany.osm`, `modrany1.osm` a `modrany_small.osm`; mapy z Overpassu atribut nemají.

- [x] Nález při kontrole soutěžní mapy (počty smazaných objektů) (18. 9. 2026)
- [x] `OsmXmlReader` přeskakuje `action='delete'`, test `Read_SkipsJosmDeletedObjects` (18. 9. 2026)
- [x] Načtení soutěžní mapy v simulaci (209 uzlů, 226 hran) (18. 9. 2026)
- [ ] Nasadit binárku na zařízení spolu se soutěžním profilem

[osm-nav.md](osm-nav.md), [OsmXmlReader.cs](../Src/ARBot.Common/Maps/OsmNav/Osm/OsmXmlReader.cs) · DevLog [2026-09-18](devlog.md#2026-09-18)

<a id="nav-recovery-manevr"></a>
### ⏸ Recovery manévr při záseku

`nav-recovery-manevr` · záměr · **odloženo** · nalezeno 13. 8. 2026

Když detektor záseku zjistí, že robot nepostupuje, umí dnes jen počkat a pak uzavřít hranu sítě; couvnutí nebo otočka na místě v lokální vrstvě neexistuje. Zapsáno jako zbývající krok globální navigace už 13. 8.; 27. 8. při projití seznamu otevřených úkolů autor rozhodl, že zastavit a ohlásit je zatím přijatelná odpověď — priorita nízká.

[global-navigation-runtime.md](global-navigation-runtime.md) · DevLog [2026-08-13](devlog.md#2026-08-13), [2026-08-27](devlog.md#2026-08-27)

<a id="nav-osmnav-integrace"></a>
### ✅ Modul OSM navigace převzatý do ARBot.Common

`nav-osmnav-integrace` · záměr · **hotovo** · nalezeno 4. 8. 2026 · vyřešeno 4. 8. 2026

Navigace nad OpenStreetMap (graf, routing, cost-to-goal, kolize) existovala jako samostatný projekt z dřívějška. Přenesla se do `ARBot.Common/Maps/OsmNav` včetně 74 testů (převod xUnit na NUnit), sjednotily se geotypy na systémové `LLA` v radiánech a duplicitní bodové typy (`Point2D`, `Point2DF`) na jeden. Čistě algoritmická práce bez hardwaru, doména je popsaná v osm-nav.md.

- [x] Přenos a namespace, testy na NUnit (4. 8. 2026)
- [x] Sjednocení `Point2D` a geo typů na `LLA` (4. 8. 2026)
- [x] Doménová dokumentace osm-nav.md (4. 8. 2026)

[osm-nav.md](osm-nav.md), [rozhodnutí 4. 8. 2026](decisions.md) · DevLog [2026-08-04](devlog.md#2026-08-04)

<a id="nav-globalni-navigace-runtime"></a>
### ✅ Globální navigace po síti cest v runtime

`nav-globalni-navigace-runtime` · záměr · **hotovo** · nalezeno 11. 8. 2026 · vyřešeno 14. 9. 2026

Vrstva nad lokálním plánovačem: dostane cíl v zeměpisných souřadnicích, drží trasu po OSM síti a lokální vrstvě podává „mrkev" - poslední bod trasy uvnitř lokální mapy. Postup se měří jedním potenciálem (zbytek hrany plus cost-to-goal), tři detektory (nehýbu se, bloudím, přehrazeno) hrany zdražují a zavírají, uzavření přežije změnu cíle. Fáze 0–4 hotové 13. 8., na zařízení jela poprvé 14. 9. 2026 s misí Track (a hned ukázala nedosažitelné mrkve a jeden `NoRoute`). Průřez koridorem (fáze 4b) se nerealizoval; recovery manévr je odložený jako samostatné téma.

- [x] Návrh a tři revize s autorem (11. 8. 2026)
- [x] Fáze 2–3: `GlobalNavigator`, mrkev, trasa v mapě (13. 8. 2026)
- [x] Fáze 4: detektory záseku a uzavírání hran (13. 8. 2026)
- [x] Jízda po síti na zařízení (Hviezdoslavova, mise Track) (14. 9. 2026)

čeká na [lok-gps-odometrie-do-fuze](#lok-gps-odometrie-do-fuze) · [global-navigation-runtime.md](global-navigation-runtime.md), [osm-nav.md](osm-nav.md) · DevLog [2026-08-11](devlog.md#2026-08-11), [2026-08-12](devlog.md#2026-08-12), [2026-08-13](devlog.md#2026-08-13), [2026-09-14](devlog.md#2026-09-14)

<a id="nav-vzdalenosti-wgs84"></a>
### ✅ Délky hran vycházely o 0,3 % kratší než svět

`nav-vzdalenosti-wgs84` · vada · **hotovo** · nalezeno 16. 8. 2026 · vyřešeno 16. 8. 2026

Graf sítě měřil vzdálenosti haversinem na kouli, zatímco lokální rovina a fúze počítají na elipsoidu WGS84 — na syntetické mapě vyšlo 9,969 m místo 10,000 m, a týkalo se to všech map. `GreatCircle` teď počítá geodetiku na zvoleném elipsoidu; projekce na úsečku vědomě zůstala na kouli, protože se tam měřítko vykrátí. Při tom se přejmenovala vlastnost, která tvrdila excentricitu a byla zploštění.

[rozhodnutí 16. 8. 2026](decisions.md) · DevLog [2026-08-16](devlog.md#2026-08-16)

<a id="nav-profil-robot-osm"></a>
### ✅ Mapa se načítala profilem chodce — bez cyklostezek, se schody a skrz zamčené branky

`nav-profil-robot-osm` · vada · **hotovo** · nalezeno 1. 9. 2026 · vyřešeno 2. 9. 2026

Autor si v náhledu všiml, že z OSM mizí modrá čárkovaná cesta. Byla to cyklostezka, kterou profil chodce nezná — a týmž profilem se stavěla i navigační síť, po které robot jede (na mapě Hájů 9 cyklostezek pryč). Opačným směrem profil přijímal schody, které kolový robot nevyjede. Vznikl profil `Robot()` a druhý den se opravilo i to, že uzel `barrier=gate` s `access=private` nebo `locked=yes` plánovač bral jako průchozí. Závory a sloupky se záměrně neblokují (rozchod 0,41 m), což je úsudek, ne měření.

- [x] Profil `TravelProfile.Robot()` pro obě cesty načítání (1. 9. 2026)
- [x] Zamčená a soukromá branka blokuje uzel (2. 9. 2026)

[osm-nav.md](osm-nav.md) · DevLog [2026-09-01](devlog.md#2026-09-01), [2026-09-02](devlog.md#2026-09-02)

## Lokální mapa a plánování

<a id="lp-vykon-retezu-na-arm"></a>
### ⬜ Výkon řetězu hloubka → grid → EDT → A* na ARM není změřený

`lp-vykon-retezu-na-arm` · záměr · **otevřeno** · nalezeno 10. 8. 2026

Occupancy grid, distanční transformace a A* běží v řídicí smyčce každý takt (10×/s). Při návrhu 10. 8. 2026 se ověření výkonu na cílové desce zapsalo jako zbývající krok a od té doby se řetěz měřil jen na Windows. Na Orange Pi je změřená vizuální část (zpracování snímku, síť na CPU i NPU) a obsazenost taktu se sleduje souhrnně (`PerfMsg`), ale rozpad na integraci + EDT + A* jako čísla z ARM chybí — takže se neví, kolik z periody 100 ms tam ten řetěz bere.

- [ ] Změřit integraci + EDT + A* na Orange Pi (doba na takt, podíl periody)

[occupancy-and-local-planning.md](occupancy-and-local-planning.md), [perf-monitoring.md](perf-monitoring.md), [LocalPathPlanner.cs](../Src/ARBot.Common/Occupancy/LocalPathPlanner.cs), [ClearanceField.cs](../Src/ARBot.Common/Occupancy/ClearanceField.cs) · DevLog [2026-08-10](devlog.md#2026-08-10)

<a id="lp-koridor-trasy-jako-cena"></a>
### ⬜ Koridor trasy jako měkká cena v lokálním A*

`lp-koridor-trasy-jako-cena` · záměr · **otevřeno** · nalezeno 12. 8. 2026

Lokální plánovač dostane z trasy po síti cest jen jediný bod (mrkev), takže tvar cesty do ceny A* nevstupuje. Původní obava „robot sjede z cesty všude, kde je vedle geometricky volno" se 27. 8. 2026 ukázala lichá — cesty se drží sám díky sémantickému kanálu z vize (mimo cestu = blokováno) a ceně neznáma, a přibyly testy, které to přibíjejí. Otevřený zbytek je užší: kde vize okraj cesty nevidí, mapa se ho nezastane — měkká preference blízkosti osy cesty (šířka z `Node.Width`) by to doplnila. Nikdo to zatím nepotřeboval.

- [x] Ověřeno testy, že plán drží cestu díky sémantice a nebere zkratku přes neznámo (27. 8. 2026)
- [ ] Měkká cena podle vzdálenosti od osy cesty v A* (jen kde vize okraj nevidí)

[global-navigation-runtime.md](global-navigation-runtime.md), [occupancy-and-local-planning.md](occupancy-and-local-planning.md), [LocalPathPlanner.cs](../Src/ARBot.Common/Occupancy/LocalPathPlanner.cs) · DevLog [2026-08-27](devlog.md#2026-08-27)

<a id="lp-path-following-stara-cisla"></a>
### ⬜ Tabulky v `path-following.md` počítají se starými limity (0,8 m/s a 0,2 m/s²)

`lp-path-following-stara-cisla` · vada · **otevřeno** · nalezeno 18. 9. 2026

Našlo se při psaní webového článku o regulátoru, kde se čísla nepřebírala z dokumentu, ale počítala znovu z `Profile.cs`. Dokument uvádí u tabulek „hodnoty z `Profile`“, ale ty hodnoty tam dnes nejsou: `MaxAllowedSpeed` je 1,2 m/s (provozní profil 1 m/s) a `MaxAcceleration` 0,5 m/s², kdežto tabulky oblouk-vs-klotoida i lookahead počítají s 0,8 a 0,2. Důsledky nejsou kosmetické — úhlové zrychlení vychází 2,44 rad/s² místo 0,98, náběh rotace 3,2° místo 8° a nejhorší případ (kde se potkává limit otáčení s `v_max`) leží na ~32°, ne na ~40°. Závěry tím nepadají (náběh je pořád malý proti běžné zatáčce, chyba oblouku hluboko pod rezervou 1 cm), ale konkrétní čísla v obou tabulkách neplatí. Je to táž třída vady jako `maxspeed=1` v `pi-freerun.cfg`, kde komentář popisoval počáteční hodnotu, ačkoli se strop mezitím zvedl — autoritativní je kód, dokument se zapomněl přepsat.

- [ ] Přepočítat obě tabulky v `path-following.md` z dnešního `Profile.cs`
- [ ] Zvážit, jestli jde hlídat testem (jako `ProfilyBezpecnostTests` u stropu rychlosti)

[path-following.md](path-following.md), [Profile.cs](../Src/ARBot.Common/Configuration/Profile.cs) · DevLog [2026-09-18](devlog.md#2026-09-18)

<a id="lp-zasek-v-blokovane-mape"></a>
### ⬜ Robot 18. 9. dvakrát stál minuty před blokovanou lokální mapou — popsané, ne vysvětlené

`lp-zasek-v-blokovane-mape` · vada · **otevřeno** · nalezeno 18. 9. 2026

`20260918-154028.rec`: po dvou celých kolech Tracku robot na cestě k bodu 1/3 od ~320 s zpomalil na 0,05–0,12 m/s a od 380 s stál 7 minut do konce záznamu — plán délky 5 cm, potvrzeně volno 0,00 m, `Blocked` ~50 % buněk, stavy `AlreadyAtGoal`/`EscapingBlocked`, 4× „NOUZOVE ZASTAVENI - kolize 0,00 m“, hraniční body z kamer 1,7–2,8 m před robotem (něco tam bylo). Reset gridu při zotavení kamer (409 s) nepomohl — blokace se vrátila. `20260918-155329.rec`: po volbě mise 50–120 s `EscapingBlocked` s nouzovým zastavením v 59–85 % taktů, pak se rozjel. Co bylo před robotem, záznam bez snímků na obrazovce neříká; může to být skutečná překážka, tráva, do které mise dovedla, nebo mapa rozmazaná stáním. Vedlejší efekt pro koridor: ve stání nevzniká (0,6 % cyklů), takže celkové procento `Ok` v záznamu je číslo o stání, ne o koridoru.

- [x] Nález (`ARBot.Analyze localplan --from=320`, `corridor`, `log`) (18. 9. 2026)
- [ ] Podívat se na snímky z kamer v čase 320–400 s (View) a říct, co robota zastavilo
- [ ] Rozhodnout, jestli je to `lp-unik-z-blokovane-bunky` / `lp-filtr-izolovanych-bunek`, nebo nová vada

[map-correlation-localization.md (sekce 18. 9. 2026)](map-correlation-localization.md), [occupancy-and-local-planning.md](occupancy-and-local-planning.md) · DevLog [2026-09-18](devlog.md#2026-09-18)

<a id="lp-omega-dif-faktor-a-znamenko"></a>
### 🧪 Robot by zatáčel dvakrát rychleji, než regulátor chce

`lp-omega-dif-faktor-a-znamenko` · vada · **v kódu, na HW neověřeno** · nalezeno 12. 8. 2026 · vyřešeno 12. 8. 2026

Převod úhlové rychlosti na příkaz motorům bral `dif` jako rozdíl rychlostí kol, jenže driver ho k jednomu kolu přičítá a od druhého odečítá - je to offset na kolo, správně s polovinou rozchodu. Shodly se na tom tři nezávislé zdroje (předchozí generace robotu, profil pohybu, skript řadiče); dva existující testy starý faktor kódovaly. Znaménko rotace je jiná otázka a z kódu se rozhodnout nedá (asymetrická negace ve skriptu, záleží, které kolo je motor 1) - má se změřit na robotu malým `+ω` ve stoje. Naslepo se neopravuje: otočené znaménko znamená zatáčet od dráhy místo k ní.

- [x] Faktor 1/2 v `ControlLoop`, test `RotationSpeed_ToDif_IsHalfWheelBase` (12. 8. 2026)
- [x] Znaménko odometrického ω potvrzeno předchozí generací (12. 8. 2026)
- [ ] Změřit znaménko příkazové rotace na zařízení

[path-following.md](path-following.md) · DevLog [2026-08-12](devlog.md#2026-08-12)

<a id="lp-regulator-zapadka-lookahead"></a>
### 🧪 Robot jel 0,1 m/s, i když bylo povoleno 1,2 m/s

`lp-regulator-zapadka-lookahead` · vada · **v kódu, na HW neověřeno** · nalezeno 14. 8. 2026 · vyřešeno 14. 8. 2026

Mapa, obálka i plánovač pouštěly plnou rychlost, ale regulátor vydával 0,05 m/s. Omezovač váže dopřednou rychlost na vzdálenost k lookahead bodu a dobu dorovnání rotace - a lookahead se počítal z aktuální rychlosti, takže při nízké rychlosti seděl na podlaze 0,15 m a strop zůstával nízký: západka, ze které se soustava sama nedostane. Dvě dřívější hypotézy padly, protože stály na offline testu místo na měření za běhu. Podle návrhu autora se teď míří na nejbližší uzel dráhy před robotem (uzly blíž než lookahead se přeskakují); bez opravy dosáhne test 0,19 z 0,80 m/s. Na zařízení se to samostatně nepotvrdilo - venku 7. 9. brzdil robota jiný omezovač (odstup od překážky).

- [x] Diagnostika po stupních (grid, obálka, plánovač, regulátor) (14. 8. 2026)
- [x] Cíl řízení = nejbližší uzel dráhy, přeskok blízkých uzlů, dva regresní testy (14. 8. 2026)
- [ ] Potvrdit na zařízení, že robot dosáhne povolené rychlosti

[path-following.md](path-following.md) · DevLog [2026-08-14](devlog.md#2026-08-14), [2026-09-07](devlog.md#2026-09-07)

<a id="lp-unik-z-blokovane-bunky"></a>
### 🧪 Únik z blokované buňky pod robotem

`lp-unik-z-blokovane-bunky` · záměr · **v kódu, na HW neověřeno** · nalezeno 18. 8. 2026 · vyřešeno 18. 8. 2026

Robot v záznamu 5 s hlásil `RobotBlocked`, ačkoli stál na okraji cesty, ne u překážky — buňka pod ním byla podle barvy jistě mimo cestu a nejbližší průjezdná ležela 5 cm vedle. Relaxace gridu se zamítla (kamera buňku pod sebou nikdy neuvidí). Únik vede přes semanticky blokované buňky, přes geometricky blokované nikdy, nejvýš 1,5 m, a uváznutí není selhání plánu, aby nezavřelo hranu, která je v pořádku. V aplikaci neběželo.

- [x] Návrh a implementace úniku s testy (18. 8. 2026)
- [ ] Zapisovat pod půdorysem robota důkaz „volno" do kanálu hloubky (krok 3 návrhu)
- [ ] Ověřit za běhu

[occupancy-and-local-planning.md](occupancy-and-local-planning.md) · DevLog [2026-08-18](devlog.md#2026-08-18), [2026-09-03](devlog.md#2026-09-03)

<a id="lp-freerun-stisnene-podminky"></a>
### 🧪 První FreeRun na železe ve stísněném prostoru skončil nárazem

`lp-freerun-stisnene-podminky` · vada · **v kódu, na HW neověřeno** · nalezeno 2. 9. 2026 · vyřešeno 3. 9. 2026

Rozbor 417 s záznamu novým `ARBot.Analyze localplan`: koridor se detekoval jen ve 2 % cyklů, mrkev ležela 3 m rovně vpřed a v 97 % plánů byla nedosažitelná. Fallback pak dělal detour 8–24 m skrz neznámo nebo pahýl k čelu překážky — a přesně ten robot jel. Eskapovací zóna 0,5 m kolem robota byla ve stísněném prostoru trvale aktivní a dovolila plánovat pod bezpečným odstupem (min 0,00 m). Druhý den se zóna zrušila (těsný start = únik s hysterezí), nedosažitelný cíl dostal stavy `GoalBlocked` / `GoalUnsafe` a odstup je parametr `safedist=`. Reakce mise na nedosažitelnou mrkev zůstala otevřená a vyřešila se 14. 9. cílem jako zónou. Ověřeno jen testy.

- [x] Rozbor záznamu, příkaz `ARBot.Analyze localplan`, zadání průzkumu (2. 9. 2026)
- [x] Eskapovací zóna zrušena, stavy `GoalBlocked` / `GoalUnsafe`, parametr `safedist=` (3. 9. 2026)
- [ ] Přeměřit FreeRun ve stísněném prostoru na zařízení

čeká na [lp-cil-astar-zona](#lp-cil-astar-zona) · [plan-freerun-stisnene-podminky.md](plan-freerun-stisnene-podminky.md), [occupancy-and-local-planning.md](occupancy-and-local-planning.md), [rozhodnutí 3. 9. 2026](decisions.md) · DevLog [2026-09-02](devlog.md#2026-09-02), [2026-09-03](devlog.md#2026-09-03)

<a id="lp-rychlostni-obalka-neridila"></a>
### 🧪 Rychlostní obálka lokálního plánovače v přímé jízdě vůbec neřídila

`lp-rychlostni-obalka-neridila` · vada · **v kódu, na HW neověřeno** · nalezeno 2. 9. 2026 · vyřešeno 3. 9. 2026

Strop rychlosti z odstupu od překážek a z hranice potvrzeného terénu platil jen v uzlu, do kterého se přijíždí, ne podél úseku — a u dvoubodového plánu robot → mrkev (100 % plánů FreeRunu) se strop startovního uzlu nikdy nečetl. Změřeno: strop 0,30 m/s, robot jel 0,86 m/s. Po opravě obálka poprvé skutečně řídila — a hned bylo vidět další dvě věci: robot po startu 10 s lezl 0,05 m/s, protože grid před ním nic nepotvrdil (léčba: půdorys robota je pro obálku sjízdný), a radiální rampa brzdila i podél okraje trávy (léčba: směrový model, `envelope=directional`). Nový graf rychlostního profilu v pohledu World tu vadu odhalil. Nic z toho nejelo na HW.

- [x] Strop uzlu, ze kterého se odjíždí, platí podél celého úseku (3. 9. 2026)
- [x] Půdorys robota sjízdný pro brzdnou obálku (`FootprintRadiusM`) (3. 9. 2026)
- [x] Směrový model stropu z odstupu (`envelope=directional`) (3. 9. 2026)
- [ ] Přeměřit šířku pásma podél překážky a příčnou chybu sledování na zařízení

čeká na [lp-robot-se-plazi-vyhlazovani](#lp-robot-se-plazi-vyhlazovani) · [occupancy-and-local-planning.md](occupancy-and-local-planning.md), [path-following.md](path-following.md), [rozhodnutí 3. 9. 2026](decisions.md) · DevLog [2026-09-02](devlog.md#2026-09-02), [2026-09-03](devlog.md#2026-09-03)

<a id="lp-robot-se-plazi-vyhlazovani"></a>
### 🧪 Robot se venku plazil rychlostí 0,05 m/s — může za to vyhlazování dráhy

`lp-robot-se-plazi-vyhlazovani` · vada · **v kódu, na HW neověřeno** · nalezeno 7. 9. 2026 · vyřešeno 8. 9. 2026

Rozbor venkovní jízdy (`ARBot.Analyze envelope`, rozpad po uzlech): medián příkazované rychlosti 0,05 m/s a v 53 % plánů je na podlaze už první uzel; váže odstup od překážky přesně na `SafeDist`, brzdná obálka skoro nikdy — padla hypotéza „plazí se skrz neověřený prostor". Cesta je přitom široká (3,8 m). Příčina: vyhlazení dráhy přijme zkratku podle tvrdého `d ≥ SafeDist` a zahodí odstup, který A* koupil cenou. Léčba `smooth=time`: zkratka se přijme, jen když se pod obálku vejde rampa, kterou regulátor odjede, a nezhorší jízdní čas; strop uzlu je obálka v uzlu. Brzdný zákon se sjednotil do `IMotionProfile.Dist2MaxSpeed` a opravil se vadný `Speed2Dist`. Na realistické scéně rychlost 0,05 → 0,49 m/s. Na HW neběželo — nejdřív kurz, pak přeměřit. ✅ **18. 9. 2026 přeměřeno venku s dobrým kurzem:** robot za jízdy neleze — plány na podlaze už v prvním uzlu **1 %** proti 53 % ze 7. 9., příkazovaná rychlost p50 1,0 m/s (`ARBot.Analyze localplan`, `envelope`). Na podlaze zůstává jen stání u překážky (`lp-zasek-v-blokovane-mape`).

- [x] `ARBot.Analyze envelope` po uzlech, `LocalPlanMsg` verze 2 (7. 9. 2026)
- [x] Mechanismus doložen rozhodujícím experimentem (cena nemá na dráhu vliv) (8. 9. 2026)
- [x] `smooth=time`, `Dist2MaxSpeed`, oprava `Speed2Dist`, 9 testů (8. 9. 2026)
- [x] Přeměřit v terénu po opravě kurzu — 18. 9. (`20260918-155329.rec`, jízda 180–560 s): 1. uzel na podlaze 1 % (7. 9.: 53 %), `vCmd` p50 1,0 m/s, za jízdy obálku neváže nic; uzlů a cena na Pi neměřeny (18. 9. 2026)

čeká na [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [occupancy-and-local-planning.md](occupancy-and-local-planning.md), [path-following.md](path-following.md), [rozhodnutí 8. 9. 2026](decisions.md) · DevLog [2026-09-07](devlog.md#2026-09-07), [2026-09-08](devlog.md#2026-09-08), [2026-09-18](devlog.md#2026-09-18)

<a id="lp-drzene-zastaveni-stophold"></a>
### 🧪 Řídicí smyčka umí držené zastavení (StopHold)

`lp-drzene-zastaveni-stophold` · záměr · **v kódu, na HW neověřeno** · nalezeno 13. 9. 2026 · vyřešeno 13. 9. 2026

Robota šlo zastavit jen tím, že mu mise odebrala regulátor — jenže táž vlastnost nese „kam jet" i „smím jet" najednou, takže jakmile chce robota podržet někdo jiný než mise (zotavení kamer, servisní okno), začne se o ni přetahovat a vyhraje ten, kdo psal poslední. `StopHold` je druhý, nezávislý a počítaný vstup: robot stojí, dokud drží kdokoli, brzdí rampou místo tvrdé nuly, důvod držení je povinný a jde do `DriveCommandMsg` (verze 3) i na stránku náhledu; detektor záseku se pod drženým stopem odzbrojí a po uvolnění zase ozbrojí. Držení se na robotu potvrdilo při zotavení kamer, ale robot u toho stál — brzdění pod holdem za jízdy je zatím jen z testů.

- [x] Mechanismus `StopHold` v řídicí smyčce, brzdění rampou, příznak ve zprávě (13. 9. 2026)
- [x] Odzbrojení detektoru záseku pod drženým stopem (a opětovné ozbrojení) (13. 9. 2026)
- [x] Řádek „zastaveno: důvod“ na stránce náhledu (13. 9. 2026)
- [ ] Ověřit brzdění pod holdem za jízdy na zařízení (mise + přirozený výpadek kamery)
- [x] Hold vzatý supervizorem na robotu při skutečné poruše kamery (robot stál) (13. 9. 2026)
- [ ] Rozjezd po uvolnění holdu rampou, ne skokem — ověřit měřením (rampu má dělat profil pohybu)

[plan-drive-hold.md](plan-drive-hold.md), [path-following.md](path-following.md) · DevLog [2026-09-13](devlog.md#2026-09-13)

<a id="lp-klin-mezi-zornymi-poli"></a>
### 🧪 Klín mezi zornými poli barevných kamer brzdí robota

`lp-klin-mezi-zornymi-poli` · vada · **v kódu, na HW neověřeno** · nalezeno 13. 9. 2026 · vyřešeno 13. 9. 2026

Barva D435 má při 640×480 jen 55° zorného pole a kamery jsou pootočené o ±29,3°, takže přímo před robotem zbývá mezera 3,7° (0,19 m ve 3 m), kde hloubka vidí, ale barva ne — buňka tam zůstává neznámá a rychlostní obálka na ni brzdí. Nad záznamem z 12. 9. byl klín příčinou 72,6 % zastavení paprsku a robot byl pod 0,6 m/s ve 40,5 % vzorků (proti 3,3 % bez semantiky); první měření nad jiným záznamem dalo jen 20 %, dva běhy téhož robota se liší čtyřnásobně. Léčba `WedgeFiller` (`wedgefill=`, výchozí 6°) buňku doplní interpolací z buněk příčně vlevo a vpravo, ne konstantou „sjízdné“; pětkrát ji vypnula vlastní opatrnost a pokaždé to našlo až měření. Vrátí asi pětinu ztráty (40,5 → 33,9 %), zbytek je řetěz dalších děr — hlavní brzda je dosah a hustota hloubky.

- [x] Měřidlo `ARBot.Analyze wedge` (zorná pole z intrinsik, rozpad příčin, simulace léčby) (13. 9. 2026)
- [x] `WedgeFiller` se čtyřmi pojistkami a pěti opravami z měření (13. 9. 2026)
- [ ] Přeměřit nad více záznamy (dva běhy se liší 4×)
- [ ] Dopad ceny neznámých buněk (`UnknownCostFactor`) na tvar dráhy — neměřený
- [ ] Běh na zařízení

[occupancy-and-local-planning.md](occupancy-and-local-planning.md), [record-replay.md](record-replay.md) · DevLog [2026-09-13](devlog.md#2026-09-13)

<a id="lp-cil-astar-zona"></a>
### 🧪 Cíl lokálního plánovače je zóna, ne jediná buňka

`lp-cil-astar-zona` · vada · **v kódu, na HW neověřeno** · nalezeno 14. 9. 2026 · vyřešeno 14. 9. 2026

Robot nedokázal dojet k prvnímu bodu trasy: k 44 m jel 9,5 minuty. Cílem A* byla jediná buňka, takže mrkev v trávě nebo u překážky byla nedosažitelná jako celek — plán skončil na nejbližší bezpečné buňce, stav `GoalBlocked` a robot tam zastavil a čekal, ačkoli jiná část cílové zóny dosažitelná byla (nad záznamem ze 14. 9. `GoalBlocked` 24 %, mrkev nedosažitelná v 52 % plánů). Globální vrstva v zónách myslela už od 12. 9., lokální ne. Teď je cíl zóna (`carrotradius=`; průjezdní mrkev je bod, při dojezdu se použije dojezdový poloměr zmenšený o rezervu 0,5 m), heuristika se měří k okraji zóny a A* vrací nejlevnější buňku na dojetí. Není to lék na špatnou mapu — blokovaných buněk bylo p50 27,7 % při rozbitém kurzu, takže část nedosažitelnosti může být chyba gridu, kterou zóna zakryje. ✅ **18. 9. 2026 na zařízení:** `GoalBlocked` 24 → 2 %, `GoalUnsafe` 19 → 2 %, mrkev nedosažitelná 52 → 33 % — zároveň s opraveným kurzem, takže podíl zóny a podíl kurzu se z jednoho záznamu nerozdělí.

- [x] Měření `ARBot.Analyze localplan` nad záznamem ze 14. 9. (14. 9. 2026)
- [x] Zóna jako cíl A*, poloměr jako vlastnost cíle, tři pasti hlídané testy (14. 9. 2026)
- [ ] Podívat se na grid v okamžicích nedosažitelnosti (`ARBot.Analyze grid`) — kolik je chyba mapy
- [x] Přeměřit `localplan` po nasazení — 18. 9. (`20260918-155329.rec`): `GoalBlocked` 2 % / `GoalUnsafe` 2 % (14. 9.: 24 / 19 %), mrkev nedosažitelná 33 % (52 %), s dobrým kurzem (18. 9. 2026)

čeká na [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [occupancy-and-local-planning.md](occupancy-and-local-planning.md) · DevLog [2026-09-14](devlog.md#2026-09-14), [2026-09-18](devlog.md#2026-09-18)

<a id="lp-filtr-izolovanych-bunek"></a>
### ⏸ Izolované skvrny `Blocked` do 4 buněk brzdí robota jako zeď

`lp-filtr-izolovanych-bunek` · vada · **odloženo** · nalezeno 7. 9. 2026

Rozbor rychlostní obálky nad venkovním záznamem ze 7. 9. 2026 (`ARBot.Analyze envelope`) ukázal, že ve 41,5 % plánů drží robota u odstupu izolovaná skvrna blokovaných buněk do 4 buněk (0,01 m²), která ho zbrzdí stejně jako zeď. Morfologický filtr takových skvrn je nasnadě, ale vědomě se neudělal: dokud je kurz z VN100 vedle, nejde rozlišit šum klasifikace od rozmazání gridu chybou pózy, a filtr by ve druhém případě jen zamaskoval příčinu. Rozhodne přeměření obálky po opravě kurzu. 18. 9. 2026 přeměřeno s dobrým kurzem: za jízdy obálku neváže nic, takže izolované skvrny robota aktuálně nebrzdí. Zůstává odložené.

- [x] Přeměřit `envelope` po opravě kurzu — 18. 9.: za jízdy neváže rychlost nic (`VAlong` 0 % v jízdních oknech, `Blocked` p50 26,5 %), `VAlong` váže jen ve stání u překážky; rozlišit šum od rozmazání tak není z čeho — filtr není naléhavý (18. 9. 2026)
- [ ] Filtr izolovaných buněk — jen když přeměření ukáže šum klasifikace

čeká na [lp-robot-se-plazi-vyhlazovani](#lp-robot-se-plazi-vyhlazovani), [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [occupancy-and-local-planning.md](occupancy-and-local-planning.md), [EnvelopeReport.cs](../Src/ARBot.Analyze/EnvelopeReport.cs) · DevLog [2026-09-07](devlog.md#2026-09-07), [2026-09-18](devlog.md#2026-09-18)

<a id="lp-regulator-sledovani-drahy"></a>
### ✅ Regulátor sledování dráhy z waypointů

`lp-regulator-sledovani-drahy` · záměr · **hotovo** · nalezeno 2. 8. 2026 · vyřešeno 7. 9. 2026

Robot má projíždět dráhu z waypointů tak, aby každý uzel minul v toleranci a přitom nezastavoval. Místo proporcionálního řízení (v tomto uspořádání kmitá) vznikl plán s geometrií rohů a zpětnou brzdnou obálkou a exekuce s feedforwardem a lookaheadem (`IPathPlanner`, `PathResult`, `IMotionProfile`); bodový `PointRegulator` nahradil oba staré regulátory. Řídicí smyčka dostala atomickou výměnu regulátoru a watchdog. Venku s ním robot poprvé jel 7. 9. 2026 (FreeRun). Otevřené zůstává ladění na zařízení: `MaxAllowedRotationSpeed` 30°/s je nízké, sweep `τ_look` se nikdy neudělal.

- [x] Fáze 1–5: profil, plán rohů a obálky, exekuce, výměna dráhy v `ControlLoop` (2. 8. 2026)
- [x] Sjednocení regulátorů na jedno `IRegulator` a `PointRegulator` (2. 8. 2026)
- [x] První jízda venku (FreeRun, `20260907-170728.rec`) (7. 9. 2026)

[path-following.md](path-following.md), [rozhodnutí 2. 8. 2026](decisions.md) · DevLog [2026-08-02](devlog.md#2026-08-02), [2026-08-14](devlog.md#2026-08-14), [2026-09-03](devlog.md#2026-09-03), [2026-09-07](devlog.md#2026-09-07)

<a id="lp-occupancy-grid-lokalni-planovani"></a>
### ✅ Occupancy grid a lokální plánování nad ním

`lp-occupancy-grid-lokalni-planovani` · záměr · **hotovo** · nalezeno 10. 8. 2026 · vyřešeno 7. 9. 2026

Sjízdnost z hloubky a z barvy se slévá do kartézského gridu 5 cm kotveného ve světě (dva log-odds kanály, kruhový buffer), nad ním vzdálenostní pole, A* s cenou v čase a vyhlazení na waypointy. Bezpečnost drží invariant „nejeď rychleji, než z čeho zastavíš na hranici potvrzeně průjezdného". `LocalNavigator` to zapojil do runtime 11. 8. a hned se přidalo hlídání rozjeté dráhy proti aktuální mapě (díra objevená při review). Diagnostika ze 14. 8. (rozpad zápisu snímku, koridor, obálka, regulátor) pak našla vady v projekci i v regulátoru. Venku robot s touto vrstvou poprvé jel 7. 9. 2026; co se tam ukázalo (plazení, klín, zóna cíle), jsou samostatná témata.

- [x] Návrh (9 fází) a rozhodnutí (10. 8. 2026)
- [x] Jádro: grid, integrátor, EDT, A* a vyhlazení; azimut přes sloupec obrazu (10. 8. 2026)
- [x] `LocalNavigator` v runtime, zprávy do záznamu, hlídání dráhy proti mapě (11. 8. 2026)
- [x] Diagnostika řetězu a test celého řetězu bez GUI (14. 8. 2026)
- [x] První jízda venku (FreeRun, `20260907-170728.rec`) (7. 9. 2026)

[occupancy-and-local-planning.md](occupancy-and-local-planning.md), [rozhodnutí 10. 8. a 11. 8. 2026](decisions.md) · DevLog [2026-08-10](devlog.md#2026-08-10), [2026-08-11](devlog.md#2026-08-11), [2026-08-14](devlog.md#2026-08-14), [2026-09-07](devlog.md#2026-09-07)

## Vidění

<a id="vid-grid-prahy-realna-data"></a>
### ⬜ Prahy klasifikace a šumový model gridu sjízdnosti nejsou laděné na reálných datech

`vid-grid-prahy-realna-data` · vada · **otevřeno** · nalezeno 30. 7. 2026

Geometrie a klasifikátor polárního gridu jsou ověřené syntetickým testem a na živé kameře grid ukazuje data, ale prahy (`RoughRef`, `MaxSlope`, škálování `MaxHeightDev`), šumový model a radiální hrany z reálného podílu platných pixelů se nikdy neladily nad záznamem z terénu. Grid dnes plní occupancy mapu, nad kterou plánuje lokální navigace — a ta na zařízení taky neřídila, takže případná chyba prahů zatím není vidět.

- [ ] Ladění prahů a šumového modelu nad záznamem ze zařízení

čeká na [vid-polarni-grid-sjizdnosti](#vid-polarni-grid-sjizdnosti) · [traversability-grid.md](traversability-grid.md), [occupancy-and-local-planning.md](occupancy-and-local-planning.md) · DevLog [2026-07-30](devlog.md#2026-07-30)

<a id="vid-inshadow-zahazuje-vzorky"></a>
### ⬜ Okluzní pravidlo zahazuje většinu barevných vzorků

`vid-inshadow-zahazuje-vzorky` · vada · **otevřeno** · nalezeno 14. 8. 2026

Při zápisu barvy do occupancy gridu se za první překážkou v daném azimutu stíní celý zbytek paprsku, i místa, kam kamera zjevně vidí. Nad virtuálním HW to zahodilo ~5 200 z ~12 000 kandidátů, takže semantický kanál dostává řádově míň dat než geometrický a plocha mimo cestu se potvrzuje pomalu. Záměr pravidla je správný, míra ne; k rozmyšlení je stínit jen do jisté vzdálenosti nebo vzorek jen zeslabit. Neřešeno.

[occupancy-and-local-planning.md](occupancy-and-local-planning.md) · DevLog [2026-08-14](devlog.md#2026-08-14)

<a id="vid-kalibrace-kamer-bias"></a>
### ⬜ Chybná kalibrace kamer je bias, který lokalizace integruje

`vid-kalibrace-kamer-bias` · vada · **otevřeno** · nalezeno 20. 8. 2026

Chyba v montáži kamery (yaw o 1° při dohledu 3–6 m) posune celý bodový oblak o 5–10 cm — o řád víc, než je vlastní šum korelace (5 mm). Filtr bere měření jako nezávislá, takže bias vyhraje vahou počtu a póza se drží posunutá s falešnou jistotou. Kalibrace je tedy pravděpodobně dominantní chybový člen. Rozlišovací znak jde změřit hned: bias z montáže se s kurzem otáčí, posun mapy ne — stačí projet smyčku. Extrinsiky reálných D435 neměřené.

- [ ] Projet smyčku a sledovat, zda se hlášený nesouhlas otáčí s kurzem
- [ ] Změřit extrinsiky skutečných D435

[map-correlation-localization.md](map-correlation-localization.md), [traversability-grid.md](traversability-grid.md) · DevLog [2026-08-20](devlog.md#2026-08-20), [2026-08-23](devlog.md#2026-08-23)

<a id="vid-segmentace-rozliseni-128"></a>
### ⬜ Dopad výpočtu ve 128×128 na hustotu dat pro grid a hranice cesty

`vid-segmentace-rozliseni-128` · záměr · **otevřeno** · nalezeno 6. 9. 2026

Síť počítá sjízdnost ve 128×128 (snímek 4:3 se přitom stlačí na čtverec), kdežto histogram barev pracoval nad plným snímkem 640×480 — hranice cesty a zápis do occupancy gridu tak dostávají řádově řidší data. Jaký to má dopad na hustotu gridu a přesnost hranic, naměřené není. Není to věc konfigurace: vyšší rozlišení vstupu znamená síť přetrénovat, takže stlačení i zvětšení nejbližším sousedem jsou replika tréninku a neopravují se.

- [ ] Změřit dopad 128×128 proti plnému snímku na hranice cesty a occupancy grid

[semantic-segmentation.md](semantic-segmentation.md), [OnnxBackProject.cs](../Src/ARBot.Common/Vision/Nn/OnnxBackProject.cs) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-07](devlog.md#2026-09-07)

<a id="vid-model96-2-na-npu"></a>
### ⬜ Lepší model Model96.2 dává 96,7 %, ale půlí snímkovou frekvenci

`vid-model96-2-na-npu` · záměr · **otevřeno** · nalezeno 7. 9. 2026

Model96.2 je 34× dražší než Model61.1; na CPU 637 ms (nepoužitelné), na NPU 44 ms s přesností 96,66 % / IoU 0,953 a falešně přidanou cestou 2,0 % místo 7,9 %. Za běhu runtime ale spadne frekvence z 29 na 16,5 snímků/s a int8 kvantizace ho rozbije (37,8 %), takže musí běžet fp16. Přibyl parametr `camerafps=`, který nastavuje frekvenci přímo na kameře. Jestli poloviční frekvence řízení vadí, se neví — nikdy s tím nejelo; v provozním profilu zůstává Model61.1 a blok pro Model96.2 je jen připravený.

- [x] Změřit Model96.2 na NPU (čas, přesnost, int8 vs fp16) (7. 9. 2026)
- [x] `camerafps=` a připravený blok v `pi-provoz.cfg` (7. 9. 2026)
- [ ] Rozhodnout provozní bod jízdou — jestli 16 snímků/s řízení stačí

[semantic-segmentation.md](semantic-segmentation.md) · DevLog [2026-09-07](devlog.md#2026-09-07), [2026-09-09](devlog.md#2026-09-09)

<a id="vid-segmentace-pravda-d435"></a>
### ⬜ Kvalita segmentační sítě na dnešních snímcích D435 je bez ground truth neznámá

`vid-segmentace-pravda-d435` · záměr · **otevřeno** · nalezeno 7. 9. 2026

Segmentační síť má proti histogramu barev naměřenou výhodu (88,2 % proti 78,0 % per-pixel) jen na sadě 50 snímků z ARBot2 — jiná kamera, jiné scény, data z let 2019–2022. Na dnešních záznamech z D435 dává síť zjevně čistší obraz cesty, na zarostlé ploše je ale nerozhodná, zatímco histogram tvrdí 80 % sjízdné — a bez ground truth k našim záznamům je to jen rozpor dvou metod, ne verdikt. Chybí anotovaná sada snímků z D435 z roku 2026; s ní by šlo říct, která metoda má pravdu a jestli síť za jízdy (rozmazání, expozice, stíny) drží.

[semantic-segmentation.md](semantic-segmentation.md), [models/testset (sada z ARBot2)](../models/testset), [OnnxBackProject.cs](../Src/ARBot.Common/Vision/Nn/OnnxBackProject.cs) · DevLog [2026-09-07](devlog.md#2026-09-07)

<a id="vid-trenink-nejde-zopakovat"></a>
### ⬜ Trénink segmentace se dnes nedá zopakovat jedním kliknutím

`vid-trenink-nejde-zopakovat` · vada · **otevřeno** · nalezeno 7. 9. 2026

Trénovací notebook z roku 2022 stahuje data přes legacy LabelBox API, které je na serveru vypnuté (`label_generator()` neexistuje), a dnešní Colab má Keras 3, kde se staré `.h5` váhy nenačtou. Export testovací sady je přepsaný na dnešní `project.export()`, ale část labelů se nevyexportuje (anotace nástrojem, který už není v ontologii projektu) — opravit to jde jen v LabelBoxu. Referenční float model s pevnými tvary v repu není; export z Kerasu s tím správným checkpointem má připravený notebook `ExportFloatModel.ipynb`.

- [x] Export sady přepsat na dnešní API a rozlišit id labelů od data rows (7. 9. 2026)
- [ ] Připojit chybějící nástroje k ontologii v LabelBoxu, aby se sada exportovala celá
- [ ] Zprovoznit trénink (nebo aspoň export float modelu) v dnešním Colabu

[semantic-segmentation.md](semantic-segmentation.md), [SemanticSegmentation.ipynb](../Src/Colab/SemanticSegmentation.ipynb), [ExportTestSet.ipynb](../Src/Colab/ExportTestSet.ipynb) · DevLog [2026-09-07](devlog.md#2026-09-07), [2026-09-09](devlog.md#2026-09-09)

<a id="vid-model96-float-checkpoint"></a>
### ⬜ U Model96.2 chybí float checkpoint lepších vah, optimalizace je nevyužitelná

`vid-model96-float-checkpoint` · vada · **otevřeno** · nalezeno 9. 9. 2026

Dokumentace vedla `Model96.2.rknn` jako převod z `.h5` větve (95,35 %), ale naměřeno měl 96,66 %, tedy víc než údajný zdroj. Měřením se rozhodlo, že vznikl z `.tflite` větve (96,80 %, −0,14 p. b. za fp16). Ta větev je ale dynamic-range kvantovaná, takže na ní optimalizace grafu nenajde nic; optimalizovat jde jen horší `.h5` větev za ztrátu 1,31 p. b. přesnosti. Model96.2 proto zůstává beze změny a −45 % času půjde vytěžit teprve s float checkpointem těch lepších vah, který v repu není. Int8 ten model dál rozbíjí.

- [x] Zjistit, z čeho dnešní `Model96.2.rknn` vznikl (9. 9. 2026)
- [ ] Sehnat float checkpoint lepších vah Model96.2

[semantic-segmentation.md](semantic-segmentation.md), [models/README.md](../models/README.md) · DevLog [2026-09-09](devlog.md#2026-09-09)

<a id="vid-zpetna-projekce-hloubka"></a>
### 🧪 Zpětná projekce pixelu ignorovala hloubku

`vid-zpetna-projekce-hloubka` · vada · **v kódu, na HW neověřeno** · nalezeno 21. 8. 2026 · vyřešeno 21. 8. 2026

Převod pixelu barevného obrazu na bod v prostoru byl mrtvý na všech platformách: Windows volal nativní `ColorPixel23D`, které v knihovně není, ARM vyhazoval výjimku, a báze tiše promítala na rovinu země — u horizontu body na stovky metrů. Extrinsiky color–depth kamera znala, ale ARM varianta je zahazovala. Nově managed `ColorPixelTo3D`, extrinsiky protažené HALem až do záznamu a podtřídy projekce zrušené. Na reálné D435 neověřeno.

- [x] `ColorPixelTo3D` a oprava báze `CameraProjection.TransformBack` (21. 8. 2026)
- [x] Extrinsiky color–depth přes HAL a do záznamu (`CameraFrame` layout v5) (21. 8. 2026)
- [ ] Ověřit extrinsiky a body hranice na skutečné D435

[map-correlation-localization.md](map-correlation-localization.md), [traversability-grid.md](traversability-grid.md) · DevLog [2026-08-21](devlog.md#2026-08-21)

<a id="vid-prob-overlay-128"></a>
### 🧪 Pravděpodobnost cesty 128×128 se kreslila jen přes střed snímku

`vid-prob-overlay-128` · vada · **v kódu, na HW neověřeno** · nalezeno 10. 9. 2026 · vyřešeno 10. 9. 2026

Síť počítá ve 128×128 a pokrývá celý snímek 640×480, ale panel Obrázky držel poměr stran každé vrstvy zvlášť, takže překryv kryl jen prostředních 75 % šířky, a odečet hodnoty pod kurzorem platil jen v levém horním rohu a i tam pro jiný bod. Řídicí cesta byla v pořádku, zkreslení bylo jen v zobrazení. Vrstva teď nese rozměr scény, kterou pokrývá, a každá osa se škáluje zvlášť; tutéž vadu měl webový náhled (`?layer=prob`), kde se pravděpodobnost natahuje na rozměr barvy. Panel ověřen za běhu v simulaci, web jen testem rozměru JPEG.

- [x] Překryv a odečet kurzoru v panelu Obrázky (10. 9. 2026)
- [x] Webový náhled natahuje pravděpodobnost na rozměr snímku (10. 9. 2026)
- [ ] Ověřit webový náhled na zařízení

[Views/README.md](../Src/ARBot/Views/README.md), [semantic-segmentation.md](semantic-segmentation.md) · DevLog [2026-09-10](devlog.md#2026-09-10)

<a id="vid-nativni-knihovna-opravy"></a>
### ✅ Nativní knihovna měla chybějící exporty na x64 a špatnou volací konvenci na ARM

`vid-nativni-knihovna-opravy` · vada · **hotovo** · nalezeno 3. 7. 2026 · vyřešeno 3. 7. 2026

První NUnit testy nad P/Invoke vrstvou `NativeComputeUnit` odhalily, že čtyři funkce (`TransformPoint4DImpl`, `Depth2XYZImpl`, `XYZ2PlaneImpl`, `ClearAggregateImpl`) v x64 DLL vůbec nebyly exportované (latentní `EntryPointNotFound` i v projekci kamery) a že ARM64 assembler byl psaný x86 stylem místo AAPCS64 (floaty v jiných registrech), plus chyba offsetu ×32. Opraveno a ověřeno na x64, přes Docker/QEMU i na skutečném Orange Pi. Cesta `Segment` padá na x64 (`AccessViolation`), v produkci se nepoužívá, její testy zůstávají ignorované.

- [x] NUnit testy `NativeComputeUnit` (3. 7. 2026)
- [x] Doplnění exportů v `asm_win_x64.asm`, ochrana singularity v `CalcPlaneParams` (3. 7. 2026)
- [x] Oprava AAPCS64 a offsetů v `asm_linux_arm64.S`, ověřeno QEMU + Orange Pi (3. 7. 2026)

[build-and-platforms.md](build-and-platforms.md), [rozhodnutí 25. 7. 2026 (nativní knihovna se staví CMakem)](decisions.md) · DevLog [2026-07-03](devlog.md#2026-07-03)

<a id="vid-polarni-grid-sjizdnosti"></a>
### ✅ Polární grid sjízdnosti z hloubkové kamery s robot-centrickým pohledem

`vid-polarni-grid-sjizdnosti` · záměr · **hotovo** · nalezeno 29. 7. 2026 · vyřešeno 1. 8. 2026

První vrstva vnímání terénu: hloubka → point cloud → polární grid kolem robota, per kamera, s klasifikací buněk a důvěrou. Zapojený do runtime (v Run se počítá, ve View jen přehrává — živé intrinsics se nezaznamenávají, přepočet ze záznamu je odložený), s ptačím pohledem a overlayem přes hloubkový obraz. Ověřeno na živé kameře 30. 7. Převod hloubky na body jede nativní SIMD cestou (3× levnější), 1. 8. se výpočet přesunul na vlákno kamery přímo do `CameraFrame` a buňky se kreslí jako mezikruhové výseče místo překrývajících se čtverců. Dnes je vstupem kartézského occupancy gridu.

- [x] Návrh, implementace, syntetický test geometrie a klasifikace (29. 7. 2026)
- [x] Zapojení do runtime, `RobotCentricDocument`, overlay přes hloubku, ověřeno na živé kameře (30. 7. 2026)
- [x] Nativní depth → pointcloud (`DepthTransform2Impl`) s ověřenou ekvivalencí (30. 7. 2026)
- [x] Grid součástí `CameraFrame` (verze 2), výpočet synchronně na vlákně kamery (1. 8. 2026)
- [x] Buňky jako mezikruhové výseče (1. 8. 2026)

[traversability-grid.md](traversability-grid.md), [rozhodnutí 29. 7. 2026](decisions.md) · DevLog [2026-07-29](devlog.md#2026-07-29), [2026-07-30](devlog.md#2026-07-30), [2026-08-01](devlog.md#2026-08-01)

<a id="vid-gc-spicky-latence"></a>
### ✅ Vizuální cesta se každých ~5 s zasekla na 200–450 ms kvůli GC

`vid-gc-spicky-latence` · vada · **hotovo** · nalezeno 30. 7. 2026 · vyřešeno 1. 8. 2026

Na zařízení skákalo stáří gridu mezi 30 a 500 ms. CSV diagnostika ukázala, že vlastní výpočet trvá 16–50 ms, ale ~8–13 % snímků zasáhne periodická pauza gen2 GC z alokací velkých obrazových bufferů. Server GC to zhoršil (zamítnuto měřením), nativní transform snížil CPU 3×, ale ocas nezmizel. Řešením byla architektura: kamery pullované řídicí smyčkou, výpočet na vlákně kamery, poolované buffery — a hlavní viník se našel až na HW: `MessageWriter` serializoval každou zprávu přes novou `MemoryStream` (~90 MB/s do LOH), k tomu odpojené UART senzory alokovaly stack-trace v těsné smyčce. Po opravách self-test na zařízení: gen2 = 0, žádný snímek nad 100 ms.

- [x] CSV diagnostika, diagnóza GC, Server GC zamítnut měřením (30. 7. 2026)
- [x] Nativní depth → pointcloud bez alokací (CPU −3×, ocas beze změny) (30. 7. 2026)
- [x] Pull kamer řídicí smyčkou, pooling bufferů a kopií per odběratel (1. 8. 2026)
- [x] `MessageWriter` bez alokací na zprávu, backoff a škrcení logu v `SensorBase` (1. 8. 2026)
- [x] Ověřeno na HW self-testem (gen2 = 0, 0 % snímků nad 100 ms) (1. 8. 2026)

[traversability-grid.md](traversability-grid.md), [plan-camera-vision-refactor.md](plan-camera-vision-refactor.md), [selftest.md](selftest.md), [rozhodnutí 1. 8. 2026](decisions.md) · DevLog [2026-07-30](devlog.md#2026-07-30), [2026-08-01](devlog.md#2026-08-01)

<a id="vid-pathedges-zahazovany-vypocet"></a>
### ✅ Hranice cesty se počítaly a výsledek se zahazoval

`vid-pathedges-zahazovany-vypocet` · vada · **hotovo** · nalezeno 9. 8. 2026 · vyřešeno 7. 9. 2026

Kamerový driver volal nativní výpočet hran cesty a výsledek odjakživa zahodil; navazující hledač hran si je počítal podruhé sám. Výpočet se přesunul do procesoru snímku, hrany se nesou v `CameraFrame` a serializují se se snímkem (formát 3), mrtvá větev v driveru se odstranila. Na zařízení výkon potvrdilo až A/B měření 7. 9. 2026, kde `PathEdges` běží v runtime za jízdy (a nad sítí dokonce nad 24× menším obrazem).

- [x] Přesun do `CameraFrameProcessor`, hrany v `CameraFrame` formát 3 (9. 8. 2026)
- [x] Úklid mrtvé větve `BackProject` v `D435Camera` (9. 8. 2026)
- [x] Výkon na zařízení (A/B na Orange Pi za běhu runtime) (7. 9. 2026)

[rozhodnutí 9. 8. 2026](decisions.md), [record-replay.md](record-replay.md) · DevLog [2026-08-09](devlog.md#2026-08-09), [2026-09-07](devlog.md#2026-09-07)

<a id="vid-cameraprojection-vady"></a>
### ✅ Projekce kamery měla čtyři skryté chyby

`vid-cameraprojection-vady` · vada · **hotovo** · nalezeno 10. 8. 2026 · vyřešeno 21. 8. 2026

Při stavbě occupancy gridu se v `CameraProjection` postupně našly čtyři chyby: promítaly se i body za kamerou (chyběla kontrola `Z > 0`, bod 4 m za robotem vyšel jako pixel před ním), záměna přetížení `ToDistort` nechala celou cache nulovou, posunutí kamery se v `Transform` započítalo dvakrát (plocha mimo cestu se pak neoznačovala jako nesjízdná, chyba ~95 px, platí i pro reálný hardware) a zpětná projekce `TransformBack` ignorovala hloubku. Testy to nechytly, protože měly vlastní referenční projekci bez postranního posunutí. Poslední kus se opravil 21. 8. přepsáním báze, aby hloubku používala.

- [x] Kontrola `Z > 0` a oprava `ToDistort` (10. 8. 2026)
- [x] Dvojí odečet posunutí v `Transform` + round-trip test proti rendereru (14. 8. 2026)
- [x] Čtvrtá vada (zpětná projekce bez hloubky) je samostatné téma, opraveno 21. 8. (21. 8. 2026)

[imu-and-frames.md](imu-and-frames.md), [virtual-hw.md](virtual-hw.md) · DevLog [2026-08-10](devlog.md#2026-08-10), [2026-08-14](devlog.md#2026-08-14), [2026-08-21](devlog.md#2026-08-21)

<a id="vid-segmentace-nn"></a>
### ✅ Sjízdnost z RGB neuronovou sítí místo histogramu barev

`vid-segmentace-nn` · záměr · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 7. 9. 2026

Druhá implementace „cesty z RGB" (`backproject=nn`): U-Net Model61.1 z ARBot2 přes ONNX Runtime, který nese nativní knihovnu pro Windows i ARM, takže v simulaci i na robotu běží týž kód. Kvantizace je schovaná uvnitř modelu a předzpracování je replika tréninku, ne naše volba. Změřeno na Orange Pi: síť stojí 10,2 ms (na ARM vyhrává int8, na x86 float — opačně), za běhu runtime přidá jen 6–7 ms, protože odpadne histogram přes plný snímek. Proti sadě s pravdou dává síť 88,2 % / IoU 0,846 proti 78,0 % / 0,752 u histogramu a hlavně vymýšlí cestu, kde není, 2,6× méně často. S histogramem robot jel 7. 9., se sítí na NPU od 12. 9. (provozní profil `backproject=npu`); dopad na řízení proti histogramu změřený není.

- [x] Rozbor modelu, převod TFLite → ONNX a `OnnxBackProject` s testy (6. 9. 2026)
- [x] Měřidlo `ARBot.Analyze backproject` (čas, shoda s histogramem, srovnávací obrázky) (6. 9. 2026)
- [x] Změřit inferenci a A/B za běhu runtime přímo na Orange Pi (7. 9. 2026)
- [x] Měření proti pravdě (`--truth=`, sada 50 snímků z LabelBoxu, `SegmentationMetrics`) (7. 9. 2026)
- [x] Modely do gitu, aby šla síť spustit na čerstvé kopii (7. 9. 2026)

[semantic-segmentation.md](semantic-segmentation.md), [testset/README.md](../models/testset/README.md) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-07](devlog.md#2026-09-07)

<a id="vid-mezera-88-vs-95"></a>
### ✅ Síť dává 88 %, ačkoli notebook tvrdí 95 %

`vid-mezera-88-vs-95` · vada · **hotovo** · nalezeno 7. 9. 2026 · vyřešeno 7. 9. 2026

První měření proti pravdě vyšlo o 7,3 procentního bodu hůř, než uvádí trénovací notebook. Šest hypotéz padlo měřením (kvantizace, jiný checkpoint, naše rekonstrukce sady, pořadí kanálů, vzorkování při zmenšení, „novější snímky jsou těžší"). Vysvětlení podal autor: trénovací sada se v čase měnila, takže Model61.1 z února 2021 byl trénován i testován na jiných datech než dnešní sada — sedí to s tím, že Model96.2 mezeru nemá. Je to vysvětlení, ne důkaz; autor rozhodl nedohledávat to v LabelBoxu. Důsledek platí: u Model61.1 se nesmí tvrdit, že dává 95 %.

- [x] Zamítnout hypotézy měřením (float, BGR, originální sada, vzorkování) (7. 9. 2026)
- [x] Vysvětlení jinou trénovací sadou, rozhodnutí nedohledávat (7. 9. 2026)

[semantic-segmentation.md](semantic-segmentation.md) · DevLog [2026-09-07](devlog.md#2026-09-07)

<a id="vid-npu-rknn"></a>
### ✅ Síť běží na NPU Orange Pi (`backproject=npu`)

`vid-npu-rknn` · záměr · **hotovo** · nalezeno 7. 9. 2026 · vyřešeno 7. 9. 2026

Inference přes P/Invoke na `librknnrt.so`, převod modelu `models/onnx2rknn.py`; každá kamera má vlastní jádro NPU. Síť stojí 3,3 ms místo 10,2 ms na CPU a za běhu runtime jen +1,2–1,5 ms proti histogramu, přesnost prakticky stejná. Padlo dřívější doporučení psát C++ shim — stačí pět volání API. Cestou se opravil chybný návod na ověření driveru (RKNPU je DRM node, ne `/dev/rknpu*`) a nasazení posílá modely i knihovnu. Od 7. 9. je NPU v provozním profilu `pi-provoz.cfg` a ověřené na robotu — obě kamery startují na NPU bez ztráty snímků.

- [x] Zjistit stav NPU driveru na Pi a opravit návod (7. 9. 2026)
- [x] `RknnBackProject` + převod modelu, tři pasti RKNN (float zdroj, NCHW, normalizace na NPU) (7. 9. 2026)
- [x] Nasazení modelů a `librknnrt.so`, zapnutí v `pi-provoz.cfg`, ověřeno na Pi (7. 9. 2026)

[semantic-segmentation.md](semantic-segmentation.md), [onnx2rknn.py](../models/onnx2rknn.py), [pi-provoz.cfg](../config/pi-provoz.cfg) · DevLog [2026-09-07](devlog.md#2026-09-07)

<a id="vid-model-optimalizace-grafu"></a>
### ✅ Polovina výpočtu segmentační sítě byla zbytečná

`vid-model-optimalizace-grafu` · záměr · **hotovo** · nalezeno 9. 9. 2026 · vyřešeno 9. 9. 2026

Rozbor grafu Model61.1 ukázal dvě exaktní úpravy (konvoluce 1×1 se spočítá před zvětšením obrazu a dvě sousední 1×1 konvoluce bez nelinearity se sloučí), které uberou 49 % násobení při nezměněném rozhodnutí na všech 819 200 pixelech testovací sady. Nástroj `models/onnxopt.py` ověřuje shodu rozhodnutí, ne čísel. Optimalizovaný model je výchozí pro CPU (`nnmodel=`, −54 % času na x86, jen −8 % na ARM) i pro NPU (`npumodel=`, 3,27 → 2,72 ms na Orange Pi, přesnost beze změny); z ubraných násobení NPU využilo jen třetinu, takže se muselo měřit, ne extrapolovat. Existenci výchozích modelů i shodu rozhodnutí hlídají testy. Nevysvětlené zůstává, proč je varianta `_int8_deq_opt` o 27 % rychlejší než `_float_opt` s týmž grafem.

- [x] Rozbor grafu a `onnxopt.py` s kontrolou shody rozhodnutí (9. 9. 2026)
- [x] Optimalizovaný model jako výchozí `nnmodel=`, kryto dvěma testy (9. 9. 2026)
- [x] Převod do RKNN, měření na Orange Pi a přepnutí `npumodel=` (9. 9. 2026)
- [x] Přeměření CPU cesty na ARM (pořadí variant je tam jiné než na x86) (9. 9. 2026)

[semantic-segmentation.md](semantic-segmentation.md), [onnxopt.py](../models/onnxopt.py), [models/README.md](../models/README.md) · DevLog [2026-09-09](devlog.md#2026-09-09)

<a id="vid-rozbor-modelu-a-notebooku"></a>
### ✅ Trénovací notebook validoval na testovací sadě a měl další chyby

`vid-rozbor-modelu-a-notebooku` · vada · **hotovo** · nalezeno 9. 9. 2026 · vyřešeno 9. 9. 2026

Rozbor Model61.1 a trénovacího notebooku ze zadání „co by šlo vylepšit". Nejvážnější nález je validační sada shodná s testovací (udávaných 95,5 % je výběrové maximum přes ~1000 epoch, ne nezávislý odhad); dál sigmoid se špatnou ztrátou, dropout v každém bloku, dvakrát definovaná třída modelu a uložení datové sady, které z masky „všechno je cesta" udělá „nic". Mrtvých neuronů je jen 0,7 %, ale z 16 vstupů poslední vrstvy stačí jeden; flip-TTA a softmax jsou zamítnuté měřením. Notebook je opravený (oddělená validace, správná ztráta, `GenericModel27` s residuály), ale žádný nový model se z něj nenatrénoval — na vývojovém stroji TensorFlow nejde spustit.

- [x] Mrtvé neurony a redundance kanálů změřeny (9. 9. 2026)
- [x] Postprocessing přeměřen (softmax, flip-TTA, práh) (9. 9. 2026)
- [x] Chyby v notebooku sepsané a opravené, architektura v `GenericModel27` (9. 9. 2026)

[semantic-segmentation.md](semantic-segmentation.md), [SemanticSegmentation.ipynb](../Src/Colab/SemanticSegmentation.ipynb) · DevLog [2026-09-09](devlog.md#2026-09-09)

## Mise

<a id="mise-vizualni-dojezd-na-cil"></a>
### ⬜ Vizuální dojezd posledních metrů podle QR kódu

`mise-vizualni-dojezd-na-cil` · záměr · **otevřeno** · nalezeno 12. 8. 2026

Mise Robotour jede na cíl z GPS, jejíž chyba ±2 m je pro „zastav u kódu" na hraně použitelnosti — stanoviště se proto bere jako zóna o dojezdovém poloměru 3 m, ne bod. Poslední ~3 m by šly řídit podle vidění: rohy QR kódu v obraze dávají směr i vzdálenost a robot by dojel ke kódu, ne k místu, kde ho GPS tuší. Zapsáno při návrhu globální navigace a mise Robotour v srpnu 2026 jako budoucí rozšíření; kód ani měření nevznikly.

[global-navigation-runtime.md](global-navigation-runtime.md), [robotour-mission.md](robotour-mission.md), [RobotourMission.cs](../Src/ARBot.Common/Missions/RobotourMission.cs)

<a id="mise-magcal-sber-po-zapisu"></a>
### ⬜ Kalibrace magnetometru se po zápisu sama znehodnotí — kolektor sbírá dál

`mise-magcal-sber-po-zapisu` · vada · **otevřeno** · nalezeno 17. 9. 2026

Mise `magcal` došla do verdiktu HOTOVO v 48. s (podmíněnost 330, `sd|B|` 0,0026 G) a HOTOVO držela 88 s. V 16:20:16 obsluha ťukla na zápis, v 16:20:20 se kalibrace zapsala do registru 23 i do flash — a v 16:20:19, tedy uvnitř toho zápisu, se proložení zhroutilo: `sd|B|` 0,0026 → 0,0073 → 0,0276 G, měřítko osy z 1,03 → 2,46 a verdikt spadl na „NEPOUZITELNE: pole je porad nekonzistentni. Postav robota na JEDNO misto dal od kovu a zacni znovu." Ta rada je opačná než skutečnost — pole konzistentní bylo a přestalo být právě tím zápisem. Příčina je v kódu, ne v poli. `MagCalMission.Consume` přidává každý `IMUState` do kolektoru bez ohledu na fázi, `MagCalCollector.Add` žádnou bránu nemá a proložení se počítá přes celou nasbíranou sadu. `MagnetometerRaw` (`UncompMag`) je přitom podle měření z 12. 9. 2026 pole KOMPENZOVANÉ — právě proto si mise registr 23 před sběrem sama maže. Po zápisu už vymazaný není, takže od té chvíle padají do téže sady vzorky měřené přes novou kompenzaci a fit míchá dvě různé soustavy. Zapsaná kalibrace je přitom v pořádku — zapsalo se to, co platilo v okamžiku ťuknutí (`1,092364 … −0,109534`, tedy dobrá první půlka dat), ne ten rozpadlý výsledek; gate `Usable` ve `WriteToSensor()` drží. Vada je v tom, co obsluha uvidí PO úspěšném zápisu: „NEPOUŽITELNÉ" a pokyn začít znovu, tedy zahodit kalibraci, která právě vyšla.

- [x] Nález a důkaz ze záznamu (časová shoda zhroucení se zápisem do registru 23) (17. 9. 2026)
- [ ] Po `MagCalPhase.Written` přestat sbírat; další měření začíná s prázdnou sadou
- [ ] Test, že zápis sám verdikt nezmění

[plan-vn100-kalibrace.md](plan-vn100-kalibrace.md), [MagCalMission.cs](../Src/ARBot.Common/Missions/MagCalMission.cs), [MagCalCollector.cs](../Src/ARBot.Common/Calibration/MagCalCollector.cs) · DevLog [2026-09-17](devlog.md#2026-09-17)

<a id="mise-robotour"></a>
### 🧪 Mise Robotour jako stavový automat s QR kódy

`mise-robotour` · záměr · **v kódu, na HW neověřeno** · nalezeno 11. 8. 2026 · vyřešeno 26. 8. 2026

Soutěžní scénář depo → nakládka → vykládka → depo: robot dojede, obsluha drží nouzové zastavení, z pravé kamery se přečte QR kód s cílem a po uvolnění stopu se jede dál. Návrh z 11. 8. prošel třemi revizemi (mrkev až na okraj mapy, počátek roviny z mapy, nouzové zastavení řeší řídicí smyčka). Dekodér je nakonec ZXing.Net místo ZBar, potvrzování cíle obsluhou se zrušilo (mise běží bez operátora) a cíl z kódu se přichycuje na cestu. Celý průchod je proklikaný v simulaci, na zařízení mise neběžela.

- [x] Návrh a revize zadání (11. 8. 2026)
- [x] Fáze 2–5: skener QR, parser `geo:`, automat, `MissionMsg`, panel UI (26. 8. 2026)
- [x] Průchod misí v simulaci, přichycení cíle na cestu (27. 8. 2026)
- [ ] Ověření na zařízení (fáze 7)

čeká na [nav-globalni-navigace-runtime](#nav-globalni-navigace-runtime), [mise-nouzove-zastaveni-controlloop](#mise-nouzove-zastaveni-controlloop) · [robotour-mission.md](robotour-mission.md), [rozhodnutí 26. 8. 2026 (ZXing, bez potvrzování)](decisions.md) · DevLog [2026-08-11](devlog.md#2026-08-11), [2026-08-12](devlog.md#2026-08-12), [2026-08-26](devlog.md#2026-08-26), [2026-08-27](devlog.md#2026-08-27)

<a id="mise-cil-dosazitelnost"></a>
### 🧪 Zkouška dosažitelnosti cíle z QR kódu nebyla důvěryhodná

`mise-cil-dosazitelnost` · vada · **v kódu, na HW neověřeno** · nalezeno 26. 8. 2026 · vyřešeno 27. 8. 2026

Dvě vady v tom, jak mise posuzuje cíl z kódu. Zkouška byla pesimističtější než jízda: cíl na téže cestě za robotem hlásila jako nedosažitelný, protože mapmatching vybral orientovanou hranu podle pořadí, ne podle kurzu — teď zkouší obě orientace. A dosažitelnost neověřovala vzdálenost cíle od sítě; hůř, navigace měří dojezd proti surovému cíli, takže cíl odsazený od osy cesty víc než o 3 m by nikdy neohlásil dojezd a mise by uvízla napořád. Cíl se proto přichycuje na cestu a co je dál než `MaxTargetOffRoadM` (15 m, z úsudku) je nedosažitelné; odstup jde do záznamu. Tatáž past se 12. 9. znovu řešila u mise Track.

- [x] Zkouška bere minimum přes hranu i její reverzní (cíl za robotem) (27. 8. 2026)
- [x] Přichycení cíle na cestu, limit `MaxTargetOffRoadM`, `MissionMsg` verze 6 (27. 8. 2026)
- [ ] `NoRoute` na cíl z QR = neplatný cíl, číst znova (dnes přerušení mise)
- [ ] Nastavit 15 m z odstupů naměřených na zařízení

[robotour-mission.md](robotour-mission.md), [rozhodnutí 27. 8. 2026](decisions.md), [track-mission.md](track-mission.md) · DevLog [2026-08-26](devlog.md#2026-08-26), [2026-08-27](devlog.md#2026-08-27), [2026-09-12](devlog.md#2026-09-12)

<a id="mise-qr-cteni"></a>
### 🧪 Čtení QR kódů z kamery (ZXing.Net místo ZBaru)

`mise-qr-cteni` · záměr · **v kódu, na HW neověřeno** · nalezeno 26. 8. 2026 · vyřešeno 26. 8. 2026

Cíl mise Robotour zadává člověk QR kódem. Dekodér je čistě managed ZXing.Net — binding ZBaru z ARBot2 nebyl k dispozici, a tím celá plánovaná fáze „nativní libzbar na obě platformy" zmizela. Skener je samostatný stupeň, který mise zapíná jen pod drženým stopem; převod na šedou se zobecnil na `Image<T>.ToGray`. Aby šel průchod misí projít v simulaci, umí virtuální kamera postavit svislou desku s kódem (jen do barvy, ne do hloubky). Z 1,2 m se kód nepřečetl, staví se na 1,0 m. Úspěšnost čtení na skutečném stanovišti není naměřená — testy dokazují cestu, ne čitelnost, protože kód kóduje týž ZXing.

- [x] `QrScanner` + `QrCodeMsg`, dekodér ZXing.Net, převod BGR32 → Y800 (26. 8. 2026)
- [x] QR kód do virtuální kamery (`SyntheticBillboard`), test scéna → render → dekodér (26. 8. 2026)
- [x] Deska kolmo na pohled kamery a na 1,0 m (z 1,2 m se nepřečte) (27. 8. 2026)
- [ ] Změřit úspěšnost čtení a dobu dekódování na skutečném stanovišti (Orange Pi, D435)

[robotour-mission.md](robotour-mission.md), [rozhodnutí 26. 8. 2026](decisions.md), [virtual-hw.md](virtual-hw.md) · DevLog [2026-08-26](devlog.md#2026-08-26), [2026-08-27](devlog.md#2026-08-27)

<a id="mise-robotour-armovani-rozptyl"></a>
### 🧪 Mise by se v depu nezarmovala nikdy — práh rozptylu fixů byl pod šumem GPS

`mise-robotour-armovani-rozptyl` · vada · **v kódu, na HW neověřeno** · nalezeno 26. 8. 2026 · vyřešeno 26. 8. 2026

Armování v depu čeká na okno kvalitních fixů GPS. Navržený práh 1,0 m byl pod nominálním šumem přijímače (σ 1,5 m) a statistika brala největší odchylku, která s délkou okna roste — delší čekání kritérium přitvrzovalo. Teď se měří RMS s prahem 2,5 m; tatáž veličina jde filtru jako sigma jednoho vzorku. Práh je z úsudku, panel naměřený rozptyl vypisuje, takže se má nastavit z prvních běhů na zařízení. Zároveň panel říká, PROČ se nepokračuje (fix nedorazil / nesplňuje kritéria / fixy jsou rozházené).

- [x] RMS místo maxima, práh 2,5 m (26. 8. 2026)
- [x] Kvalita fixu a rozptyl okna ve `MissionMsg` a v panelu (26. 8. 2026)
- [ ] Nastavit práh z naměřeného rozptylu na zařízení

[robotour-mission.md](robotour-mission.md), [rozhodnutí 26. 8. 2026](decisions.md) · DevLog [2026-08-26](devlog.md#2026-08-26)

<a id="mise-robotour-bez-operatora"></a>
### 🧪 Mise Robotour běží bez operátora — potvrzování cíle zrušeno

`mise-robotour-bez-operatora` · záměr · **v kódu, na HW neověřeno** · nalezeno 26. 8. 2026 · vyřešeno 26. 8. 2026

Úloha je simulace autonomního doručení: s robotem interagují jen odesílatel a odběratel, a to pouze QR kódem a stop tlačítkem. Potvrzovací tlačítko v panelu modelovalo někoho, kdo v úloze není, proto se zrušilo. Uvolnění stopu je od té doby plnohodnotný signál („vyloženo", nebo „člověk odešel" bez přečteného kódu); robot nikdy neodjede bez cíle a zamítnutý kód má viditelný důvod. Celý průchod misí autor proklikal v simulaci 27. 8.; na zařízení mise neběžela.

- [x] Zrušit potvrzování, uvolnění stopu jako signál, `MissionMsg` verze 5 (26. 8. 2026)
- [x] Důvod zamítnutí kódu ve zprávě a v panelu (nesrozumitelný / daleko / bez trasy) (26. 8. 2026)
- [x] Průchod misí v simulaci proklikán autorem (27. 8. 2026)

[robotour-mission.md](robotour-mission.md), [rozhodnutí 26. 8. 2026](decisions.md) · DevLog [2026-08-26](devlog.md#2026-08-26), [2026-08-27](devlog.md#2026-08-27)

<a id="mise-track"></a>
### 🧪 Mise Track — objezd míst ze souboru

`mise-track` · záměr · **v kódu, na HW neověřeno** · nalezeno 8. 9. 2026 · vyřešeno 8. 9. 2026

`mission=track track=<cesta>`: řádek = místo ve stupních, `repeat` = jezdit dokola. Každé místo se přichytí na nejbližší bod sítě cest — a to je oprava vady, ne kosmetika, protože dojezd se měří proti surovému cíli a mise by jinak u prvního bodu uvízla navždy. Bod dál než `trackoffroad=` misi přeruší, nesrozumitelný řádek je chyba, mezi body se nezastavuje a volba mise robota nerozjede (čeká na stisk a uvolnění nouzového zastavení). V simulaci objela tři místa a začala druhé kolo. Od 13. 9. se všechna místa přichycují předem při odjezdu, seznamy leží u map v `OSM/`. Na zařízení odjela 12. a 14. 9. (13 min, k prvnímu bodu 44 m za 9,5 minuty, běh z 17:06 skončil `NoRoute`); celý seznam neobjela. 17. 9. 2026 poprvé DOJELA na místo: trasa 147 m k bodu 1/3 za 3 minuty (16:06:51 → 16:09:53), pak si vzala cíl 2/3 (trasa 37 m) a obsluha ji v 16:11:37 zastavila ze stránky. Přichycení všech tří míst předem proběhlo (největší odstup 2,1 m z limitu 50 m). Jelo se to ale s rozbitým kurzem (viz `hw-zelezo-od-kabelu-kamer`), takže o chování mise po kalibraci to neříká nic. Druhý běh téhož dne zatuhl 4 s po odjezdu (`prov-zatuhnuti-za-behu-mise`). ✅ **18. 9. 2026 seznam poprvé objetý celý, i s `repeat`** (`20260918-154028.rec`: 3 místa za 5 min, druhé kolo za 2,5 min, s korekcemi z koridoru naostro); třetí kolo skončilo stáním před blokovanou mapou (`lp-zasek-v-blokovane-mape`). Druhý běh (`-155329.rec`): 5 míst za 8 min, k prvnímu bodu 93 m za 4 min, z toho 130 s stání po startu.

- [x] `TrackPlan`, `TrackMission`, `TrackMsg`, parametry, 36 testů (8. 9. 2026)
- [x] Projeto v simulaci (tři místa + druhé kolo) (8. 9. 2026)
- [x] Seznamy `*.track` přesunuty k mapám do `OSM/` (12. 9. 2026)
- [x] Projet celou misi na zařízení — 18. 9.: dvě celá kola (6 míst za 5 min) včetně `repeat`, ve druhém běhu 5 míst za 8 min (18. 9. 2026)
- [x] První dojezd na místo na zařízení (17. 9., bod 1/3 po trase 147 m za 3 min) (17. 9. 2026)
- [x] První jízdy na zařízení (12. 9., 14. 9.) — k prvnímu bodu dojela, seznam neobjela (14. 9. 2026)
- [ ] `trackoffroad=` nastavit z naměřených odstupů (údaj je v záznamu), ne z úsudku
- [ ] Hláška na stránce, když je `mission=track` bez `track=` (dnes se mise tiše nezaloží)

[track-mission.md](track-mission.md) · DevLog [2026-09-08](devlog.md#2026-09-08), [2026-09-12](devlog.md#2026-09-12), [2026-09-13](devlog.md#2026-09-13), [2026-09-14](devlog.md#2026-09-14), [2026-09-17](devlog.md#2026-09-17), [2026-09-18](devlog.md#2026-09-18)

<a id="mise-track-prichyceni-predem"></a>
### 🧪 Mise Track přichycuje všechna místa na síť předem, při odjezdu

`mise-track-prichyceni-predem` · záměr · **v kódu, na HW neověřeno** · nalezeno 13. 9. 2026 · vyřešeno 13. 9. 2026

Mise Track přichycovala místa na síť cest až ve chvíli, kdy na ně přišla řada — se seznamem, jehož druhé místo leží mimo síť, robot odjel na první a misi přerušil daleko od člověka. Teď se všechna místa přichytí a zkontrolují při odjezdu (ne už při volbě mise: tam bez pózy kontrola vracela nuly a tiše prošla i pro bod 372 m od cesty), `TrackMsg` verze 3 nese přichycené souřadnice a zóny na půdorysu se kreslí z nich, ne ze surových bodů. Autorem hlášený zásek po uvolnění nouzového zastavení se nereprodukoval; reprodukoval se jiný — simulace nad velkou mapou (3 771 uzlů) vyčerpá CPU renderem virtuální kamery a stránka umlkne, takže simulace není měřítko výkonu robota. Ověřeno v simulaci, na zařízení neběželo.

- [x] Přichycení všech míst v `Depart`, rozlišení „odstup 0“ od „nepřichyceno“ (13. 9. 2026)
- [x] `TrackMsg` verze 3 s přichycenými místy; zóny kreslené z nich (13. 9. 2026)
- [ ] Hlášený zásek po uvolnění stopu — nereprodukován, postup jak chytit zapsán (dotnet-stack + `PerfMsg`)
- [ ] Ověřit na zařízení

[track-mission.md](track-mission.md), [headless.md](headless.md) · DevLog [2026-09-13](devlog.md#2026-09-13)

<a id="mise-qr-jmeno-kamery"></a>
### 🧪 Skener QR na robotu nedostal jediný snímek — jméno kamery „Right" vs. „Right 740112071021"

`mise-qr-jmeno-kamery` · vada · **v kódu, na HW neověřeno** · nalezeno 19. 9. 2026 · vyřešeno 19. 9. 2026

Na soutěži 19. 9. 2026 robot v misi Robotour kód nepřečetl (`records/test/20260919-092933.rec`): automat byl 239 s ve fázi `Servicing` pod drženým stopem, skener zapnutý, a v záznamu není ani jedna `QrCodeMsg`. Kód přitom v obraze BYL — offline dekodér nad týmiž snímky ho čte v 725 z 3 957 (obě kamery, dva různé `geo:` texty). Příčina: skutečná D435 se jmenuje `Right 740112071021` (driver skládá název a sériové číslo), kdežto `QrScannerConfig.CameraName` je `Right` a porovnávalo se celé jméno — virtuální kamera vrací holý název, takže simulace i všech 19 testů procházely a na robotu skener nikdy nedostal snímek. Druhá past téhož dne: stránka náhledu kreslila PRVNÍ kameru ve slovníku (levou), takže obsluha podle telefonu ukazovala kód levé kameře, zatímco se četlo z pravé. Léčba: jméno kamery se bere jako první slovo (`QrScanner.CameraMatches`), stránka kreslí tu kameru, ze které se čte QR, a říká to v řádku „na obrázku (čte QR)". Nouzové obejití bez nové binárky: `qrcamera=` (prázdné = všechny kamery) v profilu. Měří to nový `ARBot.Analyze mission`.

- [x] Rozbor `ARBot.Analyze mission`: časová osa fází a stopu, servisní okna, snímky živým dekodérem (19. 9. 2026)
- [x] `QrScanner.CameraMatches` — shoda na první slovo jména (2 testy, 21 QR testů zelených) (19. 9. 2026)
- [x] Stránka náhledu kreslí kameru, ze které se čte QR (`WebStatus.PreferredCameraName`), a hlásí ji (19. 9. 2026)
- [x] Čtení kódu na robotu: `20260919-100414.rec` 2 `QrCodeMsg` a kód přijat, `-101057` 535, `-101903` 195 (19. 9. 2026)
- [ ] Ověřit na robotu, že stránka kreslí kameru, ze které se čte QR (řádek „na obrázku (čte QR)")

[robotour-mission.md](robotour-mission.md), [headless.md](headless.md) · DevLog [2026-09-19](devlog.md#2026-09-19)

<a id="mise-robotour-dalsi-nakladka"></a>
### 🧪 Změna pravidel Robotour 2026 — po vykládce další nakládka místo jízdy do depa

`mise-robotour-dalsi-nakladka` · záměr · **v kódu, na HW neověřeno** · nalezeno 19. 9. 2026 · vyřešeno 19. 9. 2026

Pravidla Robotour dovolují po úspěšné vykládce rozhodnout se pro další nakládku místo návratu do depa, po ní následuje další vykládka a opět volba — dokola. V automatu je to jediný rozdíl: servisní okno u vykládky má zapnutý skener a kód, který tam projde strojovými kontrolami, je místo další nakládky (`nextPickupChosen`); uvolnění stopu bez kódu znamená „žádná další nakládka, do depa“. Rozlišuje se `CodeExpected` (kód se přijímá na každém stanovišti) a `CodeRequired` (bez něj se neodjede — depo a nakládka vrací na `AwaitingEStop`). Stránka náhledu i UI panel u vykládky hlásí „vyloženo: QR kód DALŠÍ nakládky, nebo uvolnění stopu bez kódu = jízda do depa“ (`MissionWait.QrCodeOrRelease`, `MissionStatusText.WaitFor(phase, stop)`); „kód nevidím“ se u vykládky nehlásí. `MissionMsg` je verze 7 (`Deliveries` = počet vykládek, `NextPickupChosen`); `PickupLatDeg`/`DropLatDeg` jsou od té doby poslední nakládka/vykládka. Limit vzdálenosti od depa platí i pro další nakládky.

- [x] Automat: skener u vykládky, kód = další nakládka, uvolnění bez kódu = depo; `MissionMsg` v7 (19. 9. 2026)
- [x] Hlášení na stránce a v UI panelu (`QrCodeOrRelease`), 3 nové testy, 2 přepsané (Common 1 601, Runtime 140) (19. 9. 2026)
- [x] Průchod vykládka → kód další nakládky → nakládka → vykládka → depo proklikán autorem v Avalonii na virtuálním HW (19. 9. 2026)
- [ ] Ověřit na robotu (skutečné kamery, stop tlačítko, stránka náhledu)

[robotour-mission.md](robotour-mission.md) · DevLog [2026-09-19](devlog.md#2026-09-19)

<a id="mise-robotour-depothdop"></a>
### 🧪 Mise se v depu nezarmovala — práh HDOP 2,0 mezi budovami nesplnitelný

`mise-robotour-depothdop` · vada · **v kódu, na HW neověřeno** · nalezeno 19. 9. 2026 · vyřešeno 19. 9. 2026

Na soutěži 19. 9. 2026 stála mise Robotour 158 s v `ArmingAtDepot` (`20260919-101546.rec`), stránka ukazovala „sigma 60–70 m“. Ta sigma je `gpsposstd × HDOP`, tedy nejistota pro fúzi, ne kritérium mise: to je fix + ≥ 6 družic + HDOP ≤ 2,0 nepřerušeně 5 s, pak RMS rozptyl ≤ 2,5 m. Mezi budovami byl HDOP 1,74–2,95 (p50 2,30, p90 2,50) při 12–16 družicích, prahu 2,0 vyhovovalo 4,7 % fixů a nejdelší nepřerušená série byla 3 s; s prahem 3,0 vyhovuje 100 % fixů obou ranních záznamů. Práh je teď parametr `depothdop=` (default 2,0 z `RobotourConfig` se nemění), provozní profil `pi-provoz.cfg` má 3,0 — rozptyl polohy hlídá `MaxSpreadM` dál. Měří to blok 1b `ARBot.Analyze mission` (percentily HDOP, % vyhovujících fixů, nejdelší série pro 2,0 / 2,5 / 3,0 / 4,0).

- [x] Blok 1b v `ARBot.Analyze mission`: kvalita fixu v `ArmingAtDepot` proti kritériu mise (19. 9. 2026)
- [x] Parametr `depothdop=` (registr, runtime, `pi-provoz.cfg` = 3,0) (19. 9. 2026)
- [ ] Ověřit na robotu: armování v depu s `depothdop=3` do 5 s od stisku

[robotour-mission.md](robotour-mission.md), [configuration.md](configuration.md) · DevLog [2026-09-19](devlog.md#2026-09-19)

<a id="mise-robotour-mapa-ostrov"></a>
### 🧪 Kód se četl a mise ho zamítala „nevede trasa“ — robot stál na náměstí spojeném se sítí jen schody

`mise-robotour-mapa-ostrov` · vada · **v kódu, na HW neověřeno** · nalezeno 19. 9. 2026 · vyřešeno 19. 9. 2026

Na soutěži 19. 9. 2026 (`20260919-101057.rec`, `-101903.rec`) se QR kód četl (535 a 195 `QrCodeMsg`), ale mise ho pokaždé zamítla hláškou „na cíl nevede po síti žádná trasa (je mimo mapu?)“ — a stránka náhledu dál psala „čeká se na QR kód“, takže obsluha myslela, že se kód nečte. Cíl `50.1038082,14.4240751` je přitom přesně uzel mapy na živé `footway`. Rozbor proti `MapMsg` a `GlobalNavMsg` ze záznamu: síť `Robotour2026-ver1.osm` má pod profilem Robot 2 komponenty souvislosti; ostrov je jediná cesta 956523901 (`highway=pedestrian` + `area=yes`, dlážděné náměstí, 40 uzlů, 139 m) spojená se sítí jen `highway=steps` (uzly 8852424426 a 8852424425, 0,9 m od sebe) — a schody profil Robot nepouští. Robot při zamítnutí stál 3–4 m od uzlu ostrova. `Probe` odpověděl podle grafu správně, ale hláška posílala člověka hledat chybu jinam a stránka ji neukázala. V 10:04 týž kód projel, protože robot stál o 50 m dál na chodníku. Léčba v kódu: řádky „QR kódy“ a „kód ZAMÍTNUT“ na stránce, hláška „z místa, kde robot stojí … síť rozpojená“, nový `ARBot.Analyze route` a blok 1c v `mission`. Ostrov je podle autora skutečný (robot tam nevyjede, GPS ho tam jen posadila), takže se neopravuje mapa, ale načtení: `mapprune=` (výchozí true, `NetworkIslands`) zahodí všechny komponenty kromě té s největší délkou cest v metrech (ne podle počtu uzlů, ne podle toho, kde robot stojí — právě ta póza je z chybné GPS). Offline z pózy na náměstí se póza přichytí na chodník 2,4 m vedle a cíl je dosažitelný (393 m). Co se zahodilo, jde do Trace; `mapprune=false` vrátí síť.

- [x] Rozbor `ARBot.Analyze route` (komponenty, cesty ostrova, nejbližší dvojice uzlů) a blok 1c v `mission` (19. 9. 2026)
- [x] Stránka náhledu ukazuje počet přečtených/zamítnutých kódů a důvod zamítnutí (19. 9. 2026)
- [x] Hláška zamítnutí říká, že trasa nevede z místa, kde robot stojí, a že síť může být rozpojená (19. 9. 2026)
- [x] Ostrovy sítě zahodit při načtení mapy (`mapprune=`, `NetworkIslands`, 5 testů); `route` z náměstí: dosažitelné 393 m (19. 9. 2026)
- [ ] Ověřit na robotu přijetí kódu z náměstí (Trace „ZAHOZENO 1“ v záznamu) a zobrazení zamítnutí na stránce

[robotour-mission.md](robotour-mission.md), [osm-nav.md](osm-nav.md) · DevLog [2026-09-19](devlog.md#2026-09-19)

<a id="mise-nouzove-zastaveni-controlloop"></a>
### ✅ Nouzové zastavení v řídicí smyčce a ve firmwaru motorů

`mise-nouzove-zastaveni-controlloop` · záměr · **hotovo** · nalezeno 11. 8. 2026 · vyřešeno 30. 8. 2026

Stav tlačítka nouzového zastavení tekl do řídicí smyčky už dřív, jen se zahazoval. Smyčka teď pod stopem posílá nulovou rychlost a rotaci nuluje až ve stoje (dobrzdění zůstává řízené), ostatní smyčky běží dál, takže po uvolnění robot plynule pokračuje; do záznamu jde příznak, proč byla nula. Stejné pravidlo se zapsalo i do MicroBasic skriptu řadiče SDC2160 (dřív nuloval rotaci hned, takže brzdil vždy rovně). Skript je zdroj, ne kompilovaný kód, takže se do jednotky nahrává zvlášť — nahrán 30. 8. (značka `Version 2.0`) a podle autora na robotu ověřen.

- [x] `IsEmergencyStop` v `ControlLoop`, `DriveCommandMsg` verze 2 (12. 8. 2026)
- [x] Skript řadiče upraven na totéž pravidlo (v repu) (12. 8. 2026)
- [x] `RizeniDiffPodvozku.mbs` dosynchronizován ze skriptu v komentáři (18. 8. 2026)
- [x] Nahrát skript do jednotky (`Version 2.0`) (30. 8. 2026)
- [x] Ověřeno na zařízení (sdělení autora 17. 9. 2026) (17. 9. 2026)

[robotour-mission.md](robotour-mission.md), [path-following.md](path-following.md) · DevLog [2026-08-11](devlog.md#2026-08-11), [2026-08-12](devlog.md#2026-08-12), [2026-08-18](devlog.md#2026-08-18), [2026-08-30](devlog.md#2026-08-30)

<a id="mise-freerun"></a>
### ✅ Mise FreeRun — jízda v pravé polovině koridoru bez mapy

`mise-freerun` · záměr · **hotovo** · nalezeno 25. 8. 2026 · vyřešeno 7. 9. 2026

Jednodušší mise před Robotourem, pro homologaci a přesun mezi stanovišti: držet se v pravé polovině detekovaného koridoru, překážkám se vyhýbat lokální mapou, bez mapové navigace; když koridor není, držet kurz. Je to jen producent „mrkve" pro existující lokální vrstvu, takže je malá — musela se ale vytáhnout mapově nezávislá část hledání koridoru (`CorridorSource`). V simulaci se usadí na −0,503 m proti požadovaným −0,500. Mise se vybírá selektorem `mission=none|freerun|robotour`, protože mise se vylučují. Venku poprvé jela 7. 9. (452 s, ~105 m); robot se přitom plazil, ale to bylo lokální plánování a chybný kurz, ne mise.

- [x] Implementace, extrakce `CorridorSource`, měření proti pravdě (dva běhy) (25. 8. 2026)
- [x] Profil `config/pi-freerun.cfg` pro Orange Pi (1. 9. 2026)
- [x] První jízda venku na zařízení (`20260907-170728.rec`) (7. 9. 2026)

[mission-freerun.md](mission-freerun.md) · DevLog [2026-08-25](devlog.md#2026-08-25), [2026-09-01](devlog.md#2026-09-01), [2026-09-07](devlog.md#2026-09-07)

<a id="mise-robotour-panel"></a>
### ✅ Panel mise Robotour a nouzové zastavení v simulaci

`mise-robotour-panel` · záměr · **hotovo** · nalezeno 26. 8. 2026 · vyřešeno 27. 8. 2026

Ovládací panel mise (fáze, na co se čeká, přečtený kód s odvozeným cílem, čítače, Start / Přerušit) čte stav ze zpráv, takže funguje i při přehrávání záznamu. Bez nouzového zastavení ve virtuálních motorech se servisní okno v simulaci nedalo projít vůbec — přibylo červené tlačítko stopu. Autor panel používal a během dvou dnů nahlásil řadu vad: panel tvrdil „mise neběží" (runtime si stupeň uložil dřív, než vznikl), čas mise 6·10¹⁰ s, deska s kódem mizela hned po postavení, kód zkosený, nečitelná tlačítka, kamera zmizela dokumentům mimo hlavní dok (obecná vada `IsActive`). Vše opraveno a 27. 8. proklikáno („vše funguje jak má").

- [x] Panel *Tools → Mise Robotour*, stop tlačítko ve virtuálních senzorech (26. 8. 2026)
- [x] 'Opravy z používání: stupeň hledat znovu, čas mise, deska s kódem, náhled kamery, styl tlačítek' (26. 8. 2026)
- [x] `IsActive` pro dokumenty mimo `DocumentDock`, tlačítka s hotovými kódy, stop jako červené tlačítko (27. 8. 2026)

[robotour-mission.md](robotour-mission.md), [Views/README.md](../Src/ARBot/Views/README.md) · DevLog [2026-08-26](devlog.md#2026-08-26), [2026-08-27](devlog.md#2026-08-27)

<a id="mise-magcal"></a>
### ✅ Robot si kalibraci magnetometru změří sám z telefonu (`mission=magcal`)

`mise-magcal` · záměr · **hotovo** · nalezeno 8. 9. 2026 · vyřešeno 12. 9. 2026

Místo notebooku v poli: robot stojí, obsluha s ním otáčí rukou, stránka náhledu říká, co ještě chybí, a po ťuknutí pod drženým nouzovým zastavením se kalibrace zapíše do registru 23 a do flash. Práh podmíněnosti byl odhadnut o pět řádů mimo, náklony musí být na obě strany, sklon se počítal ve špatném rámci, a proložení všech vzorků by misi po minutě zadusilo (plná SVD) — vyřešeno ředěním na 1 500 vzorků. První výjezd 10. 9. našel tři vady (verdikt byl diagnóza, ne pokyn; koše podle velikosti odklonu; chyběla mapa pokrytí), druhý týž den dal použitelnou kalibraci, zapsanou 11. 9. a ověřenou 12. 9. Od 12. 9. si mise registr 23 před sběrem sama vymaže, jinak by druhé spuštění dobrou kalibraci přepsalo — to na senzoru neběželo. Vymazání registru 23 před sběrem (12. 9.) je samostatné téma a na senzoru ještě neběželo.

- [x] Fáze 1 — `MagCalFit`, pokrytí, kolektor, mise, registry, `ARBot.Analyze magcal` (8. 9. 2026)
- [x] Výkon proložení (ředění na `MaxFitSamples`, podmíněnost invariantní) (8. 9. 2026)
- [x] První výjezd — tři vady opraveny (koule, mapa pokrytí 24×5, verdikt jako pokyn) (10. 9. 2026)
- [x] Sklon vyřazen z brány (rozhodnutí autora), kalibrace použitelná (10. 9. 2026)
- [x] Zápis do senzoru a ověření venku (12. 9. 2026)

[plan-vn100-kalibrace.md](plan-vn100-kalibrace.md), [plan-vn100-kalibrace-kroky.md](plan-vn100-kalibrace-kroky.md), [imu-and-frames.md](imu-and-frames.md), [rozhodnutí 10. 9. a 12. 9. 2026](decisions.md) · DevLog [2026-09-08](devlog.md#2026-09-08), [2026-09-10](devlog.md#2026-09-10), [2026-09-11](devlog.md#2026-09-11), [2026-09-12](devlog.md#2026-09-12)

## Provoz na zařízení

<a id="prov-perf-monitoring"></a>
### ⬜ Měření výkonu řízení — stíhá řídicí smyčka svou periodu?

`prov-perf-monitoring` · záměr · **otevřeno** · nalezeno 1. 9. 2026

Do té doby nešlo z běhu poznat, jestli řídicí smyčka stíhá svých 10 taktů za sekundu. Měření sedí ve scheduleru (jediné místo, které zná plánovaný i skutečný čas taktu): obsazenost periody, zpoždění a zameškané takty, fronty a zahozené zprávy stupňů, CPU procesu — jednou za sekundu jako `PerfMsg` do proudu, tedy do UI i do záznamu, a panel Tools → Výkon (`perf=`, práh `perfwarn=`). První měření hned našlo zameškané takty na Windows (samostatné téma). Na zařízení `PerfMsg` chodí do záznamu od 4. 9. Fáze 3 (teplota, frekvence, CPU stroje přes HAL) a fáze 4 (`ARBot.Analyze perf`) se nezačaly; `perfwarn` je pořád odhad.

- [x] Fáze 1–2 — měření ve `Scheduler`u, `PerfCollector`, `PerfMsg`, panel Výkon, 23 testů (1. 9. 2026)
- [x] `PerfMsg` v záznamu ze zařízení (headless, Orange Pi) (4. 9. 2026)
- [ ] Fáze 3 — teplota, frekvence, CPU stroje, CPU čas taktu (přes HAL)
- [ ] Fáze 4 — `ARBot.Analyze perf` nad záznamem
- [ ] Nastavit `perfwarn` z měření na Orange Pi (dnes odhad 70 %)

[perf-monitoring.md](perf-monitoring.md), [plan-perf-monitoring.md](plan-perf-monitoring.md) · DevLog [2026-09-01](devlog.md#2026-09-01), [2026-09-04](devlog.md#2026-09-04), [2026-09-05](devlog.md#2026-09-05)

<a id="prov-zameskane-takty-windows"></a>
### ⬜ Řídicí smyčka na Windows zamešká 3–4 takty za sekundu, ačkoli práce trvá pod 1 ms

`prov-zameskane-takty-windows` · vada · **otevřeno** · nalezeno 1. 9. 2026

První měření nového panelu Výkon hned něco našlo: v simulaci na Windows scheduler nestihne 3–4 takty za sekundu a zpoždění jde až na 108 ms při periodě 100 ms, přestože vlastní práce taktu trvá pod milisekundu. Brzdí tedy časovač, ne řídicí kód. Padla tím podmínka, kterou si spec kladla pro dva odložené nálezy (dohánění zameškaných taktů, krok rampy dobrzdění z periody). Nic se neopravuje: číslo je z Windows, kde hrubé rozlišení časovače stačí jako vysvětlení; první krok je přeměřit totéž na Orange Pi a podle toho nastavit `perfwarn`.

- [x] Změřit v simulaci na Windows (1. 9. 2026)
- [ ] Přeměřit totéž na Orange Pi a rozhodnout o dohánění taktů

[perf-monitoring.md](perf-monitoring.md), [plan-perf-monitoring.md](plan-perf-monitoring.md) · DevLog [2026-09-01](devlog.md#2026-09-01)

<a id="prov-start-po-rebootu"></a>
### ⬜ Start služby po skutečném rebootu Orange Pi se nezkoušel

`prov-start-po-rebootu` · záměr · **otevřeno** · nalezeno 5. 9. 2026

Služba `arbot` je `enabled` s `Restart=always`, restart služby i SIGTERM jsou ověřené, ale nikdo ještě nenechal desku nabootovat od studena a nezkontroloval, že runtime nastartuje, rozjede senzory a stojí, dokud mu člověk nevybere misi. Souvisí s tím i kamery, které se po bootu občas vyčtou jen na USB 2.0.

- [ ] Reboot desky a kontrola journalu, náhledu a senzorů po startu

čeká na [hw-kamery-usb2-po-bootu](#hw-kamery-usb2-po-bootu) · [headless.md](headless.md), [deploy/README.md](../deploy/README.md) · DevLog [2026-09-05](devlog.md#2026-09-05)

<a id="prov-sigsegv-pri-stop"></a>
### ⬜ Nativní pád (SIGSEGV) při zastavování runtime je častý

`prov-sigsegv-pri-stop` · vada · **otevřeno** · nalezeno 14. 9. 2026

V reprodukčním testu (32 kol volba mise → stop → restart) proces 12× spadl nativně při `Stop()`; dvě další SIGSEGV při `Stop()` přišly týž den za provozu. `CrashLog` nativní pád nezachytí, takže po něm nezůstane stopa. Příčina se nehledala; s vypnutým start-limitem je důsledek restart služby, ne trvalé odstavení, ale pád při každém druhém zastavení zůstává.

[headless.md](headless.md) · DevLog [2026-09-14](devlog.md#2026-09-14)

<a id="prov-zatuhnuti-za-behu-mise"></a>
### ⬜ Runtime zatuhl 4 s po odjezdu mise Track a hlídač ho nechytil

`prov-zatuhnuti-za-behu-mise` · vada · **otevřeno** · nalezeno 17. 9. 2026

Runtime naběhl, senzory i řídicí smyčka běžely, v 16:12:38 obsluha uvolnila nouzové zastavení, mise Track odjela (přichytila 3 místa, cíl 1/3, trasa 50 m) — a do 20 ms po té hlášce přestaly proudit všechny zprávy najednou: IMU v 4,074 s záznamu, motory 4,078, GPS 4,068, obě kamery, řídicí smyčka i koridor. Dál žilo jediné vlákno — dotazování odpojené T265, jehož hlášky tekly do záznamu ještě 140 s. Stránka podle obsluhy odpovídala, ale čas v ní neběžel, což s tím sedí: web i zapisovač záznamu byli naživu, produkce dat ne. Zásek NENÍ v ovladačích, a je to změřené: vlákno T265 je taky `SensorBase` a běželo dál. Jediné, čím se od ostatních liší, je, že nic nepublikuje (dotaz vždy selže a jen loguje) — všechna vlákna, která publikují, stojí. `Info` z `TraceInfoBridge` jde přímo na `Stream`, kdežto měření jdou navíc přes `RoleRouter` do `processing` (`RelaySource`, fan-out na vlákně producenta), a právě ta větev je společná všemu, co umlklo. Konkrétní blokující odběratel ze statického čtení vidět není (stupně mají vlastní frontu `DropOldest`, `RecordingTarget` je `DropNewest`, takže žádný `Post` blokovat nemá), takže dál se pohne až ze zásobníků vláken. `HangWatchdog` nevystřelil, protože hlídá jen `Start(Mode.Run)` — ten skončil o 4 s dřív a token byl zahozen. Minidump proto neexistuje a jediný důkaz je záznam. ⚠️ **Není to totéž co zásek o devět minut později** (16:21, `prov-deadlock-mise-webstatus`), ačkoli spouštěč je stejný (volba mise `track` ze stránky). Ten je deadlock mezi zámky `TrackMission` a `WebStatus`, oba na fan-outu do `Stream` — jenže kdyby byl držený zámek `WebStatus`, zastavil by se i most `Trace → Info`, protože `WebStatus` je na `Stream` připojený jako PRVNÍ (v `ARBot.Headless/Program.cs`, mimo `connections`, tedy před záznamem). A `Info` teklo dál 140 s. Tenhle zásek je tedy na větvi `processing`, ne na `Stream`, a zůstává neurčený.

- [x] Rozbor záznamu — kdy to umlklo, co přežilo a co mají umlklá vlákna společného (17. 9. 2026)
- [ ] Hlídač i na běžící smyčku (tep ze `Scheduler`u), aby zásek za jízdy vyrobil minidump
- [ ] Ze zásobníků vláken určit blokujícího odběratele na větvi `processing`

[headless.md](headless.md), [RelaySource.cs](../Src/ARBot.Common/Communication/RelaySource.cs), [MessageSource.cs](../Src/ARBot.Common/Communication/MessageSource.cs) · DevLog [2026-09-17](devlog.md#2026-09-17)

<a id="prov-zaznam-nevidet-ze-nebezi"></a>
### ⬜ V terénu není poznat, jestli se běh nahrává a kam

`prov-zaznam-nevidet-ze-nebezi` · vada · **otevřeno** · nalezeno 17. 9. 2026

Po jízdě 17. 9. 2026 (track po kalibraci magnetometru, podle obsluhy s dobrým kurzem) se nenašel žádný `*.rec` — a proč, to ze záznamů dohledat nejde, protože chybí právě ten záznam. Při hledání se ukázalo, že o nahrávání nemluví nic, co má obsluha v terénu po ruce, a u robota je jen mobil: `RecordPathFromParams()` napíše „beh se zaznamenava do …" do `Trace` PŘED tím, než runtime stojí, takže ta hláška skončí jen v journalu a do záznamu se z principu dostat nemůže — táž past jako s účinnou konfigurací, opravená 5. 9. 2026 zopakováním po připojení mostu. Když je `record=` prázdné nebo `false`, vrátí se `null` MLČKY, takže neexistuje ani ta jedna řádka. `ARBotRuntime.RecordPath` se plní jen ve `WireView`, v `Run` zůstává `null`, tedy runtime ani sám neví, kam píše. A stránka náhledu o nahrávání nemá ani slovo. Běh, který se nenahrál, je proto v terénu k nerozeznání od běhu, který se nahrál. Dohledáno na zařízení týž den — a jsou to DVA různé běhy, ne jeden. Pokus v 16:21:22 (`20260917-162122.rec`) se do souboru nedostal vůbec: `Start(Run)` uvízl na deadlocku (`prov-deadlock-mise-webstatus`) o kus dřív, než se `FileStream` vůbec zakládá, takže hláška „beh se zaznamenava do …" v journalu je, ale soubor nikdy nevznikl. Ta hláška se totiž tiskne v `RecordPathFromParams()`, tedy PŘED `WireRun` — **říká záměr, ne výsledek**, a to je samo o sobě matoucí. Druhý pokus je v následujícím bootu (16:22:52) a **robota skutečně řídil** — obsluha má snímky stránky z **16:26** (`běží true`, kurz 0,355 rad, rychlost **0,905 m/s**, trasa, plán i mrkev) a z **16:29**. Hodiny přitom sedí na sekundy: snímek z 16:20 zachycuje dialog o zápisu kalibrace (journal 16:20:16–20) a snímek z 16:21 ukazuje **vlastní čas stránky 16:21:22**, tedy přesně okamžik `POST /mission?m=track` v journalu. ⚠️ **Po tom běhu ale nezůstalo NIC — ani záznam, ani journal — a to je pořád neurčené.** Journal toho bootu končí v **16:23:24** (zatímco robot jel ještě aspoň 6 minut), služba `arbot` v něm nevypsala **ani řádek**, ačkoli jádro ve stejné chvíli hlásí enumeraci kamer (tedy aplikace běžela), a v datovém adresáři není mezi 16:22 a 18:28 zapsaný ani bajt. Plný disk to nebyl (366 GB volných), `record=true` platilo všude a hodiny jsou z RTC. ⚠️ **Prověřeno na zařízení 17. 9. večer a nic z toho to nevysvětluje** — vyloučeno je: plný disk (366 GB volných), chyba disku nebo `errors=remount-ro` (v journalu žádná chyba ext4/NVMe), ztráta přes armbian-ramlog (`/var/log` sice JE na zramu, ale `journal` je symlink na `/var/log.hdd`, tedy na disk), záznam odložený vedle binárek (`~/arbot-headless-run/records/` neexistuje), škrcení journaldem (žádné „Suppressed"), posun hodin (snímky stránky sedí s journalem na sekundy) a jiný boot (výpis všech 18 bootů mezi 16:23:24 a 18:28:13 žádný nemá). **Co zbývá jako holý fakt:** v tom bootu služba `arbot` nastartovala v 16:23:00, jádro v 16:23:16–24 enumeruje obě D435 (tedy aplikace běžela a otevírala kamery) — a služba přitom do journalu nevypsala **ani jeden řádek**, ani úvodní verzi, ani výpis konfigurace, ani „Ceka se na vyber mise". Chybí zároveň **výstup do journalu i záznam**, tedy obojí, co aplikace zapisuje, ačkoli podle snímků jela a řídila robota rychlostí 0,9 m/s. Další krok je **reprodukce**: pustit touž misi znovu a sledovat, jestli se výpis do journalu objeví. Nezávisle na tom dává smysl odstranit spouštěč celé té epizody — deadlock (`prov-deadlock-mise-webstatus`). ⚠️ **Hypotéza „tvrdé vypnutí to spolklo" NESTAČÍ** a je poctivé to říct: `/etc/fstab` má kořen na `commit=120` a `RecordingTarget.OnFlush` dělá jen spravovaný `Flush()` do stránkové cache (**nikdy `fsync`**), takže se ztratí nanejvýš ~2 minuty — jenže mezi posledním řádkem journalu a snímkem obrazovky je **5,5 minuty**. ✅ **Hodiny robota ale z podezřelých vypadly, a to měřením ZE ZÁZNAMU** (`ARBot.Analyze gps`, nový blok A0): GPS nese ITOW, tedy absolutní čas, který nepochází z hodin Pi. Po rekonstrukci (viz `hw-gps-fixtime-rozbity`) vychází den v týdnu **4 = čtvrtek** a systémový čas je proti GPS **+3,9 s** v jednom záznamu a **+4,0 s** v druhém — tedy hodiny jdou a ty ~4 s jsou konstantní zpoždění řetězu, ne drift. Časová osa v záznamech i v journalu je proto skutečná a v 16:29 byl robot podle vlastních hodin **vypnutý**. Čas 16:29 tedy pochází z jiných hodin než robotových; rozhodne obsah toho snímku. Nezávisle na tom platí, že chybějící `fsync` je vada sám o sobě: záznam nemá přežít odpojení napájení jen náhodou.

- [x] Nález — proč to nejde dohledat (žádná stopa mimo journal, stránka mlčí) (17. 9. 2026)
- [ ] `RecordPath` plnit i ve `WireRun` a dát na stránku řádek „nahrává se do …" / „BEZ ZÁZNAMU"
- [ ] Hlásit i případ `record=false` a hlášku zopakovat po připojení `TraceInfoBridge`
- [ ] Na zařízení dohledat, proč záznam ze 17. 9. po kalibraci chybí — pokus 16:21 vysvětlen, běh v 16:29 NE
- [ ] Záznam přežije odpojení napájení: `fsync` (`Flush(true)`) v `OnFlush`, nebo aspoň hned po založení souboru
- [ ] Hláška „beh se zaznamenava do …" má říkat výsledek, ne záměr (dnes se tiskne před `WireRun`)

[record-replay.md](record-replay.md), [ARBotRuntime.cs](../Src/ARBot.Runtime/Robot/ARBotRuntime.cs), [WebStatus.cs](../Src/ARBot.Runtime/Web/WebStatus.cs) · DevLog [2026-09-17](devlog.md#2026-09-17)

<a id="prov-pudorys-umysl-a-zony"></a>
### 🧪 Půdorys náhledu ukazuje, co se robot chystá udělat, a zóny k dosažení

`prov-pudorys-umysl-a-zony` · záměr · **v kódu, na HW neověřeno** · nalezeno 12. 9. 2026 · vyřešeno 12. 9. 2026

Na půdorysu stránky náhledu přibyla trasa globální navigace, dráha z lokálního plánovače (i s uzly, z jejichž rozestupu je vidět vyhlazování), legenda včetně dosud nepopsané ujeté dráhy a kružnice o dojezdovém poloměru kolem míst mise (aktivní plnou čarou), aby šlo v terénu dohlížet na závod. Kvůli tomu nese `GlobalNavMsg` dojezdový poloměr a `TrackMsg` celý seznam míst; od 13. 9. se zóny kreslí na přichycených místech, od 14. 9. jsou i vrstvou ve World pohledu (`GoalZones`). Zdroje se nesčítají — přednost má to, co zadal člověk. Ověřeno testy a jízdou v simulaci, na zařízení neběželo.

- [x] Trasa a lokální plán s prahem stáří, legenda (12. 9. 2026)
- [x] Zóny o dojezdovém poloměru, `GlobalNavMsg` v2 a `TrackMsg` v2 (12. 9. 2026)
- [x] Zóny na přichycených místech (`TrackMsg` v3) (13. 9. 2026)
- [ ] Ověřit na zařízení

[headless.md](headless.md), [world-view.md](world-view.md), [track-mission.md](track-mission.md) · DevLog [2026-09-12](devlog.md#2026-09-12), [2026-09-13](devlog.md#2026-09-13), [2026-09-14](devlog.md#2026-09-14)

<a id="prov-start-limit-sluzba"></a>
### 🧪 Služba se po pěti restartech v pěti minutách vzdá a robot je mrtvý

`prov-start-limit-sluzba` · vada · **v kódu, na HW neověřeno** · nalezeno 14. 9. 2026 · vyřešeno 15. 9. 2026

Systemd má výchozí `StartLimitBurst=5` / `StartLimitIntervalUSec=5min`: šestý start v okně skončí `start-limit-hit`, jednotka zůstane `failed` a `Restart=always` ji už nevrátí — crash loop odstaví robota v terénu natrvalo do ručního `reset-failed`. Našlo se to na reprodukčním testu 14. 9., kdy počítadlo restartů došlo na 4 z 5. Limit vypnut (`StartLimitIntervalSec=0`) a přidán rostoucí `RestartSec`; je to bezpečné, protože se robot bez výběru mise sám nerozjede a deterministické příčiny (návratové kódy 2, 3) dál odfiltruje `RestartPreventExitStatus`. Na zařízení neběželo.

- [x] Nález na reprodukčním testu (restart counter 4 z 5) (14. 9. 2026)
- [x] `StartLimitIntervalSec=0` + rostoucí `RestartSec` v jednotce (15. 9. 2026)
- [ ] Ověřit na zařízení (jednotku nasadit a vyvolat opakovaný pád)

[headless.md](headless.md), [deploy/README.md](../deploy/README.md) · DevLog [2026-09-14](devlog.md#2026-09-14), [2026-09-15](devlog.md#2026-09-15)

<a id="prov-audit-bezpecnost-rizeni"></a>
### 🧪 Externí audit — bezpečnostní vrstva řízení měla čtyři díry

`prov-audit-bezpecnost-rizeni` · vada · **v kódu, na HW neověřeno** · nalezeno 15. 9. 2026 · vyřešeno 15. 9. 2026

Autor dodal externí read-only audit; tři kritické a jeden vysoký nález se potvrdily přímo ve zdroji. Výjimka v taktu řídicí smyčky se polykala a robot jel po posledním příkazu až do zásahu 500ms watchdogu motorů (K1); `Stop()` nikdy neposlal motorům nulu, ačkoli dokumentace tvrdila opak (V1); brána „mise jen při drženém nouzovém zastavení“ prošla s odpojeným motorovým UARTem, protože fail-rámec driveru se četl jako stisk tlačítka a obnovení linky jako pokyn „jeď“ (K2); jedno NaN měření otrávilo fúzi natrvalo, protože gating na NaN nezabere (K3). K tomu diagnostika poruch z `Debug` do `Trace` na cestách, které audit jmenoval, se škrtičem `PoruchaHlasic` proti zaplavení záznamu (V3). Opraveno TDD — u každého nálezu nejdřív test, který na starém kódu spadl. Nic z toho neběželo na zařízení.

- [x] K1 — výjimka v taktu = `Drive(0,0)` + zpráva s nulami + `Trace` (15. 9. 2026)
- [x] V1 — nula motorům v `ControlLoop.Stop()` (15. 9. 2026)
- [x] K2 — fail-rámec není měření tlačítka (`HasMeasurement`), automaty mise i brány stojí (15. 9. 2026)
- [x] K3 — brána na konečnost před fúzí a pojistka na výsledek kroku EKF (15. 9. 2026)
- [x] V3 — `Debug` → `Trace` se škrtičem, `DiagnostikaPoruchTests` (15. 9. 2026)
- [ ] Ověřit na zařízení

[path-following.md](path-following.md), [ekf-fusion.md](ekf-fusion.md) · DevLog [2026-09-15](devlog.md#2026-09-15)

<a id="prov-audit-druha-davka"></a>
### 🧪 Externí audit, druhá dávka — tichý senzor, zatuhlý Stop, razítka kamer, CI, licence

`prov-audit-druha-davka` · vada · **v kódu, na HW neověřeno** · nalezeno 15. 9. 2026 · vyřešeno 15. 9. 2026

Zbytek nálezů auditu: po odpojení USB převodníku se senzor tvářil jako zdravý (V4, nově „nic neměřím“ je chyba); `Stop()` mohl zatuhnout navždy na UARTu bez dat — běh testů s vypnutou opravou nedoběhl vůbec (V5); razítka T265 a D435 běžela mimo `TimeBase`, u D435 se teď kotví jen počátek, aby šel dál poznat zamrzlý stream (V6); datový závod na cíli navigace mezi stupněm a misí (V7); softwarová porucha vize se přičítala k výpadkům kamery (V8); čistý klon nešel postavit a chybělo CI (V10); profil FreeRun rozjížděl robota sám po startu (V11); `vnrestore.sh` zapisoval do flash bez ptaní (V12); nevyplněná licence a chybějící soupis cizích součástí (V13). Nález V2 (fúze extrapoluje bez omezení) autor rozhodl neřešit; ze středních nálezů (`HeadVeh` čte špatný offset, `FixTime` z ITOW, driver motorů nekontroluje prefixy řádků) se nesáhlo na nic. Na zařízení neběželo nic.

- [x] V4 + V5 — `SilentTimeout`, `StopTimeout`, konečný `ReadTimeout` UARTu (15. 9. 2026)
- [x] V6 — razítka kamer v `TimeBase` (`DeviceClockAnchor`), `CasZTimeBaseTests` (15. 9. 2026)
- [x] V7 + V8 — cyklus navigace pod zámkem, `CameraVisionStep` (15. 9. 2026)
- [x] V10 — build čistého klonu, přeskakování testů bez nativní knihovny, CI workflow (15. 9. 2026)
- [x] V11 + V12 — `autorun=false` v profilu, potvrzení zápisu do flash, `set -euo pipefail` (15. 9. 2026)
- [ ] V13 — ověřit podmínky tří proprietárních binárek, atribuce OSM (ODbL)
- [ ] Střední nálezy (`PVTMessage.HeadVeh`, `FixTime` z ITOW, prefixy řádků `SDC2160Ex`)
- [ ] Ověřit V4/V5/V6 na zařízení (odpojit převodník za běhu, `systemctl stop`)

[THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md), [build-and-test.yml](../.github/workflows/build-and-test.yml), [deploy/README.md](../deploy/README.md), [record-replay.md](record-replay.md) · DevLog [2026-09-15](devlog.md#2026-09-15)

<a id="prov-deadlock-mise-webstatus"></a>
### 🧪 Deadlock mezi zámkem mise a zámkem stránky při volbě mise

`prov-deadlock-mise-webstatus` · vada · **v kódu, na HW neověřeno** · nalezeno 17. 9. 2026 · vyřešeno 17. 9. 2026

17. 9. 2026 v 16:21:22 se po volbě mise `track` ze stránky runtime zasekl uvnitř `Start(Run)`. `HangWatchdog` vystřelil a pořídil minidump (`logs/hang-Start-Run-20260917-162142.dmp`) — a z jeho zásobníků je příčina určená jednoznačně. Je to klasická inverze pořadí zámků (ABBA): vlákno volby mise drží zámek `TrackMission` (`StartMission` → `lock (gate)` → `EnterPhase` → `EmitState` → `EmitDerived` → `RelaySource.Post` → `WebStatus.Post`) a čeká na zámek `WebStatus`; vlákno webového serveru drží zámek `WebStatus` (`Handle` → `ToJson` → `lock (gate)` → `AppendHead`) a čeká na zámek `TrackMission` (`get_PhaseText`). Spouštěč je proto úplně běžný provoz: stránka se sama obnovuje, takže stačí, aby dotaz na stav přišel ve chvíli, kdy se mise zakládá — a v terénu se na stránku kouká právě v ten okamžik, protože se z ní mise vybírá. ✅ **Že je cyklus UZAVŘENÝ (tedy skutečný deadlock, ne jen dlouhé čekání), je ověřené v kódu:** `WebStatus.ToJson` bere `lock (gate)` a **uvnitř něj** volá `AppendHead`, tedy i `TrackMission.PhaseText`. Sám se nerozpustí — jediné, co ho ukončí, je konec procesu. Sedí to i s tím, co obsluha udělala: stránka zmlkla a robota vypnula a zapnula. Kořen je porušení pravidla, které si `RelaySource` sám píše: fan-out běží na vlákně producenta, takže se do něj nesmí vstupovat s drženým zámkem. Léčba je publikovat AŽ MIMO zámek — pod zámkem zprávu jen složit. Totéž platí pro každou další misi a stupeň, který volá `EmitDerived` zevnitř `lock`, a pro `WebStatus.ToJson`, který si má stav misí napřed ofotit a teprve pak skládat odpověď. **Opraveno 17. 9. 2026, z obou stran** (jedna by stačila, ale invariant má platit oběma směry): mise skládá zprávu pod zámkem a **publikuje až mimo něj** — drží to `using` rozsah (`Publikace()`), takže se na to nedá zapomenout ani při `return` uprostřed zámku, a vnořená volání (`Abort` zevnitř `OnGlobalNav`) mlčí podle `Monitor.IsEntered`, aby publikoval vždy jen nejvnějšnější rozsah. `WebStatus.ToJson` si stav mise **ofotí před** svým zámkem (`NactiMisi`), takže se drží vždy jen jeden zámek. ⚠️ **Prohlédnuty byly všechny mise a týká se to jen dvou:** `TrackMission` a `RobotourMission`. `FreeRunMission` i `MagCalMission` nemají zámek **žádný** (a `MagCalCollector` taky ne), takže tam ten tvar vzniknout nemůže — dřívější tvrzení, že mají tentýž tvar, bylo mylné. Cesta `WebStatus` → `AppendMagCal` → `MagCalWriteBlockedReason` čte jen uloženou zprávu, ne misi. Hlídají to tři testy (`MisePublikujeMimoZamekTests`, jeden i v rigu Robotouru), které **prokazatelně chytají vrácenou vadu** — po dočasném vrácení publikace pod zámek spadly. ⚠️ **Na zařízení to neběželo.**

- [x] Příčina určená z minidumpu (oba zásobníky, uzavřený cyklus zámků) (17. 9. 2026)
- [x] Publikovat mimo zámek (`Publikace()` + `Vypust()`) v `TrackMission` i `RobotourMission` (17. 9. 2026)
- [x] `WebStatus.ToJson` si stav mise ofotí předem (`NactiMisi`), ne pod vlastním zámkem (17. 9. 2026)
- [x] Testy pořadí zámků (ověřeno vrácením vady — spadnou) (17. 9. 2026)
- [ ] Ověřit na zařízení (volba mise ze stránky při otevřeném náhledu)

[headless.md](headless.md), [TrackMission.cs](../Src/ARBot.Common/Missions/TrackMission.cs), [WebStatus.cs](../Src/ARBot.Runtime/Web/WebStatus.cs), [RelaySource.cs](../Src/ARBot.Common/Communication/RelaySource.cs) · DevLog [2026-09-17](devlog.md#2026-09-17)

<a id="prov-sit-soutezni-provoz"></a>
### ✅ Síť robota pro soutěž — vlastní WiFi AP a kabel bez routeru

`prov-sit-soutezni-provoz` · záměr · **hotovo** · nalezeno 29. 8. 2026 · vyřešeno 30. 8. 2026

Na soutěži není router, ale k robotu je potřeba se dostat z notebooku i z mobilu a stahovat velké záznamy. Robot vysílá vlastní AP `arbot` (na `hostapd`, protože AP režim `wpa_supplicant` s tímhle Broadcom čipem klienta nepřipojil) a po kabelu vezme adresu ze sítě, nebo když DHCP do 12 s nepřijde, sám rozdá adresu notebooku (`eth-direct`). Ověřeno rebootem: AP je k dispozici 9,8 s po startu. Cena: v síti s pomalým DHCP může robot spadnout na přímé spojení. Druhý den se opravilo, že sdílené připojení bralo notebooku internet (posílalo výchozí trasu přes robota); účinek se ověří až při dalším přepojení kabelu. Postup je v `setup-orangepi.sh`, ověřený jen staticky — celý běh ověří až reinstalace.

- [x] AP `arbot` na `hostapd`, ethernet `eth-dhcp` → `eth-direct`, ověřeno rebootem (29. 8. 2026)
- [x] Pád na přímé spojení zkrácen ze 74 s na 12 s (`ipv6.method ignore`) (29. 8. 2026)
- [x] `setup-orangepi.sh` přepsán podle nové konfigurace (ověřeno staticky) (29. 8. 2026)
- [x] Sdílené připojení neposílá notebooku výchozí trasu ani DNS (`no-default-route.conf`) (30. 8. 2026)

[OrangePi5Ultra/POSTUP.md (kroky 3 a 4)](../OrangePi5Ultra/POSTUP.md), [setup-orangepi.sh](../OrangePi5Ultra/setup-orangepi.sh), [rozhodnutí 29. 8. 2026](decisions.md) · DevLog [2026-08-29](devlog.md#2026-08-29), [2026-08-30](devlog.md#2026-08-30)

<a id="prov-timebase-100x"></a>
### ✅ Na Orange Pi šel čas aplikace 100× rychleji

`prov-timebase-100x` · vada · **hotovo** · nalezeno 31. 8. 2026 · vyřešeno 1. 9. 2026

První běh aplikace na Pi: kamera hlásila 0,3 Hz, ale snímky přibývaly desítkami za sekundu, a čas snímku v overlayi byl po pěti minutách o osm hodin napřed. `TimeBase.Now` sčítalo surové tiky `Stopwatch` (na Linux/ARM64 1 GHz) s tiky `DateTime` (100 ns); na Windows je frekvence shodou okolností 10 MHz, takže se to nikdy neprojevilo. Z `TimeBase` se razítkuje na 45 místech, takže `dt` mezi měřeními bylo 100× větší — záznamy z Pi před opravou nejsou použitelné. Motor kvůli tomu „blikal napětím" (rozpočet 500 ms byl reálně 5 ms). Opraveno, změřeno na zařízení izolovaně a 1. 9. potvrzeno v běžící aplikaci. Následně se čas v celé aplikaci sjednotil na `TimeBase` (pravidlo v CLAUDE.md).

- [x] `TimeBase` sčítá `Elapsed.Ticks`; táž záměna v `Performance.ToString()`; testy (31. 8. 2026)
- [x] Ověřeno v běžící aplikaci na Pi (1. 9. 2026)

[rozhodnutí 31. 8. 2026](decisions.md), [record-replay.md](record-replay.md) · DevLog [2026-08-31](devlog.md#2026-08-31), [2026-09-01](devlog.md#2026-09-01), [2026-09-04](devlog.md#2026-09-04), [2026-09-15](devlog.md#2026-09-15)

<a id="prov-profil-freerun-pi"></a>
### ✅ Profil pro FreeRun na Pi — a profily na zařízení vůbec nefungovaly

`prov-profil-freerun-pi` · vada · **hotovo** · nalezeno 1. 9. 2026 · vyřešeno 2. 9. 2026

Při psaní prvního provozního profilu `config/pi-freerun.cfg` se ukázalo, že na zařízení profil nešlo vůbec načíst: v adresáři aplikace na Pi není `config/` ani `OSM/`, takže `config=` ukazovalo na neexistující soubor a start skončil chybou — dokumentace přitom tvrdila opak. Léčba: build kopíruje profily i mapy k binárkám a strážný test hlídá, že každý profil v repu projde registrem. Přibyly tři parametry na přání autora: `record=` (záznam bez klikání v UI), `autorun=` (Run po startu) a `maxspeed=` (strop rychlosti); při tom se zjistilo, že `FreeRunConfig.MaxSpeedMps` byl mrtvé pole, které nikdo nečetl. Profil na Pi zabral hned 2. 9. (první FreeRun na železe); audit 15. 9. pak v profilu vypnul `autorun`, protože robot, který se po startu rozjede sám, projekt zakazuje.

- [x] Kopírovat `config/*.cfg` a `OSM/*.osm` do výstupu buildu, test `ProfilyVRepuTests` (1. 9. 2026)
- [x] Parametry `record=`, `autorun=`, `maxspeed=`; odstraněno mrtvé `MaxSpeedMps` (1. 9. 2026)
- [x] Profil načten na Pi a FreeRun s ním jel (2. 9. 2026)
- [x] Audit: `autorun=false` v profilu, `ProfilyBezpecnostTests` (15. 9. 2026)

[configuration.md](configuration.md), [mission-freerun.md](mission-freerun.md), [config/pi-freerun.cfg](../config/pi-freerun.cfg) · DevLog [2026-09-01](devlog.md#2026-09-01), [2026-09-02](devlog.md#2026-09-02), [2026-09-15](devlog.md#2026-09-15)

<a id="prov-diagnostika-debug-trace"></a>
### ✅ Diagnostika senzorů šla do Debug, takže v Release na zařízení nezůstala žádná stopa

`prov-diagnostika-debug-trace` · vada · **hotovo** · nalezeno 2. 9. 2026 · vyřešeno 2. 9. 2026

Na snímku panelu Debug output nebyl o nefunkčních kamerách ani řádek — drivery hlásily přes `Debug.WriteLine`, které se v Release buildu vykompiluje pryč, a právě Release běží na robotu. Příčina výpadku kamer se tak hodinu hledala měřením zvenčí místo pár sekund čtení logu. Převedeno 31 hlášení v obou HAL a v obecné chybové cestě všech senzorů, ověřeno přímo na Pi z Release knihoven; pravidlo je v CLAUDE.md a hlídá ho strážný test. Audit 15. 9. našel totéž mimo drivery (výjimky stupňů, `LocalNavigator`, UART) a přidal škrcení hlášek na horké cestě.

- [x] Drivery kamer a `SensorBase` do `Trace`, test `DiagnostikaSenzoruTests` (2. 9. 2026)
- [x] Ověřeno na Pi z Release buildu (2. 9. 2026)
- [x] Audit: stupně, navigace a UART do `Trace`, škrtič `PoruchaHlasic`, `DiagnostikaPoruchTests` (15. 9. 2026)

[CLAUDE.md](../CLAUDE.md), [hardware.md](hardware.md) · DevLog [2026-09-02](devlog.md#2026-09-02), [2026-09-15](devlog.md#2026-09-15)

<a id="prov-fake-hwclock"></a>
### ✅ Robot běžel po bootu 20 hodin pozadu kvůli fake-hwclock

`prov-fake-hwclock` · vada · **hotovo** · nalezeno 3. 9. 2026 · vyřešeno 3. 9. 2026

Systémový čas na Pi ukazoval včerejší večer, ačkoli hardwarové RTC sedělo na sekundy. Jádro čas z RTC nastavilo správně a hned poté ho přepsal `fake-hwclock-load` posledním uloženým časem z doby před vybitím baterie — balík pro stroje bez RTC, který na této desce nemá co dělat, a robot k NTP běžně cestu nemá. Služba zamaskovaná, ověřeno rebootem, krok přidán do instalačního skriptu. Záznamy z doby před opravou mají včerejší jméno i razítka.

- [x] Zamaskovat fake-hwclock, čas z RTC, krok v instalačním skriptu (3. 9. 2026)

[OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md), [setup-orangepi.sh](../OrangePi5Ultra/setup-orangepi.sh) · DevLog [2026-09-03](devlog.md#2026-09-03)

<a id="prov-rustdesk-zamek-obrazovky"></a>
### ✅ Vzdálená plocha na Pi „vytuhla" — zamykal ji spořič KDE, ne RustDesk

`prov-rustdesk-zamek-obrazovky` · vada · **hotovo** · nalezeno 3. 9. 2026 · vyřešeno 3. 9. 2026

RustDesk třikrát skončil černou obrazovkou bez možnosti přihlášení. Sezení na Pi běží headless na Waylandu a KDE má výchozí zámek po 5 minutách nečinnosti — v obou bootech se zamykací obrazovka objevila přesně 5 minut po přihlášení a RustDesk ji na Waylandu neumí zobrazit ani ovládat. Na pokyn autora zámek vypnut, krok v instalačním skriptu. Že tím freezy zmizely, se do DevLogu nezapsalo; další zmínka o problému není.

- [x] Vypnout automatický zámek obrazovky KDE (3. 9. 2026)

[OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md) · DevLog [2026-09-03](devlog.md#2026-09-03)

<a id="prov-zachyt-padu-pi"></a>
### ✅ Pády aplikace na Pi nešly dohledat — teď po nich zůstane stopa

`prov-zachyt-padu-pi` · záměr · **hotovo** · nalezeno 3. 9. 2026 · vyřešeno 3. 9. 2026

Aplikace na robotu „chvíli běžela a zmizela" a nikde nic: journal v RAM zmizel s vybitou baterií, výjimka .NET šla na stderr terminálu, core dumpy nevznikaly. Na robotu je teď trvalý journal, hlášení fatálních signálů jádrem a spouštěcí skript s logem a minidumpem; v aplikaci `CrashLog`, který neošetřenou výjimku zapíše do `logs/crash-*.log` a pokusí se dopsat záznam. Vyplatilo se týž večer (pád rozebraný do řádku kódu) a 5. 9. crash log na Pi skutečně vznikl. Nativní pád (SIGSEGV) `CrashLog` nezachytí — zůstává journal a core dump. Sourozenec pro zatuhnutí, `HangWatchdog`, přibyl 14. 9.

- [x] Trvalý journal, `print-fatal-signals`, skript `run-arbot.sh` (3. 9. 2026)
- [x] `CrashLog` v aplikaci (3. 9. 2026)
- [x] Crash log vznikl na zařízení (5. 9. 2026)

[OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md), [headless.md](headless.md) · DevLog [2026-09-03](devlog.md#2026-09-03), [2026-09-05](devlog.md#2026-09-05), [2026-09-14](devlog.md#2026-09-14)

<a id="prov-headless-runtime-sluzba"></a>
### ✅ Runtime bez UI a provoz na robotu jako služba

`prov-headless-runtime-sluzba` · záměr · **hotovo** · nalezeno 4. 9. 2026 · vyřešeno 5. 9. 2026

Řídicí runtime se vyčlenil do vlastního projektu `ARBot.Runtime` a konzolový `ARBot.Headless` ho spouští bez Avalonie (Ctrl+C / SIGTERM → `Stop()`, návratové kódy). Druhý den z toho vznikl provoz jako služba: systemd jednotka `arbot`, dvoufázový běh (bez zadané mise robot nastartuje senzory a stojí, dokud mu člověk nevybere misi), `dataroot=` a zámek jedné instance kvůli nasazení stínovou kopií, verze binárky v crash logu i v záznamu. Ověřeno na Orange Pi: služba, SIGTERM za 7 ms, zámek, CPU 6,2 % při čekání; při ověřování se našlo sedm vad, mimo jiné že se konfigurace do záznamu nikdy nedostávala. Celý průchod misí ze stránky proběhl 14. 9. Start po skutečném rebootu se dosud nezkoušel; limit pěti restartů, který službu v crash loopu trvale odstavil, se opravil 15. 9.

- [x] Projekt `ARBot.Runtime` a konzolový `ARBot.Headless` (fáze 1–2) (4. 9. 2026)
- [x] Služba systemd, dvoufázový běh, `dataroot=`, zámek instance, verze binárky (fáze 4) (5. 9. 2026)
- [x] Ověřeno na Orange Pi (služba, SIGTERM, zámek, CPU) (5. 9. 2026)
- [x] Mise vybraná ze stránky odjela na zařízení (14. 9. 2026)

[headless.md](headless.md), [plan-headless-provoz.md](plan-headless-provoz.md), [plan-runtime-headless.md](plan-runtime-headless.md), [deploy/README.md](../deploy/README.md), [rozhodnutí 4. a 5. 9. 2026](decisions.md) · DevLog [2026-09-04](devlog.md#2026-09-04), [2026-09-05](devlog.md#2026-09-05), [2026-09-14](devlog.md#2026-09-14), [2026-09-15](devlog.md#2026-09-15)

<a id="prov-webovy-nahled"></a>
### ✅ Webový náhled robota z mobilu — půdorys, kamera, senzory, stop

`prov-webovy-nahled` · záměr · **hotovo** · nalezeno 4. 9. 2026 · vyřešeno 5. 9. 2026

Stránka nad vlastním HTTP serverem (`web=<port>`): půdorys s occupancy gridem a sítí cest, snímek kamery včetně vrstvy „cesta z RGB", stav senzorů se stářím poslední zprávy a tlačítko Zastavit. Kreslí se líně — bez publika se nic nekreslí ani nekopíruje (13,9 % CPU bez publika proti 14,3 % s ní). Cestou se opravila zamrzlá stránka (pool kopií snímků měl kapacitu 2 pro dvě kamery) a geometrie sítě cest (rozšiřující se cesta je trychtýř). 5. 9. přibyl výběr mise pod drženým nouzovým zastavením a ověření na Pi včetně textu měřítka a živého snímku z D435. Stránka od té doby rostla dál (GPS kvalita, Power off, zóny míst, řádek zastavení).

- [x] Kreslení v `Common/Rendering`, HTTP v `Runtime/Web`, stránka a JSON (4. 9. 2026)
- [x] Pool kopií snímků 2 → 4, hláška při vyčerpání (4. 9. 2026)
- [x] Výběr mise z webu, jednořádková hlavička, tlačítka v liště (5. 9. 2026)
- [x] Ověřeno na Orange Pi (fonty, živý snímek) (5. 9. 2026)

[headless.md](headless.md), [plan-headless-web.md](plan-headless-web.md), [rozhodnutí 4. 9. 2026](decisions.md) · DevLog [2026-09-04](devlog.md#2026-09-04), [2026-09-05](devlog.md#2026-09-05)

<a id="prov-nahled-stara-zalozka"></a>
### ✅ Otevřená záložka náhledu běží po nasazení dál se starou stránkou

`prov-nahled-stara-zalozka` · vada · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 6. 9. 2026

Z hlášení „chybí tlačítko Power off": náhled je jednostránková aplikace, po nasazení tedy v otevřené záložce běží starý skript, zatímco hlavička ukazuje novou verzi (čte se ze stavu). Chybějící funkce pak vypadá jako nenasazená a hlavička u toho lže. Léčba: do stránky se otiskne verze binárky, která ji poslala; skript ji porovnává s verzí ze stavu a při neshodě se jednou sám přenačte (pojistka proti zacyklení). Přibyly dva strukturální testy, že každé `getElementById` má své `id` a každý `onclick` svou funkci.

- [x] Verze stránky ve skriptu, varování a jednorázové přenačtení (6. 9. 2026)
- [x] Strukturální testy nad vygenerovanou stránkou (6. 9. 2026)

[headless.md](headless.md) · DevLog [2026-09-06](devlog.md#2026-09-06)

<a id="prov-nasazeni-dat-config-osm-modely"></a>
### ✅ Nasazení posílá i profily, mapy a modely

`prov-nasazeni-dat-config-osm-modely` · záměr · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 7. 9. 2026

`nasad.ps1` do té doby nasazoval jen binárky; profily a mapy se kopírovaly ručně a poznalo se to až tím, že se změna v profilu na robotu neprojevila. Teď jdou `config/`, `OSM/` a modely (`*.onnx`, `*.rknn`) do datového adresáře, odkud je aplikace čte, a `librknnrt.so` vedle binárek. Repo vyhrává, ale skript před kopií porovná MD5 a vypíše soubory, které se liší. Ověřeno celým řetězem na Pi včetně stínové kopie.

- [x] `config/` a `OSM/` do datového adresáře s porovnáním MD5 (6. 9. 2026)
- [x] Modely a `librknnrt.so`, ověření na robotu (7. 9. 2026)

[deploy/README.md](../deploy/README.md) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-07](devlog.md#2026-09-07)

<a id="prov-poweroff-ze-stranky"></a>
### ✅ Vypnutí desky ze stránky náhledu

`prov-poweroff-ze-stranky` · záměr · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 13. 9. 2026

Aby šlo robotovi bezpečně odpojit napájení bez notebooku, stránka náhledu nabízí Power off (`POST /poweroff`): zastaví runtime a vypne desku příkazem z `poweroffcmd=`. Na Windows je příkaz prázdný a tlačítko se záměrně neukazuje (rozhodnutí autora). Odpověď se posílá až po pokusu, aby se selhání dozvěděla obsluha. Poprvé použito na robotu 13. 9. („robot na noc vypnut").

- [x] `POST /poweroff`, `poweroffcmd=`, tlačítko na stránce (6. 9. 2026)
- [x] Použito na skutečné desce (13. 9. 2026)

[headless.md](headless.md) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-13](devlog.md#2026-09-13)

<a id="prov-zotaveni-kamer-supervizor"></a>
### ✅ Zaseknuté kamery D435 si runtime zotaví sám za ~29 s

`prov-zotaveni-kamer-supervizor` · záměr · **hotovo** · nalezeno 12. 9. 2026 · vyřešeno 13. 9. 2026

Zamrzlý stream D435 dřív znamenal mrtvou kameru na stovky sekund až do restartu služby. `CameraRecoverySupervisor` po 15 neúspěšných pokusech o připojení vezme držené zastavení, počká na stání, nechá kamery zaparkovat jejich vlastní smyčku (žádost a potvrzení — první verze bourala pipeline z cizího vlákna a zatuhla) a vymění sdílený RealSense kontext. Na robotu ověřeno 5× z 5 na skutečné poruše, obnova 28–29 s. Odstup po neúspěchu se zdvojnásobuje a po třech marných zotaveních za 15 min se to u dané kamery vzdá, aby jedna vadná kamera nestahovala dokola i zdravé. Je to obvaz, ne léčba příčiny: proč teardown vede k zaseknutí jen asi ve 4 % případů, se neví, a za jízdy by to znamenalo zastavení na ~28 s každých pár minut.

- [x] Příčina změřena na živé poruše: reset USB ani odpojení `uvcvideo` nepomohly, jiný proces kameru zabral — zaseknutý je stav librealsense v našem procesu, ne zařízení (12. 9. 2026)
- [x] Supervizor + tlačítko „Zotavit kamery“ na stránce náhledu (13. 9. 2026)
- [x] Přestavba na žádost a potvrzení (`IRecoverableCamera`) po zatuhnutí první verze na robotu (13. 9. 2026)
- [x] Ověření na skutečné poruše (5× z 5, 28–29 s) (13. 9. 2026)
- [x] Druhá podoba záseku (kamera zmlkne bez selhaných dotazů) — počítá se každý neúspěšný pokus (13. 9. 2026)
- [x] Rostoucí odstup a vzdání po třech marných zotaveních (tři pokusy o správné kritérium) (13. 9. 2026)

[plan-drive-hold.md](plan-drive-hold.md), [hardware.md](hardware.md), [headless.md](headless.md) · DevLog [2026-09-12](devlog.md#2026-09-12), [2026-09-13](devlog.md#2026-09-13), [2026-09-14](devlog.md#2026-09-14)

<a id="prov-zatuhnuti-start-hangwatchdog"></a>
### ✅ Runtime zatuhl při volbě mise; z toho hlídač zatuhnutí

`prov-zatuhnuti-start-hangwatchdog` · vada · **hotovo** · nalezeno 14. 9. 2026 · vyřešeno 14. 9. 2026

Při volbě mise ze stránky se runtime na zařízení zasekl uvnitř `Start(Mode.Run)` — v journalu doběhlo `corridor=false`, `mission=track` už nikdy (běžně 5 ms mezi nimi), proces žil dál, stránka umlkla a autor robota vypnul; tím zmizel i stav, ze kterého by to šlo přečíst. Léčba není oprava (mechanismus záseku je neprokázaný), ale důkaz, který si robot pořídí sám: `HangWatchdog` (`hangwatch=`, výchozí 20 s) obaluje start, po vypršení jde do `Trace` hlášení a vedle něj minidump se zásobníky všech vláken. Reprodukce od stolu (32 kol volba mise → stop → restart) zásek nevyvolala; rozdíl je, že tehdy robot jel. Kandidátem na příčinu je od 15. 9. `Stop()` zatuhlý na UARTu bez dat (nález V5 auditu). 17. 9. 2026 hlídač na zařízení POPRVÉ běžel — a v 16:21:42 **vystřelil a odvedl přesně to, kvůli čemu vznikl**: zásek při volbě mise `track`, minidump `logs/hang-Start-Run-20260917-162142.dmp`, a z jeho zásobníků je příčina určená na řádek — je to **ABBA deadlock mezi zámkem `TrackMission` a zámkem `WebStatus`**, viz `prov-deadlock-mise-webstatus`. Tím je tohle téma vyřešené: hlídač je hotový, nasazený a prokázaný v provozu, a vada, kterou chytil, se vede samostatně. ⚠️ **Nechytí ale zásek ZA během** — token se po dokončení `Start(Run)` zahodí, takže zásek z téhož dne v 16:12 (4 s po startu) žádný dump nemá; viz `prov-zatuhnuti-za-behu-mise`.

- [x] Rozbor journalu a záznamů (dva omyly opravené měřením) (14. 9. 2026)
- [x] `HangWatchdog` s minidumpem, arm před zámkem, jen pro `Run` (14. 9. 2026)
- [x] Reprodukce od stolu — negativní (14. 9. 2026)
- [x] Nasadit hlídač na zařízení a nechat zásek chytit v provozu (17. 9. 2026)

[headless.md](headless.md) · DevLog [2026-09-14](devlog.md#2026-09-14), [2026-09-15](devlog.md#2026-09-15)

## Hardware a senzory

<a id="hw-d435-vypadky-za-provozu"></a>
### ⬜ Kamera D435 se za provozu odmlčí

`hw-d435-vypadky-za-provozu` · vada · **otevřeno** · nalezeno 31. 8. 2026

Při prvním běhu aplikace na Pi se pravá D435 po ~4600 s odmlčela; odpojená nebyla, v jádře se opakovalo `USBDEVFS_CLEAR_HALT`. Rešerše 11. 9. ukázala, že je to cizí, Intelem nevyřešený problém, a naše léčba je to, k čemu dojdou všichni: hlídka zamrzlého streamu, zbourání pipeline a nové připojení, od 13. 9. supervizor zotavení s drženým zastavením — ověřený na robotu na skutečné poruše (obě kamery zpět za 29 s, dřív mrtvá kamera desítky minut). Je to obvaz, ne léčba: porucha přijde asi jednou za 17 minut. Hypotéza „může za to T265" padla 14. 9. měřením a `CLEAR_HALT` se ukázal jako šum, ne podpis poruchy; podezřelá je fyzická větev USB `2-1.3` (samostatné téma).

- [x] 'Rešerše: cizí nevyřešený problém, propustnost USB ani sousedé na hubu to nejsou' (11. 9. 2026)
- [x] Supervizor zotavení kamer s drženým zastavením, ověřen na skutečné poruše (13. 9. 2026)
- [x] Vyvrátit hypotézu T265 / `CLEAR_HALT` měřením na zařízení (14. 9. 2026)
- [ ] Odstranit příčinu (kabel nebo port větve 2-1.3)

čeká na [hw-vetev-usb-2-1-3](#hw-vetev-usb-2-1-3) · [hardware.md](hardware.md), [plan-drive-hold.md](plan-drive-hold.md), [rozhodnutí 11. 9. 2026](decisions.md) · DevLog [2026-08-31](devlog.md#2026-08-31), [2026-09-06](devlog.md#2026-09-06), [2026-09-11](devlog.md#2026-09-11), [2026-09-13](devlog.md#2026-09-13), [2026-09-14](devlog.md#2026-09-14)

<a id="hw-kamery-usb2-po-bootu"></a>
### ⬜ Kamery se po bootu vyčetly jen na USB 2.0 a robot byl bez vidění

`hw-kamery-usb2-po-bootu` · vada · **otevřeno** · nalezeno 2. 9. 2026

Obě D435 se po bootu hlásily jen rychlostí USB 2.0, na které se hloubka a barva nevejdou ani pro jednu kameru — robot tedy neviděl nic a kernel si nestěžoval. Odpojení a zapojení linku vrátilo na 5 Gbps. Domněnka o nedovřeném konektoru padla (autor s kabely nehýbal); jde o SuperSpeed linku, která se při bootu nenatrénuje. Série restartů na nabíječce dala 6× dobře, jediná stopa je napájení (selhání přišlo na dosluhující baterii). Od 11. 9. driver typ linky hlásí a na USB 2.0 varuje i s léčbou; měření na baterii se neudělalo.

- [x] Fyzické přepojení kamer, ověřeno 30/30 fps (2. 9. 2026)
- [x] Studený start a pět teplých restartů na nabíječce (6× dobře) (2. 9. 2026)
- [x] Driver hlásí typ USB linky (`UsbLinkCheck`), varování na USB 2.0 (11. 9. 2026)
- [ ] Zopakovat sérii startů na baterii bez nabíječky

[hardware.md](hardware.md), [OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md) · DevLog [2026-09-02](devlog.md#2026-09-02), [2026-09-11](devlog.md#2026-09-11)

<a id="hw-d435-query-pamet"></a>
### ⬜ Když se kamera nedá znovu vyčíst, proces roste v paměti

`hw-d435-query-pamet` · vada · **otevřeno** · nalezeno 6. 9. 2026

Po nasazení levá kamera nenaběhla a `QueryDevices` hlásil „failed to set power state" ~0,7× za sekundu; běh s 93 selháními vyšplhal na 1,8 GB proti 130–140 MB, tedy ~25 MB na jeden neúspěšný dotaz. Managed strana je v pořádku (vše v `using`), roste to na nativní straně, takže léčba není dozavírat, ale přestat se ptát každou sekundu. Restart služby kameru vrátil. 14. 9. runtime hledal odpojenou T265 ~1× za sekundu (7 829 řádků za 168 minut) — dotaz přes sdílený zámek jde dál.

- [ ] Backoff dotazů `QueryDevices` po selhání
- [ ] Změřit, jestli růst paměti končí pádem

[hardware.md](hardware.md) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-14](devlog.md#2026-09-14)

<a id="hw-vn100-akcelerometr-7pct"></a>
### ⬜ Akcelerometr VN100 měří o 7 % víc než g

`hw-vn100-akcelerometr-7pct` · vada · **otevřeno** · nalezeno 6. 9. 2026

Vedlejší nález z rozboru kurzu: medián `|a|` je 10,51 m/s² proti 9,81, tedy +7 %, a sklon pole vychází 61–63° proti tabulkovým ~66°. Později se dohledal bias +0,27 m/s² v ose Z, registr 25 (kompenzace akcelerometru) je jednotkový. Při kalibraci magnetometru to zvedá rozptyl sklonu (proto sklon vypadl z brány verdiktu) a svislici natočí o ~1° při náklonu. Kalibrace akcelerometru je otevřený samostatný úkol. 12. 9. to nezávisle potvrdil živý senzor (registr 27 dává +7,3 %, registr 25 je jednotkový); z náklonů do 40° se ale elipsoida akcelerometru neurčí. 18. 9. 2026 beze změny: `|a|` v klidu 10,48 m/s² (+6,9 %), sklon pole 62,8–63,0° proti 65,95° z registru 21 — kalibrace magnetometru na to nesáhla, jak se čekalo.

- [x] Změřit bias ze záznamu a přečíst registr 25 (12. 9. 2026)
- [ ] Kalibrace akcelerometru (registr 25)

[imu-and-frames.md](imu-and-frames.md), [plan-vn100-kalibrace.md](plan-vn100-kalibrace.md) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-10](devlog.md#2026-09-10), [2026-09-12](devlog.md#2026-09-12), [2026-09-18](devlog.md#2026-09-18)

<a id="hw-kurz-zbytek-konstanta"></a>
### ⬜ Po kalibraci zbývá konstantní posun kurzu −3,7°, který nejde rozložit

`hw-kurz-zbytek-konstanta` · vada · **otevřeno** · nalezeno 12. 9. 2026

Po ověření kalibrace zůstal kurz VN100 proti GPS o −3,7° vedle. Není to železo (půlrozdíl mezi opačnými směry jen ∓1° a mezi běhy mění znaménko) ani deklinace (ta by rozpor zvětšila na −10°), a tímhle měřením se nedá rozložit na pootočení senzoru, zbytek kalibrace a šikmé jetí — na to je potřeba průjezd téhož úseku s robotem otočeným o 180°. Z živého senzoru se přitom zjistilo, že vestavěný model pole je zastaralý o ~1,9° (epocha ~2015), takže zbytek je ještě o tolik větší, a že registr 83 je ve flash, ačkoli se zapisuje bez uložení. Od 14. 9. je kurz rozbitý železem od kabelů, takže měření má smysl opakovat až po nové kalibraci. 18. 9. 2026 první jízda po nové kalibraci: rozpor `IMU − GPS kurz` je zase na obou protilehlých kurzech stejný (−3,6 / −2,9° a −1,65 / −1,22°), tedy konstanta — ale **mezi dvěma běhy 13 minut po sobě jiná** (−3,4 proti −1,1° středně). Sedí to s dotahováním VPE po každém startu procesu (registr 83, ~100–170 s); průjezd s robotem otočeným o 180° to pořád nerozloží, ale ukazuje to, že hledaná „konstanta" je spíš pomalá proměnná.

- [x] Zbytek změřen a rozložen podle směru a místa (12. 9. 2026)
- [x] Registry přečteny na živém senzoru (deklinace se aplikuje, model zastaralý) (12. 9. 2026)
- [ ] Průjezd téhož úseku s robotem otočeným o 180°

čeká na [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [imu-and-frames.md](imu-and-frames.md) · DevLog [2026-09-12](devlog.md#2026-09-12), [2026-09-18](devlog.md#2026-09-18)

<a id="hw-t265-odpojena-natrvalo"></a>
### ⬜ Odpojená T265: runtime ji dál hledá a zahlcuje journal

`hw-t265-odpojena-natrvalo` · vada · **otevřeno** · nalezeno 13. 9. 2026

T265 se 13. 9. rozbila tak, že se připojí, ale nedává pózu; softwarový reset selže a nepomůže ani restart služby, jen fyzické přepojení — a po něm se za 1,5 h zasekla znovu. Každé její marné zotavení přitom stahovalo kontextem i obě zdravé D435 a zastavovalo robota. Autor rozhodl kameru odpojit natrvalo (od 13. 9. večer na sběrnici není). Runtime ji ale dál hledá ~1×/s přes sdílený zámek RealSense a na každý pokus píše chybu do journalu (7 829 řádků za 168 min) — neškodí, ale zahlcuje; nezakládat ji, když není na sběrnici, zbývá.

- [x] T265 zapojena do zotavení kamer, per-kamera vzdání po třech marných pokusech (13. 9. 2026)
- [ ] Nezakládat T265 v runtime, když není na sběrnici (hledání 1×/s přes sdílený zámek)

[rozhodnutí 13. 9. 2026](decisions.md), [hardware.md](hardware.md) · DevLog [2026-09-13](devlog.md#2026-09-13), [2026-09-14](devlog.md#2026-09-14)

<a id="hw-vetev-usb-2-1-3"></a>
### ⬜ Tvrdé záseky kamer na větvi USB `2-1.3`

`hw-vetev-usb-2-1-3` · vada · **otevřeno** · nalezeno 13. 9. 2026

Dvanáct z dvanácti tvrdých záseků kamery RealSense přišlo na témž fyzickém USB portu, ať na něm visela kterákoli kamera (13. 9. se schválně prohodily; přiřazení portu ke kameře je změřené přes `CLEAR_HALT` během výpadku). Hypotéza „může za to T265“ padla měřením 14. 9.: bez ní se `CLEAR_HALT` (7,4 → 7,1–7,9/min) ani zamrzání streamu (3,4 → 3,6–4,2/h) nezměnily — a `CLEAR_HALT` teče 3–15/min i ve zdravém stavu, takže jako měřidlo je mrtvý. Další krok je kabel nebo port, ne software: autor 14. 9. večer přesadil kameru na volný port téhož hubu (testuje větev, ne hub ani řadič); vyhodnocení v DevLogu zatím není — a nula záseků za hodinu nic neznamená, za 3 h čistého běhu už ano.

- [x] Prohodit kamery mezi porty a sbírat epizody (8 z 8 na `2-1.3`) (13. 9. 2026)
- [x] Běh bez T265 — hypotéza `CLEAR_HALT` vyvrácena, port potvrzen (12 z 12) (14. 9. 2026)
- [x] Přesadit kameru z `2-1.3` na volný port téhož hubu (14. 9. 2026)
- [ ] Vyhodnotit přesazení (aspoň 3 h čistého běhu služby)
- [ ] Podle výsledku kabel / jiná větev hubu / řadič

[hardware.md](hardware.md), [rozhodnutí 13. 9. 2026 (odpojení T265)](decisions.md) · DevLog [2026-09-13](devlog.md#2026-09-13), [2026-09-14](devlog.md#2026-09-14)

<a id="hw-gps-fixtime-rozbity"></a>
### ⬜ `GPSState.FixTime` je nesmysl — ovladač u-bloxu skládá ITOW špatně

`hw-gps-fixtime-rozbity` · vada · **otevřeno** · nalezeno 17. 9. 2026

`uBloxGps.Read` rozkládá ITOW (čas v GPS týdnu [ms]) na dny/hodiny/minuty/sekundy a **sekundy dělí špatně**: `s = ITOW/1000 - ((d*24+h)*60 + m*60)`, kde místo `*60` má u hodin být `*3600`. Výsledek je pak mimo — v záznamech ze 17. 9. 2026 vychází `FixTime` „**9 dní** 02:16:12", ačkoli v GPS týdnu jsou dny jen 0–6. Do UI to jde rovnou (`GpsDocument.FixTimeText`), takže panel GPS ukazuje nesmyslný čas fixu. Chyba je naštěstí **deterministická a invertovatelná**: platí `TotalMs = ITOW + 84 960 000·D + 3 540 000·H`, takže se z uložené hodnoty dá ITOW spočítat zpátky — a `ARBot.Analyze gps` (blok A0) to dělá, protože starší záznamy se přepsat nedají a jsou jediným absolutním časem, který nepochází z hodin Pi. ⚠️ **Oprava ovladače změní význam pole**, takže inverze v analyzátoru musí umět obojí (pozná to podle toho, že den v týdnu je 0–6).

- [x] Nález a invertovatelnost ověřená na záznamech (den v týdnu vyšel 4 = čtvrtek) (17. 9. 2026)
- [ ] Opravit rozklad v `uBloxGps.Read` (a nechat inverzi v analyzátoru pro starší záznamy)
- [ ] Test nad známým ITOW (dnes to nekryje nic)

[uBloxGps.cs](../Src/ARBot.HAL/Devices/GPS/uBlox/uBloxGps.cs), [GpsReport.cs](../Src/ARBot.Analyze/GpsReport.cs) · DevLog [2026-09-17](devlog.md#2026-09-17)

<a id="hw-neopixel-armbian"></a>
### 🧪 Driver NeoPixel (WS2812) přes SPI na Armbianu

`hw-neopixel-armbian` · záměr · **v kódu, na HW neověřeno** · nalezeno 7. 7. 2026 · vyřešeno 7. 7. 2026

`ArmbianSpiNeoPixelDriver` posílá sub-bity LED pásku jedním zápisem přes `/dev/spidev0.0`; `SpiDevice` vstupuje jako parametr, vlastníkem je volající. Buildy pro OrangePI zelené, ale na desce nikdy neběželo — časování `PulseConfig` proti SPI hodinám a reálný běh animační smyčky (`NeoPixelProcessor`: blinkry, KnightRider, Alert) zbývá ověřit.

- [x] Driver + balíček `System.Device.Gpio` (7. 7. 2026)
- [ ] Ověřit časování a běh na Orange Pi

[OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md) · DevLog [2026-06-23](devlog.md#2026-06-23), [2026-07-07](devlog.md#2026-07-07)

<a id="hw-pojistka-zrychleni-motoru"></a>
### 🧪 Nulové nebo záporné zrychlení motorů prošlo do řadiče

`hw-pojistka-zrychleni-motoru` · vada · **v kódu, na HW neověřeno** · nalezeno 18. 8. 2026 · vyřešeno 18. 8. 2026

Drivery posílaly hodnotu zrychlení bez kontroly — záporná prošla (rampa diverguje až na plnou rychlost opačným směrem), malá se zaokrouhlila na nulu a nula za jízdy znamená, že nouzové zastavení nemá čím zabrat. Pojistka ve skriptu jednotky by robota stejně nezastavila. Nově společný převod `MotorAcceleration.ToUnits` (velikost, minimum 1); skript `RizeniDiffPodvozku.mbs` byl dosynchronizován a 30. 8. nahrán do jednotky, chování na robotu (hlavně nouzové zastavení) zbývá ověřit.

- [x] `MotorAcceleration.ToUnits` v obou driverech, 5 testů (18. 8. 2026)
- [x] Skript nahrán do motorové jednotky (30. 8. 2026)
- [ ] Ověřit nouzové zastavení a rampu na robotu

[MotorAcceleration.cs](../Src/ARBot.HAL/Devices/MotorDriver/MotorAcceleration.cs), [hardware.md](hardware.md) · DevLog [2026-08-18](devlog.md#2026-08-18), [2026-08-30](devlog.md#2026-08-30)

<a id="hw-motor-chybovy-ramec"></a>
### 🧪 Chybový rámec motorového driveru se tvářil jako měření

`hw-motor-chybovy-ramec` · vada · **v kódu, na HW neověřeno** · nalezeno 27. 8. 2026 · vyřešeno 15. 9. 2026

Když se z řídicí jednotky motorů nepodaří přečíst telemetrii, driver vyrábí zástupný rámec s nulami a příznakem stopu. Fúze z něj brala „stojím", panel vypisoval 0 V (na Pi to vypadalo jako blikání napětí) a automat mise si obnovení linky s nestisknutým tlačítkem vyložil jako „jeď". Rozlišuje to příznak `HasMeasurement` (`MotorStateBase` verze 3): mapper z takového rámce měření nevyrobí, panel drží poslední naměřené hodnoty, a od 15. 9. na něj hledí i automaty misí a držené zastavení. Stop z rámce platí dál (fail-safe). Projeví se jen na skutečném železe, virtuální motory chybovou větev nemají.

- [x] `HasMeasurement` v driverech a mapperu (27. 8. 2026)
- [x] Panel motoru nevypisuje nuly z chybového rámce, řádek „Snímek" přizná chybějící měření (31. 8. 2026)
- [x] Automaty misí a `StopHold` berou fail-rámec jako „žádná zpráva" (15. 9. 2026)
- [ ] Ověřit na zařízení při skutečném výpadku telemetrie

[rozhodnutí 27. 8. 2026](decisions.md), [hardware.md](hardware.md) · DevLog [2026-08-27](devlog.md#2026-08-27), [2026-08-31](devlog.md#2026-08-31), [2026-09-15](devlog.md#2026-09-15)

<a id="hw-magcal-uncompmag-kompenzovany"></a>
### 🧪 „Surové" pole magnetometru je kompenzované, druhá kalibrace by tu první přepsala

`hw-magcal-uncompmag-kompenzovany` · vada · **v kódu, na HW neověřeno** · nalezeno 11. 9. 2026 · vyřešeno 12. 9. 2026

Registr 54, který ICD uvádí jako nekompenzovaná měření, se na našem senzoru mění podle registru 23 stejně jako kompenzovaný výstup; 12. 9. se ze záznamu potvrdilo, že binární `UncompMag` je bit po bitu shodný s kompenzovaným polem. Kalibrační mise ale sbírá právě tohle pole a výsledek zapisuje do registru 23 přímo, takže druhé spuštění by dobrou kalibraci přepsalo maticí blízkou jednotkové. Mise si teď registr 23 před sběrem vymaže (jen do RAM, výpadek napájení vrátí flash) a při ukončení bez zápisu ho vrátí; když ho nejde přečíst ani vymazat, nezačne. Skládání kalibrací se zamítlo, protože potřebuje rámcovou transformaci, která už jednou kousla. Na senzoru neběželo.

- [x] Změřit `Magnetometer` proti `MagnetometerRaw` ze záznamu (12. 9. 2026)
- [x] Mise registr 23 vymaže a po nedokončení vrátí (12. 9. 2026)
- [ ] Ověřit na senzoru, že se registr vymaže a vrátí

čeká na [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [rozhodnutí 12. 9. 2026](decisions.md), [imu-and-frames.md](imu-and-frames.md), [plan-vn100-kalibrace.md](plan-vn100-kalibrace.md) · DevLog [2026-09-11](devlog.md#2026-09-11), [2026-09-12](devlog.md#2026-09-12)

<a id="hw-orangepi-bringup"></a>
### ✅ Zprovoznění cílové desky Orange Pi 5 Ultra (Armbian, RealSense, USB, SPI, GPU, WiFi)

`hw-orangepi-bringup` · záměr · **hotovo** · nalezeno 23. 6. 2026 · vyřešeno 23. 6. 2026

Bring-up hardwarové platformy robota mimo aplikaci: librealsense 2.53.1 zkompilovaná ze zdrojů (poslední verze s D435 i T265, flagy pro GCC 15 / CMake 4), „mrtvý" USB3 port oživený overlayem `dwc3-host` (OTG řadič byl v peripheral režimu), SPI pro NeoPixel, GPU overlay `panthor-gpu` (softwarový rendering 198 % → 11 % CPU), WiFi přes `iwd` kvůli selhávajícímu WPA2 handshake s Rockchip driverem, Samba a RustDesk. Celé je to zachycené jako idempotentní skript `setup-orangepi.sh` pro případ reinstalace. Práce běžela ~17.–23. 6., v gitu je jen kotva 23. 6.

- [x] RealSense 2.53.1 ze zdrojů, D435 i T265 ověřené `rs-enumerate-devices` (23. 6. 2026)
- [x] USB3 OTG port do host režimu, SPI overlay, GPU overlay, WiFi na `iwd` (23. 6. 2026)
- [x] Idempotentní `setup-orangepi.sh` + `POSTUP.md` (23. 6. 2026)

[OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md), [setup-orangepi.sh](../OrangePi5Ultra/setup-orangepi.sh), [hardware.md](hardware.md) · DevLog [2026-06-23](devlog.md#2026-06-23)

<a id="hw-hal-vrstva-platforma-orangepi"></a>
### ✅ HAL vrstva a platforma `OrangePI` — jedna aplikace pro Windows i ARM64

`hw-hal-vrstva-platforma-orangepi` · záměr · **hotovo** · nalezeno 30. 6. 2026 · vyřešeno 24. 7. 2026

Hardwarový kód se oddělil do `ARBot.HAL` (sdílené) + `HALWindows` / `HALArmbian`, kamery a IMU se přepsaly z WPF `Media3D` na System.Numerics, takže aplikace ani HAL netáhnou WPF a zůstávají `net10.0`. Přibyla platforma `OrangePI` (Armbian/ARM64) s vlastním HAL a druhou verzí RealSense wrapperu (2.53 na ARM proti 2.47 na Windows — managed wrapper musí verzí sedět na nativní knihovnu, minor rozdíl není ABI-kompatibilní). D435 běžela v aplikaci na skutečném Orange Pi 5. 7.; VN100 (`VectorNav.dll` je MSIL, jde i na ARM) a T265 se doplnily 13. 7., sjednocené zapojení senzorů v `ARBotHW` s větví pro OrangePI 24. 7.

- [x] Projekty HAL v buildu, `Uart` na `System.IO.Ports`, WPF → System.Numerics, HALWindows bez `-windows` (30. 6. 2026)
- [x] Platforma `OrangePI`, `HALArmbian` s RealSense 2.53, D435 ověřena v aplikaci na Orange Pi (5. 7. 2026)
- [x] Reorganizace HAL do `Devices/*` a sjednocení namespace (7. 7. 2026)
- [x] VN100 a T265 v ARM buildu (13. 7. 2026)
- [x] Sjednocené zapojení senzorů v `ARBotHW` (port parametrem) s větví pro OrangePI (24. 7. 2026)

[build-and-platforms.md](build-and-platforms.md), [architecture.md](architecture.md) · DevLog [2026-06-30](devlog.md#2026-06-30), [2026-07-02](devlog.md#2026-07-02), [2026-07-05](devlog.md#2026-07-05), [2026-07-07](devlog.md#2026-07-07), [2026-07-13](devlog.md#2026-07-13), [2026-07-24](devlog.md#2026-07-24)

<a id="hw-gps-ubx-desync"></a>
### ✅ GPS četla UBX zprávy po částech a ztrácela synchronizaci

`hw-gps-ubx-desync` · vada · **hotovo** · nalezeno 13. 7. 2026 · vyřešeno 13. 7. 2026

`UBXMessage.Parse` četl payload jedním `Read(buf, 0, len)`, který na sériovém portu vrátí i méně bajtů, takže se parser rozjížděl a data chodila nerovnoměrně. Nahrazeno blokujícím čtením celé délky; fixy chodí plynule ~10 Hz. Rozhraní `IGPS` a `IMotorControl` při tom dostala událost `MeasurementArived`.

[hardware.md](hardware.md) · DevLog [2026-07-13](devlog.md#2026-07-13)

<a id="hw-kamery-hotplug"></a>
### ✅ Kamery RealSense odolné proti odpojení a znovupřipojení za běhu

`hw-kamery-hotplug` · záměr · **hotovo** · nalezeno 13. 7. 2026 · vyřešeno 6. 9. 2026

T265 i D435 se připojují líně ve vlastní smyčce, odpojení poznají přes `QueryDevices` a timeout `TryWaitForFrames` (T265 se odpojení projeví jen timeoutem, ne výjimkou), pipeline zboří a znovu postaví bez busy-loopu; `IsError` říká „nepřipojeno". Na obou platformách. Na zařízení mechanismus reconnectu prokazatelně běží (journal 6. 9. počítá reconnecty). Výpadky D435 za provozu se ale ukázaly jako širší, Intelem nevyřešený problém — zamrzlý stream, supervizor zotavení kamer a vadná USB větev jsou navazující samostatná témata.

- [x] T265 lazy reconnect (13. 7. 2026)
- [x] D435 lazy reconnect na obou platformách (21. 7. 2026)
- [x] Ověřeno na zařízení (reconnecty v journalu) (6. 9. 2026)

[hardware.md](hardware.md) · DevLog [2026-07-13](devlog.md#2026-07-13), [2026-07-21](devlog.md#2026-07-21), [2026-09-06](devlog.md#2026-09-06), [2026-09-11](devlog.md#2026-09-11)

<a id="hw-t265-v-librealsense-253"></a>
### ✅ Obava, že librealsense 2.53 už T265 nepodporuje

`hw-t265-v-librealsense-253` · vada · **hotovo** · nalezeno 13. 7. 2026 · vyřešeno 11. 9. 2026

Při portu T265 na Armbian se zapsalo riziko „T265 byl v librealsense 2.50+ odebrán, podporu v 2.53 nutno ověřit na HW", a `build-and-platforms.md` to vedl jako otevřenou otázku. Rešerše 11. 9. ukázala, že to byl omyl: `v2.53.1` má `src/tm2` a T265 vypadl až ve 2.54.1; 2.50 je jen poslední validovaná verze. Na zařízení T265 3. 9. skutečně nabootovala. Sedíme tedy na stropu verzí, nahoru ani dolů nemá smysl. (Že se T265 13. 9. odpojila natrvalo, je jiné rozhodnutí.)

- [x] T265 běží na zařízení (3. 9. 2026)
- [x] Rešerše a oprava tvrzení v dokumentaci (11. 9. 2026)

[build-and-platforms.md](build-and-platforms.md), [hardware.md](hardware.md) · DevLog [2026-07-13](devlog.md#2026-07-13), [2026-07-30](devlog.md#2026-07-30), [2026-09-11](devlog.md#2026-09-11)

<a id="hw-realsense-retez-hubu"></a>
### ✅ Kamery RealSense nešly za řetězem dvou USB hubů

`hw-realsense-retez-hubu` · vada · **hotovo** · nalezeno 30. 8. 2026 · vyřešeno 30. 8. 2026

T265 se nezobrazila vůbec a D435 hlásily „no frames received" — vypadalo to na softwarovou vadu, ale hardware byl v pořádku. Obě D435 visely za řetězem dvou USB3 hubů a při současné inicializaci se praly o zdroj (pokaždé selhala jiná). Změřeno skriptem `rs-bench`: za dvěma huby 0 z 5, přímo na desce 10 z 10, za jedním napájeným hubem 10 z 10. Vadí tedy až dva huby za sebou, ne hub sám; robot jede na jednom napájeném hubu. Dvě zamítnuté hypotézy (napájení, `uvcvideo`) jsou v postupu zapsané.

- [x] Změřit varianty zapojení `rs-bench` (5 běhů na variantu) (30. 8. 2026)
- [x] Přepojit na jeden napájený hub, obraz potvrzen autorem (30. 8. 2026)

[OrangePi5Ultra/POSTUP.md (krok 9)](../OrangePi5Ultra/POSTUP.md), [hardware.md](hardware.md) · DevLog [2026-08-30](devlog.md#2026-08-30)

<a id="hw-seriove-porty-pi"></a>
### ✅ Sériové porty periferií na Orange Pi byly jen odhad — a špatný

`hw-seriove-porty-pi` · vada · **hotovo** · nalezeno 31. 8. 2026 · vyřešeno 31. 8. 2026

V kódu pro ARM64 byl jen odhad `/dev/ttyS0` pro IMU a motor s GPS neměly port vůbec, takže by se na Pi nezaložily. Skript `find-serial-ports.sh` porty najde pasivně (bez zápisu do nich) a vypíše hotové parametry: všechny tři periferie visí na USB (VN100 přes CP2102, Roboteq a u-blox jako USB CDC), `/dev/ttyS0` na RK3588 neexistuje. Do kódu šla jména z `/dev/serial/by-id`, protože čísla `ttyACM*` závisejí na pořadí enumerace a prohození GPS s motorem by bylo tiché. Ověřeno na zařízení dekódováním streamu VN100 a týž den během aplikace s reálnými drivery (GPS 9,99 Hz, IMU 8 kB/s, motor 386 řádků/s).

- [x] Skript `find-serial-ports.sh`, porty `by-id` v `ARBotHW.Init` (31. 8. 2026)
- [x] Aplikace na Pi s reálnými drivery všech tří UART senzorů (31. 8. 2026)

[hardware.md](hardware.md), [find-serial-ports.sh](../OrangePi5Ultra/find-serial-ports.sh) · DevLog [2026-08-31](devlog.md#2026-08-31)

<a id="hw-uart-cteni-po-bajtu"></a>
### ✅ GPS ztrácela 92 % měření, protože se port četl po jednom bajtu

`hw-uart-cteni-po-bajtu` · vada · **hotovo** · nalezeno 31. 8. 2026 · vyřešeno 31. 8. 2026

Autor viděl GPS na 0,8 Hz s občasným skokem na 3,2 Hz; první vysvětlení „u-blox má výchozích 1 Hz" bylo hádání. Přijímač ve skutečnosti jede 10 Hz a k tomu posílá ~200 NMEA vět za sekundu, ale `Uart.Read(int)` bral z portu jeden bajt a při prázdném portu spal 10 ms. Změřeno vedle sebe na zařízení: 0,88 proti 10,09 měření/s. Čtení si teď bere všechno, co v portu je, do vnitřního bufferu; ostatní způsoby čtení buffer nejdřív vyprázdní, aby se styly nemíchaly. Ověřeno na zařízení reálnými drivery (GPS 9,99 Hz, IMU i motor beze změny). Vypnutí NMEA v přijímači (87 % dat) se vědomě neudělalo — vada byla v našem čtení.

- [x] Změřit přijímač proti driveru na volném portu (31. 8. 2026)
- [x] Vnitřní buffer v `Uart.Read(int)`, ověřeno na zařízení (31. 8. 2026)

[rozhodnutí 31. 8. 2026](decisions.md), [hardware.md](hardware.md) · DevLog [2026-08-31](devlog.md#2026-08-31)

<a id="hw-zamrzla-kamera-tvarila-se-ok"></a>
### ✅ Zaseknutá kamera D435 se tvářila zdravě navždy

`hw-zamrzla-kamera-tvarila-se-ok` · vada · **hotovo** · nalezeno 1. 9. 2026 · vyřešeno 1. 9. 2026

Při dořešení odmlčené pravé kamery z 31. 8. se našly dvě vady v driveru: kamera, které přestaly chodit snímky, z USB nezmizela, takže se pipeline nikdy nezbourala a panel hlásil OK; a selhání dotazu na USB (`failed to set power state`) se hlásilo jako „kamera odpojena", což by poslalo člověka hledat kabel. Nově se počítají timeouty po sobě a po třech se pipeline restartuje (`StallRestarts`), dotaz na přítomnost umí říct „nevím". Hlavní příčina 31. 8. byla ale fyzická (port 4 hubu), kód je záchranná síť — a ta 13. 9. skutečnou poruchu opravdu zachytila.

- [x] Zásek podle počtu timeoutů, restart pipeline, `DevicePresent` jako `bool?` (1. 9. 2026)
- [x] Ověřeno na zařízení s uměle zkráceným timeoutem (1. 9. 2026)
- [x] Zachytilo skutečný zásek za provozu (obnova kamer za 29 s) (13. 9. 2026)

[hardware.md](hardware.md), [rozhodnutí 1. 9. 2026](decisions.md) · DevLog [2026-09-01](devlog.md#2026-09-01), [2026-09-13](devlog.md#2026-09-13)

<a id="hw-realsense-jeden-kontext"></a>
### ✅ Tři drivery se třemi kontexty RealSense bootovaly T265 naráz a shodily proces

`hw-realsense-jeden-kontext` · vada · **hotovo** · nalezeno 3. 9. 2026 · vyřešeno 3. 9. 2026

První pád rozebraný z minidumpu: SIGSEGV v librealsense při bootu firmwaru T265, když zařízení mezi enumerací a otevřením změnilo USB identitu. U nás k tomu docházelo proto, že každý ze tří driverů kamer měl vlastní `Context` a každý dotaz na zařízení spouštěl boot T265 — tři konkurenční bootery nad běžícími streamy. Léčba `RealSenseShared`: jeden kontext, všechny dotazy pod jedním zámkem, T265 se nabootuje synchronně před D435, k tomu hardware reset při zaseknutém stavu. Nasazeno na Pi týž den; hardware reset se 13. 9. na robotu zkusil (T265 v rozbitém stavu nespravil, to umí jen fyzické přepojení).

- [x] `RealSenseShared` — jeden kontext, zámek, boot T265 před D435 (3. 9. 2026)
- [x] Hardware reset T265 v driveru (3. 9. 2026)
- [x] Nasazeno a běží na Pi (3. 9. 2026)

[hardware.md](hardware.md) · DevLog [2026-09-03](devlog.md#2026-09-03), [2026-09-13](devlog.md#2026-09-13)

<a id="hw-d435-zamrzly-stream"></a>
### ✅ Pravá D435 posílala pořád tentýž barevný snímek a nikdo to nepoznal

`hw-d435-zamrzly-stream` · vada · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 6. 9. 2026

V 11minutovém záznamu měla pravá kamera jeden různý barevný obraz ze sta, hloubka jela; snímky chodily 10 Hz, stránka svítila zeleně a occupancy grid i mise běžely nad nehybnou fotkou. Vada je v librealsense/USB (stojí i razítko), naše kopie je v pořádku. Léčba: `StreamFreezeWatch` hlídá razítka a při stání přes 5 s zboří pipeline; přibyl rozbor `ARBot.Analyze cameras`. Regrese z téhož dne (hlídka četla razítko z už uvolněného framu a shodila každý grab) se našla náhodou v journalu a opravila; s opravou na robotu 0 chyb. Že se kamera restartem probere, ukázalo až zotavení kamer z 13. 9.

- [x] `ARBot.Analyze cameras` a nález nad záznamem (6. 9. 2026)
- [x] `StreamFreezeWatch` na obou platformách (6. 9. 2026)
- [x] Regrese (frame uvolněný před čtením razítka) opravena a ověřena na robotu (6. 9. 2026)

[hardware.md](hardware.md), [record-replay.md](record-replay.md) · DevLog [2026-09-06](devlog.md#2026-09-06), [2026-09-12](devlog.md#2026-09-12), [2026-09-13](devlog.md#2026-09-13)

<a id="hw-vn100-heading-relative"></a>
### ✅ Kurz z VN100 byl o −59° vedle, protože senzor přišel o konfiguraci

`hw-vn100-heading-relative` · vada · **hotovo** · nalezeno 6. 9. 2026 · vyřešeno 6. 9. 2026

Čtyři dny předtím kurz seděl na −0,25°. Ze záznamu se dokázalo, že chyba není v GPS, v kódu ani v gyru, ale v atitudovém řešení senzoru — a ten si přitom hlásil nejistotu 0,23°, kterou fúze brala doslova. Read-only `deploy/vnprobe.sh` na živém senzoru našel dva změněné registry: heading mode `Relative` místo `Absolute` (yaw k místu náběhu, ne k severu) a vymazanou kalibraci magnetometru. `vnrestore.sh` obojí obnovil do flash; heading mode zabral prokazatelně (kurz se za ~100 s přetočil na pole). Obnovená kalibrace z ARBot2 se ale ukázala horší než žádná (bias větší než zemské pole, kompas přestal reagovat na otáčení) a týž den se vymazala.

- [x] `ARBot.Analyze heading` bez ground truth a `ARBot.Analyze vn100` ze záznamu (6. 9. 2026)
- [x] `deploy/vnprobe.sh` — registry 35 a 23 nalezeny jako příčina (6. 9. 2026)
- [x] `deploy/vnrestore.sh` — Absolute + zápis do flash (6. 9. 2026)
- [x] Stará kalibrace změřena jako horší než žádná a vymazána (`--clearmag`) (6. 9. 2026)

[imu-and-frames.md](imu-and-frames.md), [vnprobe.sh](../deploy/vnprobe.sh), [vnrestore.sh](../deploy/vnrestore.sh) · DevLog [2026-09-06](devlog.md#2026-09-06)

<a id="hw-vn100-vpe-tahne-za-polem"></a>
### ✅ Kurz ze senzoru se táhne za vlastním magnetometrem minuty

`hw-vn100-vpe-tahne-za-polem` · vada · **hotovo** · nalezeno 7. 9. 2026 · vyřešeno 18. 9. 2026

Zesílení zpětné vazby od magnetometru vyšlo `K` = 0,0049 1/s, tedy časová konstanta 206 s: po zatáčce je yaw desítky stupňů vedle i proti svému vlastnímu poli. Původní závěr „to kalibrace neopraví" byl 10. 9. podle manuálu VN označen za nepodložený a 12. 9. se po kalibraci konstanta zkrátila na 53 s — z většiny to tedy bylo nezkalibrované železo. Ze 14. 9. je ale zpátky a horší (345 s), protože na robotu přibylo nové železo. Je to v senzoru; nastavení nejistot ve fúzi na to nesahá. ✅ **Uzavřeno 18. 9. 2026:** po nové kalibraci τ 28–53 s ve dvou jízdách (12. 9. 53 s, 14. 9. s rozbitým železem 345 s, původně 206 s). Dlouhá konstanta byla důsledek nezkalibrovaného železa; zbylých ~30–50 s je vlastnost VPE (registr 35 má zapnuté adaptivní filtrování) a fúzi nevadí — mezi odečty kompasu nese kurz gyro.

- [x] Změřit `K` ze záznamu (blok 2 `ARBot.Analyze vn100`) (7. 9. 2026)
- [x] Přeměřit po kalibraci (206 → 53 s) (12. 9. 2026)
- [x] Přeměřit po nové kalibraci — 18. 9.: `K` 0,036 / 0,019 1/s (τ 28 / 53 s) ve dvou jízdách, tedy jako 12. 9.; se špatným železem 14. 9. bylo 345 s (18. 9. 2026)

čeká na [hw-zelezo-od-kabelu-kamer](#hw-zelezo-od-kabelu-kamer) · [imu-and-frames.md](imu-and-frames.md) · DevLog [2026-09-07](devlog.md#2026-09-07), [2026-09-10](devlog.md#2026-09-10), [2026-09-12](devlog.md#2026-09-12), [2026-09-15](devlog.md#2026-09-15), [2026-09-18](devlog.md#2026-09-18)

<a id="hw-vn100-zelezo-na-robotu"></a>
### ✅ Železo na těle robota kazí kurz o desítky stupňů

`hw-vn100-zelezo-na-robotu` · vada · **hotovo** · nalezeno 7. 9. 2026 · vyřešeno 12. 9. 2026

Po opravě registrů projetá smyčka venku: `IMU yaw − GPS kurz` p50 −24°, sd 18,6°, přičemž chybuje IMU (třetí nezávislá cesta Doppler − posun polohy sedí na 0,3°). Podpis je železo vázané na tělo: tvrdé 27,2°, měkké 25,2°, `|B|` i sklon kolísají, ačkoli mají být konstanty. Motory to skoro nejsou (−0,0026 G/A, desetina rozpětí), takže to kalibrace odečte. Změřila se misí `magcal` 10. 9., zapsala do senzoru 11. 9. a 12. 9. venku sedí: zbytkové tvrdé železo 0,0023 G, rozpětí `|B|` 0,148 → 0,019 G, kurz −24° → −3,6°. Zbylá konstanta −3,7° není železo a tímhle měřením ji rozložit nejde.

- [x] Smyčka venku a blok 4 `ARBot.Analyze vn100` (párování pole s proudem motorů) (7. 9. 2026)
- [x] Kalibrace změřena v terénu misí `magcal` (10. 9. 2026)
- [x] Zapsána do senzoru a do flash (`vnrestore.sh --magcal`) (11. 9. 2026)
- [x] Ověřena venku třemi záznamy (12. 9. 2026)

[imu-and-frames.md](imu-and-frames.md), [Vn100Report.cs](../Src/ARBot.Analyze/Vn100Report.cs) · DevLog [2026-09-07](devlog.md#2026-09-07), [2026-09-10](devlog.md#2026-09-10), [2026-09-11](devlog.md#2026-09-11), [2026-09-12](devlog.md#2026-09-12), [2026-09-15](devlog.md#2026-09-15)

<a id="hw-magcal-prvni-vyjezd"></a>
### ✅ První výjezd s kalibrací magnetometru skončil bez výsledku a našel tři vady

`hw-magcal-prvni-vyjezd` · vada · **hotovo** · nalezeno 10. 9. 2026 · vyřešeno 17. 9. 2026

Obsluha `mission=magcal` nedovedla do konce, protože verdikt byl diagnóza bez pokynu (při kompletním pokrytí radil „otáčej dál"), skupiny náklonu se klíčovaly velikostí odklonu, kterou ruka neudrží, a obsluha neviděla, kam robota natočit. Léčba: vedle elipsoidy se prokládá i samotná koule, která rozliší „chybí náklon" od „měnilo se pole"; jde zapsat aspoň tvrdé železo; místo půdorysu je mapa pokrytí 24 × 5 poloh robota. Z dokumentace VN100 přibyly dvě opravy práce s registrem 44 (reset před během, vypnutí i po nedokončené misi). Rovinná rotace přitom projde všemi branami a vrátí nesmyslný bias, proto přibyla třetí brána na měřítko. Od 15. 9. jde celá kalibrace projít v simulaci. ✅ **Na skutečném senzoru to doběhlo 17. 9. 2026** (`records/test/20260917-161759.rec`) a v záznamu je vidět, že zabraly všechny tři opravy: pokrytí vyšlo **úplné** (24/24 azimutů, 5 náklonových skupin, z toho 4 odkloněné, na obě strany), skupiny jsou klíčované **polohou robota**, ne velikostí odklonu („na rovině, zvednutý předek, zvednutá levá, zvednutá zadní, zvednutá pravá"), a verdikt byl po celou dobu **pokyn, ne diagnóza** — od „chybí azimuty 30–345°; podlož robota aspoň o 15 stupňů" přes „máš jen jednu stranu (zvednutá zadní) — podlož robota na DRUHOU stranu" až po **HOTOVO ve 48. s** (podmíněnost 330, `sd|B|` 0,0026 G). Kalibrace se v 16:20:20 zapsala do registru 23 i do flash. Tím je téma uzavřené. ⚠️ Co z toho neplyne: že je ta kalibrace v pořádku dlouhodobě (drží se `hw-zelezo-od-kabelu-kamer`) a že stránka po zápisu říká pravdu — kolektor sbírá dál a verdikt se rozpadne, viz `mise-magcal-sber-po-zapisu`.

- [x] Proložení koule (`TryFitSphere`) a verdikt s pokynem (10. 9. 2026)
- [x] Zápis samotného tvrdého železa ze stránky (10. 9. 2026)
- [x] Mapa pokrytí 24 × 5 a klíčování směrem místo velikostí náklonu (10. 9. 2026)
- [x] Registr 44 se před během resetuje a po misi vypíná (10. 9. 2026)
- [x] Kalibrace projitá od začátku do konce v simulaci s vnuceným železem (15. 9. 2026)
- [x] Spustit `mission=magcal` na senzoru s novým kódem (17. 9. 2026)

[plan-vn100-kalibrace.md](plan-vn100-kalibrace.md), [rozhodnutí 10. 9. 2026](decisions.md), [imu-and-frames.md](imu-and-frames.md) · DevLog [2026-09-10](devlog.md#2026-09-10), [2026-09-15](devlog.md#2026-09-15), [2026-09-17](devlog.md#2026-09-17)

<a id="hw-magcal-sklon-brana-a-nasazeni"></a>
### ✅ Verdikt kalibrace shodil rozptyl sklonu, který měří akcelerometr, ne magnetometr

`hw-magcal-sklon-brana-a-nasazeni` · vada · **hotovo** · nalezeno 10. 9. 2026 · vyřešeno 12. 9. 2026

Druhý výjezd 10. 9. doběhl s úplným pokrytím a elipsoida se proložila, ale verdikt zamítl výsledek kvůli rozptylu sklonu 2,09° proti prahu 0,5° — a ten roste s dynamikou otáčení rukou a s biasem akcelerometru, o poli nic neříká. Stránka tak nabídla horší výsledek, než který odmítla. Autor rozhodl sklon z brány vyřadit (jen diagnostika); kalibrace z toho záznamu se 11. 9. zapsala do senzoru a do flash (`deploy/vnrestore.sh --magcal`) a 12. 9. venku sedí: rozpětí velikosti pole přes otočku 0,148 → 0,019 G, kurz proti GPS z −24° na −3,6°, setrvačnost atitudy 206 → 53 s. Od 14. 9. ji ale přebilo železo od kabelů ke kamerám (samostatné téma).

- [x] Rozbor sklonu po řádcích a azimutech, akcelerometr jako koule (10. 9. 2026)
- [x] Rozptyl sklonu vyřazen z brány, jen diagnostika (10. 9. 2026)
- [x] Kalibrace ze záznamu zapsána do senzoru a flash (11. 9. 2026)
- [x] Ověřena venku třemi záznamy včetně statické otočky (12. 9. 2026)

[rozhodnutí 10. 9. 2026](decisions.md), [plan-vn100-kalibrace.md](plan-vn100-kalibrace.md), [imu-and-frames.md](imu-and-frames.md), [deploy/README.md](../deploy/README.md) · DevLog [2026-09-10](devlog.md#2026-09-10), [2026-09-11](devlog.md#2026-09-11), [2026-09-12](devlog.md#2026-09-12)

<a id="hw-magcal-ramec-registru-23"></a>
### ✅ Kalibrace se do senzoru zapisovala ve špatném rámci, offset se přičítal

`hw-magcal-ramec-registru-23` · vada · **hotovo** · nalezeno 11. 9. 2026 · vyřešeno 11. 9. 2026

Při nasazení kalibrace se na živém senzoru změřilo, jak VN100 registr 23 aplikuje: `C·(m − b)`, tedy stejný vzorec jako náš `Apply`, potvrzeno i z ICD. Měřit se musí přes víc os a s nejednotkovou maticí — z osy X samotné vyjde opak, protože mezi kompenzací a výstupem leží registr 26, a jednotková matice nerozliší `C·m − b` od `C·(m − b)`. Hlavní nález: registr 23 se aplikuje před registrem 26, fit běží až za převodem do rámce robota, takže bias v X a Y měl obrácené znaménko a offset 0,22 G se přičítal; v tom stavu byla kalibrace pár hodin na robotu. `ToVnwrg23()` teď rámec převádí, hlídají to testy a 12. 9. venku kalibrace sedí.

- [x] Konvence registru 23 změřena na senzoru a doložena ICD (11. 9. 2026)
- [x] Rámec změřen čistým biasem po osách, `ToVnwrg23()` převádí (11. 9. 2026)

[rozhodnutí 11. 9. 2026](decisions.md), [plan-vn100-kalibrace.md](plan-vn100-kalibrace.md), [deploy/vnrestore.sh](../deploy/vnrestore.sh) · DevLog [2026-09-11](devlog.md#2026-09-11), [2026-09-12](devlog.md#2026-09-12)

<a id="hw-zelezo-od-kabelu-kamer"></a>
### ✅ Kalibrace magnetometru přestala účinkovat — přibylo železo od kabelů ke kamerám

`hw-zelezo-od-kabelu-kamer` · vada · **hotovo** · nalezeno 15. 9. 2026 · vyřešeno 18. 9. 2026

Kalibrace z 11. 9., ověřená 12. 9., v záznamech ze 14. 9. už neúčinkuje: velikost pole při stání 0,614 G proti 0,4897, rozpětí přes záznam 0,177 G (12. 9. 0,019 G), `IMU yaw − GPS kurz` p50 −15,6° a atitudové řešení senzoru se táhne za polem 345 s. Fúze kurz přebírá, takže to jde 1:1 do mapy i do mrkve — a platí to zpětně pro všechna měření nad těmi záznamy. Zdroj zúžen měřením na kabely ke kamerám (13. 9. se prohodily): je to jejich železo, ne proud — rozsvícení obou kamer posune pole jen o 6,4 mG, 4 % offsetu. 16. 9. železo přetrvává. Pro test kabelů vzniklo živé měřidlo v UI (panel magnetometru v dokumentu IMU s řádkem „Klid“, protože 1° otočení dělá víc než celý hledaný efekt) — na skutečném VN100 neběželo. Nevysvětlený zůstává klidový bias gyra −161 a −453 °/h proti +13 °/h 12. 9. 17. 9. 2026 to bylo ještě horší než 14. 9.: `|B|` p50 0,693 G proti referenčním 0,4897 (14. 9. 0,614), rozpětí 0,584–0,737 G, a kurz z kompasu byl fakticky náhodný — `IMU yaw − GPS kurz` sd 121°, 2. harmonická 114°, a ze tří modelů vyhrál „zamrzlý kompas" (zbytkový rozptyl 76,5° proti 96 a 115). Že chybuje IMU a ne GPS, potvrdila třetí cesta: `Doppler − směr posunu polohy` −1,8° ± 12,9°. Fúze kurz přebírá (`odhad − IMU yaw` 18,1° ± 17,4°), takže to šlo 1:1 do mapy i do mrkve. Nová kalibrace se týž den změřila (`mission=magcal`) a v 16:20:20 zapsala do registru 23 i do flash; tvrdé železo z proložení koule vyšlo 0,2518 G. Obsluha hlásí, že směr při následující jízdě vypadal velmi dobře — záznam z ní ale není, takže ověřené to není. ✅ **Uzavřeno 18. 9. 2026:** nová kalibrace ze 17. 9. při první jízdě drží — zbytkové vodorovné železo **11–18 mG** (14./17. 9.: 158–169 mG; 12. 9. při otáčení na místě 1,3 mG), `|B|` na referenci, VPE τ 28–53 s, kurz proti GPS −2,5 / −1,6°. Kabely jsou statické (přechody kamer 1–6 mG). Platnost kalibrace je dál vázaná na polohu kabelů — při jejich pohybu znovu `mission=magcal`. Tabulka: `imu-and-frames.md`, „Po NOVÉ kalibraci".

- [x] Rozbor záznamů ze 14. 9., dva omyly opravené měřením (15. 9. 2026)
- [x] Měřidla `ARBot.Analyze vn100` blok 5 (kamery) a `heading --bin` (vývoj rozporu v čase) (15. 9. 2026)
- [x] Panel magnetometru v UI (`MagTrace`, 11 testů, ověřeno v simulaci) (15. 9. 2026)
- [x] Kabely v poloze, ve které se 17. 9. kalibrovalo; 18. 9. jízda: skok pole při zapnutí kamer 1–6 mG (statické, v kalibraci) (17. 9. 2026)
- [x] `mission=magcal` s náklony na obě strany a nová kalibrace do senzoru (17. 9. 2026)
- [x] Ověřit novou kalibraci záznamem — 18. 9. (`20260918-154028.rec`, `-155329.rec`): zbytkové vodorovné železo 11–18 mG proti 158–169 mG, `|B|` 0,484–0,490 G, `IMU − GPS kurz` −2,5 / −1,6° (sd ~4°), VPE τ 28–53 s (18. 9. 2026)

[imu-and-frames.md](imu-and-frames.md), [panel magnetometru (snímek)](media/imu-magnetometr-2026-09-15.png) · DevLog [2026-09-15](devlog.md#2026-09-15), [2026-09-16](devlog.md#2026-09-16), [2026-09-17](devlog.md#2026-09-17), [2026-09-18](devlog.md#2026-09-18)

<a id="hw-t265-nedava-pozu"></a>
### ❌ T265 nedává pózu — firmware hlásí chybu vidění

`hw-t265-nedava-pozu` · vada · **zamítnuto** · nalezeno 3. 9. 2026 · vyřešeno 13. 9. 2026

Ani po opravě driverů z T265 nechodila póza. Sonda přímo nad librealsense ukázala, že gyro a akcelerometr chodí, ale on-device VIO půl sekundy po startu hlásí `SLAM_ERROR Vision` — v místnosti byla tma. Zároveň se ukázalo, že „OK" v panelu senzorů znamenalo jen otevřenou pipeline, ne snímky. Dál se zjistilo (6. 9.), že T265 nebyla do fúze vůbec napojená, a 13. 9. se po replugu pózu dávat naučila, ale za 1,5 h se zasekla znovu a zotavení jí nepomáhá. Autor 13. 9. rozhodl kameru odpojit natrvalo — dělá víc potíží než užitku.

- [x] Sonda nad librealsense: `SLAM_ERROR Vision`, příčina tma (3. 9. 2026)
- [x] Po replugu dává pózu, za 1,5 h se zasekne znovu (13. 9. 2026)
- [x] Rozhodnutí odpojit T265 natrvalo (13. 9. 2026)

[hardware.md](hardware.md), [rozhodnutí 13. 9. 2026](decisions.md) · DevLog [2026-09-03](devlog.md#2026-09-03), [2026-09-06](devlog.md#2026-09-06), [2026-09-13](devlog.md#2026-09-13)

<a id="hw-vypadky-kamer-hypoteza-t265"></a>
### ❌ Za výpadky kamer může souběh s T265 (hypotéza CLEAR_HALT)

`hw-vypadky-kamer-hypoteza-t265` · vada · **zamítnuto** · nalezeno 11. 9. 2026 · vyřešeno 14. 9. 2026

Rešerše ukázala, že odmlčení D435 za provozu je cizí, Intelem nevyřešený problém a naše léčba (detekce, zbourání pipeline, reconnect) je to, k čemu dojdou všichni. Nejsilnější stopa byla vlastní: po přidání T265 vyskočil `CLEAR_HALT` z 1 na ~72. Propustnost USB i VN100 s GPS na témž hubu se vyloučily výpočtem, opravily se dva omyly o verzi SDK a backendu, a rozhodlo se na backend ani verzi nesahat, dokud se to nezměří. Měření 14. 9. bez T265 hypotézu vyvrátilo: `CLEAR_HALT` i zamrzání streamu zůstaly stejné a `CLEAR_HALT` teče i ve zdravém stavu, jako měřidlo je mrtvý. Podezřelá je od té doby fyzická větev USB `2-1.3`. Vedlejší výsledek: driver hlásí typ USB linky.

- [x] Rešerše a rozhodnutí nejdřív měřit (11. 9. 2026)
- [x] Driver hlásí typ USB linky (`UsbLinkCheck`), na HW neběželo (11. 9. 2026)
- [x] Běh bez T265 změřen na robotu, hypotéza padla (14. 9. 2026)

[hardware.md](hardware.md), [rozhodnutí 11. 9. 2026](decisions.md), [build-and-platforms.md](build-and-platforms.md) · DevLog [2026-09-11](devlog.md#2026-09-11), [2026-09-14](devlog.md#2026-09-14)

## Nástroje, záznam a analýza

<a id="nast-hlasky-startu-do-zaznamu"></a>
### ⬜ Hlášky ze startu runtime se do záznamu nedostanou

`nast-hlasky-startu-do-zaznamu` · vada · **otevřeno** · nalezeno 20. 8. 2026

Most `Trace` → záznam se připojuje až na konci drátování, takže hlášky o načtení mapy, počáteční póze nebo o tom, proč se nějaký stupeň nezaložil, jdou jen do debug outputu. U záznamu z terénu se tak nedá přečíst, proč něco nevzniklo. Účinná konfigurace se od 5. 9. po připojení mostu zopakuje a přibyl příkaz `ARBot.Analyze log`, ale trasovací hlášky z drátování zůstávají mimo (znovu nalezeno 15. 9.). Nejnověji chyběl řádek „corridor=false: hranová lokalizace se nezakládá“ — v journalu byl, v `.rec` ne.

- [x] Konfigurace a verze se po připojení mostu zopakují do záznamu (5. 9. 2026)
- [x] `ARBot.Analyze log` — textový log ze záznamu (5. 9. 2026)
- [ ] Připojit most dřív nebo hlášky z drátování pufrovat

[record-replay.md](record-replay.md), [headless.md](headless.md) · DevLog [2026-08-20](devlog.md#2026-08-20), [2026-09-05](devlog.md#2026-09-05), [2026-09-15](devlog.md#2026-09-15)

<a id="nast-avalonia-headless-testy"></a>
### ⬜ Headless testy UI v Avalonii — ověřeno spikem, nezavedeno

`nast-avalonia-headless-testy` · záměr · **otevřeno** · nalezeno 1. 9. 2026

Na otázku autora, jestli by šlo omezit jeho účast při klikání v UI, se spikem mimo repozitář ověřilo, že `Avalonia.Headless.NUnit` vykreslí skutečný vizuální strom včetně DataGridu a recyklace řádků — tedy přesně mechanismus vady z 31. 8. v panelu Konfigurace. Cena je NUnit 4.5.1 (projekt pinuje 4.3.2) a nový testovací projekt; hlavní riziko jsou UI testy, které projdou naprázdno. Dvakrát se to použilo jednorázově k ověření změn; do repa zavedeno není.

- [x] Spike: panel Výkon a DataGrid Konfigurace headless (1. 9. 2026)
- [ ] Zavést testovací projekt a pravidlo „asertovat i předpoklad"

[Views/README.md](../Src/ARBot/Views/README.md) · DevLog [2026-09-01](devlog.md#2026-09-01), [2026-09-02](devlog.md#2026-09-02)

<a id="nast-panel-konfigurace-mazal-klice"></a>
### 🧪 Panel Konfigurace tiše mazal z profilu klíče shodné s defaultem

`nast-panel-konfigurace-mazal-klice` · vada · **v kódu, na HW neověřeno** · nalezeno 12. 9. 2026 · vyřešeno 12. 9. 2026

Uložení profilu z panelu zapisovalo jen hodnoty odlišné od defaultu, takže z `pi-provoz.cfg` zmizel schválně připnutý `npumodel=` — po příští změně defaultu by robot tiše počítal jiným modelem. Teď se zapisují i klíče, které v profilu výslovně byly. Z téhož uložení vyšel druhý nález: panel uložil cestu s windowsovým zpětným lomítkem, které na Linuxu je obyčejný znak, takže by mapa na Pi nebyla nalezena; hlídá to nový test nad profily v repu. Ručně psané komentáře v profilu se při uložení ztrácejí dál (skládají se znovu z registru) — známá mez. Uložení v UI proklikané není.

- [x] Zapisují se i výslovně nastavené klíče (12. 9. 2026)
- [x] Test na linuxový tvar cest v profilech (12. 9. 2026)
- [ ] Proklikat uložení v panelu

[configuration.md](configuration.md) · DevLog [2026-09-12](devlog.md#2026-09-12)

<a id="nast-rezim-simulate"></a>
### ⏸ Režim Simulate — věrný přepočet běhu nad záznamem

`nast-rezim-simulate` · záměr · **odloženo** · nalezeno 27. 7. 2026

Třetí režim vedle Run a View: přehrát záznam do skutečné fúze, vize a řízení a porovnat výstup s tím, co robot udělal. Odloženo, protože je to rozsáhlé — replay řízený časem příchodu (`T_out`), reprodukce lokální mapy a vize, virtuální hodiny nad souborem — a i tak zůstane reziduální nejistota. Háček v datech se zavedl hned: záznam nese `T_in` i `T_out`, takže se Simulate postaví bez přepisu formátu. Část potřeby od té doby kryje offline `ARBot.Analyze` (přepočet koridoru, gridu, obálky ze zaznamenaných dat).

- [x] Záznam nese `T_in` i `T_out` (27. 7. 2026)
- [ ] `T_out`-řízený replay, virtuální hodiny, porovnání s tím, co robot udělal

[record-replay.md (Odložený Simulate)](record-replay.md) · DevLog [2026-07-27](devlog.md#2026-07-27)

<a id="nast-mapsui-pad-vrstvy-hranic"></a>
### ⏸ Vrstva hranic občas shodí Mapsui při přehrávání

`nast-mapsui-pad-vrstvy-hranic` · vada · **odloženo** · nalezeno 23. 8. 2026

Při přehrávání se zapnutou vrstvou hranic občas vyskočí `NullReferenceException` uvnitř Mapsui (`GetExtent`). Diagnostika v okamžiku pádu vyloučila obsah featur (395 featur, žádná null ani bez extentu) i data (tytéž featury offline ve skutečném Mapsui: 322 cyklů, nula pádů); chování není deterministické. Vypadá to na souběh nad toutéž instancí vrstvy na straně knihovny. Po dohodě s autorem odloženo; platí pojistka `try/catch` — vrstva se vypne a důvod je v rámečku.

- [x] Pojistka: pád vrstvu vypne a důvod ukáže v rámečku (23. 8. 2026)

[world-view.md](world-view.md) · DevLog [2026-08-23](devlog.md#2026-08-23)

<a id="nast-port-arbot2-matrix"></a>
### ✅ Port z ARBot2 — vlastní třída `Matrix` nahrazena MathNet a testy převedeny na NUnit

`nast-port-arbot2-matrix` · záměr · **hotovo** · nalezeno 23. 6. 2026 · vyřešeno 24. 6. 2026

Založení repozitáře ARBot3 a první krok portu z ARBot2: domácí třída `Matrix` (~2000 řádků netestovaného kódu) se smazala a všichni živí uživatelé (`ECEF`, `Transformation`, `ICP`, `Intrinsics`, `EKFStepMsg`) přešli na MathNet.Numerics, pro fixní 3D geometrii na System.Numerics. MathNet vyhrál mikro-benchmarkem (~2,5× rychlejší i na malých maticích EKF) a prověřenými dekompozicemi. Staré MSTest testy se převedly na NUnit a jako síť před migrací se přenesly charakterizační testy; mrtvý generický EKF framework vypadl z buildu.

- [x] Založení repozitáře (`.gitattributes`, `.gitignore`, README, LICENSE) (23. 6. 2026)
- [x] Odstranění `ARBot.Common.Common.Matrix`, přepis uživatelů na MathNet / System.Numerics (24. 6. 2026)
- [x] Migrace testů na NUnit + charakterizační testy `Transformation` / `ECEF` (24. 6. 2026)

[architecture.md](architecture.md) · DevLog [2026-06-23](devlog.md#2026-06-23), [2026-06-24](devlog.md#2026-06-24)

<a id="nast-ui-avalonia-dock"></a>
### ✅ Uživatelské rozhraní — dokování, dokumenty senzorů a diagnostické panely

`nast-ui-avalonia-dock` · záměr · **hotovo** · nalezeno 30. 6. 2026 · vyřešeno 30. 7. 2026

Aplikace dostala Avalonia okno s dokovacím enginem Dock 12, první živý dokument (RGB stream D435), panel Debug output napojený na `Trace` (s filtrem neškodných binding-warningů Avalonie), panel Sensors se stavem senzorů a spolehlivé znovuotevírání panelů včetně plovoucích oken. Základ `DocumentBase` / `ToolBase` s `ViewLocator` nahradil inline šablony samostatnými pohledy s design-time náhledem. Senzory mají vlastní dokumenty — IMU s kompasem a umělým horizontem z kvaternionu, GPS, motory, kamera s přepínačem RGB/hloubka — otevírané dvojklikem, aktualizované událostmi z driveru, ne pollováním.

- [x] MainWindow + Dock 12 (`DockFactory`, `DockControl`) (30. 6. 2026)
- [x] Dokument D435 Test s živým RGB streamem (2. 7. 2026)
- [x] Panel Debug output, panel Sensors, Dock UX bez duplikátů a plovoucí okna (7. 7. 2026)
- [x] `DocumentBase` / `ToolBase` + `ViewLocator`, `IMUDocument` s kompasem a horizontem (10. 7. 2026)
- [x] Dokumenty GPS, motory, kamera (RGB/hloubka) na událostech `MeasurementArived` (25. 7. 2026)
- [x] Zapojení dokumentů do panelu Sensors (`CreateSensorDocument`) (30. 7. 2026)

[Views/README.md](../Src/ARBot/Views/README.md), [rozhodnutí 25. 7. 2026 (backpressure UI, ReopenTool, DebugOutputTool)](decisions.md) · DevLog [2026-06-30](devlog.md#2026-06-30), [2026-07-02](devlog.md#2026-07-02), [2026-07-07](devlog.md#2026-07-07), [2026-07-10](devlog.md#2026-07-10), [2026-07-25](devlog.md#2026-07-25), [2026-07-30](devlog.md#2026-07-30)

<a id="nast-system-zprav-record-replay"></a>
### ✅ Systém zpráv, řídicí smyčka a záznam / přehrávání běhu (Run / View)

`nast-system-zprav-record-replay` · záměr · **hotovo** · nalezeno 24. 7. 2026 · vyřešeno 28. 7. 2026

Páteř aplikace — pipeline `MessageSource` / `MessageTarget` s rolemi a odbočkami, řízení jako periodický uzel nad schedulerem (jede i při výpadku měření), jeden `Stream` v `ARBotRuntime` s režimy Run a View. Každá zpráva má verzi, obraz se zaznamenává jako `ImageMsg` bez komprese (~1,8 GB/min ze dvou D435, na NVMe hodiny), záznam je best-effort s indexem, přehrávání umí seek a navigaci po záznamech, UI dokumenty se aktualizují vzorem „latest-wins". Režim Simulate (přepočet nad záznamem) se vědomě odložil s háčkem `T_in` / `T_out` v záznamu. Při tom se našlo, že `.gitignore` tiše ignoroval zdrojovou složku `Logs/` s celým modelem zpráv. Záznamy z robota jsou od té doby hlavní pracovní nástroj projektu.

- [x] Zárodek systému zpráv a řídicí smyčka (24. 7. 2026)
- [x] Serializace `ImageMsg` / `CameraFrame`, verzování zpráv, backpressure UI, příkazy Run/View (27. 7. 2026)
- [x] `ARBotRuntime` Run/View, `ControlLoop` nad schedulerem, `SeekTo`, `ReplayNavTool` (28. 7. 2026)
- [x] Oprava `.gitignore` (zdrojový `Logs/` nebyl v gitu) (28. 7. 2026)

[record-replay.md](record-replay.md), [architecture.md](architecture.md), [rozhodnutí 25. 7. 2026](decisions.md) · DevLog [2026-07-24](devlog.md#2026-07-24), [2026-07-27](devlog.md#2026-07-27), [2026-07-28](devlog.md#2026-07-28)

<a id="nast-selftest-harness"></a>
### ✅ Bezobslužný self-test pro reprodukovatelné měření výkonu

`nast-selftest-harness` · záměr · **hotovo** · nalezeno 1. 8. 2026 · vyřešeno 1. 8. 2026

Při honbě za GC pauzami se každé měření dělalo ručně a výsledky mezi běhy nešly srovnat. Parametr `selftest=true` nechá aplikaci samu otevřít zadaná okna, pustit Run, po `st_seconds` zastavit, zapsat souhrn z CSV do `logs/selftest-result.txt` a skončit; varianty (`st_record`, `st_images`, `no_uart`) dávají A/B měření bez obsluhy. Týž den se jím změřilo pět variant po 20 s a doložilo, že periodické záseky zmizely. Umí i snímek obrazovky a krátké video pro ilustrace do DevLogu.

- [x] Harness `selftest=true` + varianty a souhrn z CSV (1. 8. 2026)
- [x] Diag počítadla UI v souhrnu (bitmapy, rendery) (1. 8. 2026)
- [x] Screenshot a video (`st_shot`, `st_video`, GIF/mp4 přes ffmpeg s fallbackem) (1. 8. 2026)

[selftest.md](selftest.md) · DevLog [2026-08-01](devlog.md#2026-08-01)

<a id="nast-world-view"></a>
### ✅ World (geo) pohled nad mapovým podkladem

`nast-world-view` · záměr · **hotovo** · nalezeno 6. 8. 2026 · vyřešeno 14. 8. 2026

Vedle robot-centrického pohledu vznikl geografický pohled nad podkladem (Mapsui, OSM online nebo offline MBTiles, na ARM výchozí bez podkladu) s vrstvami z proudu zpráv: poloha a kurz, stopa, trasa a graf, síť z mapy jako pásy proměnné šířky (`MapMsg`, šířka v uzlu), lokální mapa a plán. Při zprovoznění se našly tři vady: vrstva lokální mapy se nekreslila (špatný styl vrstvy), robot byl otočený o 180° a značka robota se stopou se kreslily ze surového GPS, kdežto plán z fúzované pózy - rozestup byl přesně chyba fixu. Od 14. 8. jde všechno z jednoho rámce a surové fixy jsou samostatná vypínatelná vrstva.

- [x] Dokument s Mapsui, podklady, vrstvy, export výřezu do MBTiles (6. 8. 2026)
- [x] Síť z OsmNav jako `MapMsg` a šířka cesty v uzlu (7. 8. 2026)
- [x] Mapa se publikuje z runtime, robot kreslený správně otočený (13. 8. 2026)
- [x] Jeden rámec pro všechna lokální data, vrstva Surové GPS, oprava stylu vrstvy Lokální mapa (14. 8. 2026)

[world-view.md](world-view.md), [rozhodnutí 4. 8. 2026 (Mapsui)](decisions.md) · DevLog [2026-08-06](devlog.md#2026-08-06), [2026-08-07](devlog.md#2026-08-07), [2026-08-13](devlog.md#2026-08-13), [2026-08-14](devlog.md#2026-08-14)

<a id="nast-cisty-klon-build"></a>
### ✅ Repo nešlo postavit na čistém počítači a skript o tom lhal

`nast-cisty-klon-build` · vada · **hotovo** · nalezeno 12. 8. 2026 · vyřešeno 13. 8. 2026

Na novém stroji chyběla nativní knihovna i složka `RealSense 2.0/` (nebyla v gitu, protože ji chytala ignorovací pravidla pro `x64/`), a `build_all.bat` skončil hláškou „HOTOVO", ačkoli nepostavil nic - CMake nebyl v cestě a záporný návratový kód WSL propadl jako úspěch. RealSense DLL jsou od 12. 8. v gitu (bez 200 MB symbolů), skript si VS najde přes `vswhere`, ARM část s vysvětlením přeskočí a souhrn říká pravdu.

- [x] RealSense DLL do gitu, negace v `.gitignore` (12. 8. 2026)
- [x] Oprava `build_all.bat` (vswhere, pravdivý souhrn, porovnání s nulou) (13. 8. 2026)

[build-and-platforms.md](build-and-platforms.md) · DevLog [2026-08-12](devlog.md#2026-08-12), [2026-08-13](devlog.md#2026-08-13)

<a id="nast-virtualni-hw"></a>
### ✅ Virtuální hardware - simulace kamer, motorů, GPS a IMU

`nast-virtualni-hw` · záměr · **hotovo** · nalezeno 12. 8. 2026 · vyřešeno 19. 8. 2026

Vývoj vizuální cesty bez kamer: `VirtualCamera` renderuje RGB i hloubku z načtené OSM mapy a pózy robota, tutéž projekci použije k renderu i vrátí navigaci, takže neshoda v hloubkové cestě je skutečná chyba, ne artefakt. Den nato přibyly virtuální motory (přesná inverze odometrie), GPS a IMU nad modelem `SimulatedRobot` - uzavřená smyčka přes skutečnou fúzi dává chybu polohy ~0,2 m. Šev `ARBotHW` byl zprvu jednosměrný (po simulaci se skutečné kamery už nevrátily); od 14. 8. je režim `HwMode` volitelný v menu a po startu neběží žádný HW. Od té doby na simulaci stojí většina měření v projektu.

- [x] `VirtualCamera`, `RoadScene`, renderer, round-trip test (12. 8. 2026)
- [x] Virtuální motory, GPS, IMU, `start=` (13. 8. 2026)
- [x] `HwMode` (None/Real/Virtual) a čistý šev v obou směrech (14. 8. 2026)
- [x] Běh aplikace se simulací ověřen (korelace s mapou) (19. 8. 2026)

[virtual-hw.md](virtual-hw.md) · DevLog [2026-08-12](devlog.md#2026-08-12), [2026-08-13](devlog.md#2026-08-13), [2026-08-14](devlog.md#2026-08-14), [2026-08-19](devlog.md#2026-08-19)

<a id="nast-trace-do-zaznamu"></a>
### ✅ Ladicí výstup teče do záznamu jako zpráva Info

`nast-trace-do-zaznamu` · záměr · **hotovo** · nalezeno 14. 8. 2026 · vyřešeno 5. 9. 2026

Aby šlo pustit skutečnou aplikaci na robotu a hlášky si přečíst z nahrávky místo opisování z okna, napojily se `Trace.Listeners` na zprávu `Info` (verze 2 s časem, oblastí a úrovní); filtruje se až při čtení. Test hned odhalil smyčku log → zpráva → odběratel → log, kterou drží jen tvrdý strop `MaxPerSecond`, a to, že `.gitignore` tiše ignoroval složku testů. Na tomhle mostu dnes stojí čtení poruch ze záznamů ze zařízení (`ARBot.Analyze log`); strop 200/s se později ukázal jako past pro horkou cestu a řeší ho `PoruchaHlasic`.

- [x] `Info` verze 2, `TraceInfoBridge`, `TraceLogContext` (14. 8. 2026)
- [x] Razítka z `TimeBase` místo systémových hodin (4. 9. 2026)
- [x] Čtení hlášek ze záznamu ze zařízení (`ARBot.Analyze log`) (5. 9. 2026)

[record-replay.md](record-replay.md) · DevLog [2026-08-14](devlog.md#2026-08-14), [2026-09-04](devlog.md#2026-09-04), [2026-09-05](devlog.md#2026-09-05)

<a id="nast-replay-krokovani-a-panely"></a>
### ✅ Ladění nad záznamem — krok po témž proudu, hodnota pixelu, správné panely kamer

`nast-replay-krokovani-a-panely` · záměr · **hotovo** · nalezeno 16. 8. 2026 · vyřešeno 17. 8. 2026

Replay panel se zhustil do jednoho řádku a umí skok na předchozí či následující zprávu téhož proudu, takže krokování drží jednu kameru. Obrazový dokument ukazuje hodnotu pixelu pod kurzorem v podkladu i overlayi naráz. Opravila se vada, kdy overlay nad pravou kamerou ukazoval sjízdnost levé a panely se přiřazovaly podle pořadí příchodu; replay panel se nově dokuje k Debug outputu, aby nepřekrýval obrázky.

- [x] Kompaktní Replay a skok po témž proudu (16. 8. 2026)
- [x] Hodnota pixelu pod kurzorem (16. 8. 2026)
- [x] Overlay a panel se párují podle jména kamery (16. 8. 2026)
- [x] Replay panel dokovaný k Debug outputu (17. 8. 2026)

[record-replay.md](record-replay.md), [Views/README.md](../Src/ARBot/Views/README.md) · DevLog [2026-08-16](devlog.md#2026-08-16), [2026-08-17](devlog.md#2026-08-17)

<a id="nast-snimek-a-video-okna"></a>
### ✅ Snímek obrazovky a videozáznam okna z toolbaru

`nast-snimek-a-video-okna` · záměr · **hotovo** · nalezeno 16. 8. 2026 · vyřešeno 16. 8. 2026

Snímky a videa do dokumentace šly dosud pořídit jen self-testem z příkazové řádky, tedy s ukončením aplikace. Pod menu přibyl pruh Snímek / MP4 / GIF s průběžným kódováním do běžícího ffmpegu (konstantní paměť, snímky se při nestíhání zahazují) a limity s auto-stopem. Ověřeno na Windows; na Armbianu by bylo nutné nastavit `ARBOT_FFMPEG`.

[screen-capture.md](screen-capture.md) · DevLog [2026-08-16](devlog.md#2026-08-16)

<a id="nast-synteticka-mapa-koridor"></a>
### ✅ Syntetická testovací mapa s koridorem a zúžením

`nast-synteticka-mapa-koridor` · záměr · **hotovo** · nalezeno 16. 8. 2026 · vyřešeno 16. 8. 2026

Mapa `OSM/SyntetickyKoridor.osm` s pravoúhlými rohy, zúžením na 1 m, nálevkou zpět na 3 m a T křižovatkou pro zkoušky průjezdu a odbočení v simulaci. Šířka je v OsmNav vlastností uzlu, ne úseku, takže se musela zadat tagem na každém uzlu a rohům přidat pomocné uzly — jinak by zúžení vůbec nevzniklo. Souřadnice jsou vyrobené přesnou inverzí převodu, který používá aplikace; při ověřování se našla chyba délek hran (viz WGS84).

[OSM/SyntetickyKoridor.osm](../OSM/SyntetickyKoridor.osm), [virtual-hw.md](virtual-hw.md) · DevLog [2026-08-16](devlog.md#2026-08-16)

<a id="nast-telemetricky-pohled"></a>
### ✅ Telemetrický pohled — údaje ze zpráv srovnané v čase a graf

`nast-telemetricky-pohled` · záměr · **hotovo** · nalezeno 17. 8. 2026 · vyřešeno 18. 8. 2026

Tabulka nad indexem záznamu, kde řádek je zpráva a sloupec údaj (póza, řídicí zásah, navigace, senzory), obousměrně svázaná s přehráváním, s výběrem sloupců, filtrem řádků a grafem vybraných řad kresleným vlastním controlem. Cesta příkaz → skutečnost se tak dá poprvé číst v jedné tabulce. Cestou se srovnaly směrové údaje (uloženo matematicky, azimut až při zobrazení) a doplnil chybějící HDOP ve virtuální GPS. Autor UI proklikal 18. 8.; režim Run zůstává nedělaný.

- [x] Fáze 1 — jádro v `ARBot.Common/Telemetry`, tabulka, synchronizace s přehráváním (17. 8. 2026)
- [x] Fáze 2 — graf řad, lupa, odečítátko, přehazování sloupců (17. 8. 2026)
- [x] Senzorové zprávy (motory, IMU, GPS) a odometrická rychlost v registru (18. 8. 2026)
- [x] Sjednocené směrové údaje a přepínač Azimut (18. 8. 2026)
- [x] Ruční proklikání UI autorem (18. 8. 2026)

[telemetry-view.md](telemetry-view.md), [plan-telemetry-view.md](plan-telemetry-view.md), [rozhodnutí 17. 8. 2026 (vlastní graf místo OxyPlotu)](decisions.md) · DevLog [2026-08-17](devlog.md#2026-08-17), [2026-08-18](devlog.md#2026-08-18), [2026-08-19](devlog.md#2026-08-19)

<a id="nast-world-tooltipy-navigace"></a>
### ✅ Tooltipy všech tří úrovní navigace ve World pohledu

`nast-world-tooltipy-navigace` · záměr · **hotovo** · nalezeno 17. 8. 2026 · vyřešeno 17. 8. 2026

Lokální plán byl v mapě jen modrá čára a parametry, které ji určily, nešly zjistit. Najetím na úsek plánu, hranu trasy nebo pás cesty v síti se teď ukáže popis (rychlosti, tolerance, druh hrany, šířka, stav globální navigace). Zároveň se opravilo pořadí a šířky vrstev — plán se kreslil pod trasou a mizel. Známá mezera: uzavřené a penalizované hrany se dál kreslí šedě jako zbytek grafu, rozlišuje je jen tooltip.

[world-view.md](world-view.md), [record-replay.md (zprávy s víc producenty)](record-replay.md) · DevLog [2026-08-17](devlog.md#2026-08-17)

<a id="nast-virtualni-robot-rampa-kol"></a>
### ✅ Virtuální robot zmrazil zatáčení při saturaci kol

`nast-virtualni-robot-rampa-kol` · vada · **hotovo** · nalezeno 18. 8. 2026 · vyřešeno 18. 8. 2026

Při požadavku ±30 °/s nebyly směrnice kurzu symetrické; fúze, odometrie i sklon spolu souhlasily, takže chyba byla v tom, že kola příkaz nevykonala. Simulace omezovala zrychlení každého kola zvlášť, a když byla saturovaná obě, rozdíl rychlostí (a tím rotace) zamrzl. Nově rampuje dopřednou rychlost a rozdíl zvlášť a při saturaci ustupuje dopředná — jako skutečný řadič; přibyl i strop rychlosti kola, který simulace neměla.

[virtual-hw.md](virtual-hw.md) · DevLog [2026-08-18](devlog.md#2026-08-18)

<a id="nast-simulace-meri-lokalizaci"></a>
### ✅ Simulace konečně umí změřit lokalizaci a nechat odhad driftovat

`nast-simulace-meri-lokalizaci` · záměr · **hotovo** · nalezeno 19. 8. 2026 · vyřešeno 22. 8. 2026

Virtuální kamera renderovala z odhadu fúze, takže chyba odhadu posunula i obraz a nebyla vidět; model pohybu byl ideální a IMU hlásilo absolutní kurz s bílým šumem, takže odhad nikam nedriftoval a případ, který má hranová lokalizace léčit, v simulaci nevznikal. Bezobslužné běhy navíc měřily stojící robot, protože cíl šel zadat jen klikem v mapě. Kamery od 22. 8. renderují ze skutečné pózy (`camerapose=truth`), simulace umí prokluz kol (`wheelslip=`) a bias kurzu a gyra (`imubias=`), skutečná póza jde do záznamu jako `GroundTruthMsg` se stejným razítkem jako odhad, cíl jde zadat z příkazové řádky (`goal=lat,lon`) a panel Virtuální senzory ukazuje živou chybu lokalizace. Prokluz je ověřený za jízdy (enkodéry 17,89 m proti skutečným 17,71 m). Začalo to 19. 8. vnucenou chybou pózy v renderu (`poseerror=`) a 21. 8. druhou mapou pro kamery (`visionmap=`): chyba je v datech, ne v pozorovateli. Tuze posunutá dvojnice mapy (24. 8.) patří k rovné testovací mapě.

- [x] `poseerror=` — umělá chyba pózy v renderu virtuální kamery (19. 8. 2026)
- [x] `camerapose=fusion|truth` a dva testy (konvergence korekce, robot na cestě při špatné mapě) (22. 8. 2026)
- [x] Prokluz kol, bias IMU, `GroundTruthMsg` v záznamu, `camerapose=truth` jako výchozí (22. 8. 2026)
- [x] `goal=lat,lon` — první měření za jízdy, prokluz ověřen za běhu (22. 8. 2026)
- [x] Šum z příkazové řádky `imunoise=` / `gpsnoise=` pro bezobslužné A/B (22. 8. 2026)
- [x] `visionmap=` — druhá mapa pro kamery, vrstva Mapa (vize) ve World pohledu (21. 8. 2026)

[virtual-hw.md](virtual-hw.md), [rozhodnutí 22. 8. 2026](decisions.md), [SimulatedRobot.cs](../Src/ARBot.Common/Simulation/SimulatedRobot.cs), [GroundTruthMsg.cs](../Src/ARBot.Common/Logs/GroundTruthMsg.cs) · DevLog [2026-08-19](devlog.md#2026-08-19), [2026-08-20](devlog.md#2026-08-20), [2026-08-21](devlog.md#2026-08-21), [2026-08-22](devlog.md#2026-08-22), [2026-08-24](devlog.md#2026-08-24), [2026-08-31](devlog.md#2026-08-31)

<a id="nast-cesty-relativne-ke-korenu-repa"></a>
### ✅ Cesty k mapám byly absolutní a vázané na jeden stroj

`nast-cesty-relativne-ke-korenu-repa` · vada · **hotovo** · nalezeno 21. 8. 2026 · vyřešeno 21. 8. 2026

Profily spouštění nesly absolutní cesty z jiného stroje a relativní `map=` se řešila proti pracovnímu adresáři, takže virtuální HW na jiné kopii repa tiše nevznikl. Relativní cesta k mapě se teď řeší proti kořenu repozitáře jako u `logs/` a `records/`, a hláška říká, která mapa přesně chybí. Hledání kořene repa zůstalo ve čtyřech kopiích jako dluh.

[virtual-hw.md](virtual-hw.md) · DevLog [2026-08-21](devlog.md#2026-08-21)

<a id="nast-stop-start-senzoru"></a>
### ✅ Zastavení a spuštění jednotlivého senzoru z panelu

`nast-stop-start-senzoru` · záměr · **hotovo** · nalezeno 21. 8. 2026 · vyřešeno 21. 8. 2026

Senzor nešlo zastavit vůbec — vyzvednutí měření ho skrytě spouštělo, takže ho UI nebo runtime do jednoho tiku zapnuly zpátky. To skryté spuštění bylo redundantní (každý senzor startuje v konstruktoru) a zrušilo se; řádek panelu senzorů má tlačítko Stop/Start a motory před zastavením dostanou nulovou rychlost. Vypnutí nepřežije start runtime. Logika ověřena headless; tlačítko za běhu podle autora funguje (17. 9. 2026).

[Views/README.md](../Src/ARBot/Views/README.md) · DevLog [2026-08-21](devlog.md#2026-08-21)

<a id="nast-hranice-cesty-v-ui"></a>
### ✅ Hranice cesty a proložené přímky vidět v Obrázcích a v mapě

`nast-hranice-cesty-v-ui` · záměr · **hotovo** · nalezeno 22. 8. 2026 · vyřešeno 23. 8. 2026

Autor tomu koridoru „pořádně nerozuměl, nedokázal si to představit". Detekované hranice se proto kreslí jako overlay nad barevným snímkem (jen když je vrstva vybraná) a jako vrstva „Hranice cesty" ve World pohledu; od 23. 8. nese `RoadCorridorMsg` v4 i obě proložené přímky jako úsečky, plněné ještě před jakoukoli kontrolou, takže u zamítnutých cyklů je vidět proč (příčná hrana křižovatky místo okraje). Cestou se opravily tři vady zobrazení: overlay napoprvé nic nekreslil, ve World byla vidět jen jedna kamera (`Clear()` mazal druhou) a prázdná vrstva neměla vysvětlení — teď rámeček říká, kolik bodů z kolika kamer a jestli proložení vůbec běží (`corridor=false`). Výpadky metrických bodů (18–36 % sloupců) jsou vidět jako čára a počet v popisce.

- [x] Overlay v Obrázcích a vrstva ve World pohledu (22. 8. 2026)
- [x] Proložené přímky ve zprávě (`RoadCorridorMsg` v4) a v mapě, rámeček s vysvětlením (23. 8. 2026)
- [x] Výpadky bodů viditelné a počítané, oprava zobrazení druhé kamery (23. 8. 2026)

[Views/README.md](../Src/ARBot/Views/README.md), [world-view.md](world-view.md), [map-correlation-localization.md](map-correlation-localization.md) · DevLog [2026-08-22](devlog.md#2026-08-22), [2026-08-23](devlog.md#2026-08-23)

<a id="nast-analyzatory-v-repozitari"></a>
### ✅ Analyzátory záznamů do repozitáře (`ARBot.Analyze`)

`nast-analyzatory-v-repozitari` · záměr · **hotovo** · nalezeno 23. 8. 2026 · vyřešeno 24. 8. 2026

Předchozí sezení psalo měřicí nástroje ve scratchpadu a do DevLogu zapsalo „recept", jak je postavit znovu — přiznání, že pravidlo „vše v repozitáři" platí i na měřidla. Vznikl projekt `ARBot.Analyze` (`corridor`, `dump`, `types`) a `RecordFile`, který drží pasti čtení záznamu (čte se přes index, katalog musí `CameraFrame` doregistrovat). Hned další den přibyly `corridorfit` (koridor přepočítaný ze zaznamenaných bodů, `--synth` proti pravdě), `edgebias` (odchylky hranových bodů proti známému okraji) a `grid` (polární grid tak, jak ho vidělo UI). Od té doby je to hlavní měřidlo projektu a příkazů je přes dvacet.

- [x] Projekt v řešení, `corridor` / `dump` / `types`, `RecordFile` (23. 8. 2026)
- [x] `corridorfit`, `edgebias`, `grid` (24. 8. 2026)

[record-replay.md](record-replay.md), [ARBot.Analyze](../Src/ARBot.Analyze) · DevLog [2026-08-23](devlog.md#2026-08-23), [2026-08-24](devlog.md#2026-08-24)

<a id="nast-hranice-poza-kazdeho-snimku"></a>
### ✅ Hranice se kreslily jednou pózou pro obě kamery — chyba až 2 m

`nast-hranice-poza-kazdeho-snimku` · vada · **hotovo** · nalezeno 23. 8. 2026 · vyřešeno 23. 8. 2026

Vrstva promítala všechny body „poslední známou" pózou, ačkoli snímky obou kamer jsou až 400 ms od sebe; rozdíl kurzu p90 3,2° dělal na dosahu 8 m chybu kreslení p50 0,15 m, max 2 m. Navíc promítala ground truth, kdežto occupancy grid se plní odhadem — vrstvy se nemohly krýt ani principiálně. Po seeku se zprávy podle razítka spárovat nedají (rekonstrukce stavu dodá jednu zprávu na klíč), takže póza musí cestovat ve zprávě: `CameraFrame` v6 a `RoadCorridorMsg` v5 nesou pózu v okamžiku pořízení, kamery ji stampují lambdou `EstimatedPoseAt` (vždy odhad z fúze, jiná než renderovací `camerapose=`). Ověřeno testy; pozdější záznamy verzi 6 běžně nesou.

- [x] Póza v metadatech snímku a koridoru (`CameraFrame` v6, `RoadCorridorMsg` v5) (23. 8. 2026)
- [x] World vrstva promítá per snímek, přepínač „Hranice ze skutečné pózy" (23. 8. 2026)

[record-replay.md](record-replay.md), [virtual-hw.md](virtual-hw.md), [world-view.md](world-view.md) · DevLog [2026-08-23](devlog.md#2026-08-23)

<a id="nast-kamery-simulace-sum-71-procent"></a>
### ✅ Virtuální kamery jely 6,8 Hz místo 30, protože 71 % času generovaly šum

`nast-kamery-simulace-sum-71-procent` · vada · **hotovo** · nalezeno 23. 8. 2026 · vyřešeno 23. 8. 2026

Jeden snímek stál 93 ms, z toho 66 ms šum: `DeterministicNoise.Gaussian` počítal Box–Mullera ze dvou hashů (38 ns na vzorek) a barevný šum se volal třikrát na pixel. Nahrazeno kvantilovou tabulkou (jeden hash + čtení z pole, 4096 položek kvůli L1): 7 ns na vzorek, snímek 51 ms, kamery 10 Hz; na hranové lokalizaci `NoPair` 20 → 1 a chyba polohy 0,046 → 0,036 m. Starší záznamy mají jinou realizaci téhož šumu. Ani 10 Hz ale není 30 — render bez šumu stojí 27 ms a paralelizace po řádcích by na Orange Pi brala výkon řídicí smyčce; vědomě neřešeno.

- [x] Kvantilová tabulka místo Box–Mullera (`Gaussian` 38 → 7 ns) (23. 8. 2026)

[virtual-hw.md](virtual-hw.md) · DevLog [2026-08-23](devlog.md#2026-08-23)

<a id="nast-rovna-testovaci-mapa"></a>
### ✅ Delší rovná testovací mapa se známou pravdou

`nast-rovna-testovaci-mapa` · záměr · **hotovo** · nalezeno 23. 8. 2026 · vyřešeno 24. 8. 2026

`SyntetickyKoridor.osm` je ze ~40 % křižovatka a slepý konec, a navíc má nálevku, takže se statistika koridoru počítala i tam, kde koridor existovat nemá — záznam `20260822-100403` je tím vychýlený benchmark. `OSM/SyntetickyRovny.osm` je jeden rovný úsek 160 m konstantní šířky 2,0 m: 921 přijatých z 962 cyklů, prvních 60 s bez jediného zamítnutí, nerovnoběžnost p50 0,086°. Dvojnice `SyntetickyRovnyPosunuty.osm` je tuhá translace (+0,60 m východ, −0,40 m sever), takže korelace s mapou má poprvé jednu správnou odpověď (uzavírá otevřený bod z 20. 8.). Mapy hlídá test (rovnost, šířka i mezi uzly, délka, posun stejným vektorem). Past: robot startuje ve středu obálky uzlů, takže z mapy dlouhé L je ve směru jízdy jen L/2.

- [x] `SyntetickyRovny.osm` 160 m × 2 m a profily v `launchSettings.json` (24. 8. 2026)
- [x] `SyntetickyRovnyPosunuty.osm` jako tuhá translace pro `visionmap=` (24. 8. 2026)
- [x] Testy `SyntetickeMapyTests` hlídají vlastnosti map (24. 8. 2026)
- [x] Gate rovnoběžnosti prošetřen a ponechán, násypka zapsána jako vlastnost staré mapy (24. 8. 2026)

[map-correlation-localization.md](map-correlation-localization.md), [SyntetickyRovny.osm](../OSM/SyntetickyRovny.osm), [SyntetickeMapyTests.cs](../Src/ARBot.Common.Tests/OsmNav.Tests/SyntetickeMapyTests.cs) · DevLog [2026-08-23](devlog.md#2026-08-23), [2026-08-24](devlog.md#2026-08-24), [2026-08-25](devlog.md#2026-08-25), [2026-09-03](devlog.md#2026-09-03)

<a id="nast-simulace-trava-render"></a>
### ✅ Tráva v simulaci se renderovala špatně — šev na hranici, mrtvé parametry scény, tráva bez výšky

`nast-simulace-trava-render` · vada · **hotovo** · nalezeno 23. 8. 2026 · vyřešeno 24. 8. 2026

Tři vady renderu virtuálních kamer, které zkreslovaly měření koridoru. Drsnost trávy rozdvojila roviny vozovky a trávy, takže paprsky mířící na hranici netrefily ani jednu a podél celého okraje běžela čára bez hloubky (23 % sloupců bez bodu); správná fyzika je svislá stěna trávy na okraji — po opravě 96,7 % sloupců s bodem a chyba polohy p50 0,151 → 0,055 m. Pak se ukázalo, že `grassheight=` / `grassrough=` / `depthnoise=` z UI i z příkazové řádky neměly žádný účinek: `VirtualHWOptions.Scene` měla vlastní výchozí instanci, takže se renderovalo z jiné scény, než do které se hodnoty zapisovaly — tři hypotézy z úvahy padly, rozhodlo až měření výstupu běžící aplikace (`ARBot.Analyze grid`). A barevný render protínal jen rovinu vozovky, takže vyvýšená tráva nezakrývala cestu za sebou; teď jde stejnou cestou jako hloubka (rychlá cesta při nulové trávě zůstává).

- [x] Svislá stěna trávy na rozhraní cesty a trávy (23. 8. 2026)
- [x] Parametry scény se skutečně uplatňují (`VirtualHWOptions.Scene`), výpis s čím se renderuje (24. 8. 2026)
- [x] `RenderColor` přes obě roviny, tráva zakrývá cestu; opravená hláška „dokonalá rovina“ (24. 8. 2026)
- [x] Doměřen vliv šumu scény (drsnost trávy dominuje, šum hloubky téměř nic) (24. 8. 2026)

[virtual-hw.md](virtual-hw.md), [grass-traversability.png](media/grass-traversability.png) · DevLog [2026-08-23](devlog.md#2026-08-23), [2026-08-24](devlog.md#2026-08-24)

<a id="nast-konfigurace-parametru"></a>
### ✅ Konfigurace aplikace — registr parametrů, profily ze souboru a panel

`nast-konfigurace-parametru` · záměr · **hotovo** · nalezeno 31. 8. 2026 · vyřešeno 1. 9. 2026

Aplikace se dosud nastavovala výhradně z příkazové řádky a klíč parametru nikde neexistoval jako věc: nešlo vypsat, co lze nastavit, a překlep tiše propadl na výchozí hodnotu. Vznikl registr parametrů s typem a popisem, profily `klíč=hodnota` (`config=cesta`) s precedencí default → profil → příkazová řádka, evidence původu hodnoty a panel *Tools → Konfigurace* s validací, rozbalovacími seznamy u výčtů a uložením profilu. Neznámý klíč nebo neplatná hodnota v profilu je chyba při startu. Panel autor proklikal (našel při tom vadu ztrácející hodnoty při scrollu tabulky). Na zařízení profily zprvu nefungovaly (chyběly soubory v build výstupu, 1. 9.); od 4. 9. se parametry čtou typovanými odkazy a od 5. 9. jde účinná konfigurace do záznamu.

- [x] Registr, profil, precedence, panel, validace hodnot, chyby v polích jako standard (31. 8. 2026)
- [x] Vada ztrácející hodnoty při recyklaci řádků `DataGrid` (dva typy řádků) (31. 8. 2026)
- [x] Tlačítko *Uložit a restartovat* ověřeno autorem; profily fungují na zařízení (1. 9. 2026)
- [x] Typované odkazy `ParamRegistry.X.Value` místo `Program.GetParam*` (4. 9. 2026)
- [x] Účinná konfigurace a verze binárky do záznamu (5. 9. 2026)

[configuration.md](configuration.md), [plan-configuration.md](plan-configuration.md), [Views/README.md](../Src/ARBot/Views/README.md) · DevLog [2026-08-31](devlog.md#2026-08-31), [2026-09-01](devlog.md#2026-09-01), [2026-09-04](devlog.md#2026-09-04), [2026-09-05](devlog.md#2026-09-05)

<a id="nast-panely-senzoru"></a>
### ✅ Údaje v panelech senzorů poskakovaly a špatně se četly

`nast-panely-senzoru` · vada · **hotovo** · nalezeno 31. 8. 2026 · vyřešeno 31. 8. 2026

Víc hodnot v jednom textovém bloku: když se změnil počet znaků (číslo snímku přeteče o řád, frekvence z 0,8 na 30,0), posunulo se všechno za tím údajem. Každá hodnota má teď vlastní buňku pevné šířky a řádek „Snímek" (číslo, Hz, čas) kreslí sdílený control na pevné souřadnice pro všechny čtyři senzory; motoru ten řádek chyběl úplně. Po zpětné vazbě z běhu se opravily dvě další pasti layoutu (control uvnitř tabulky, `Auto` sloupce s proměnlivým textem). Podle autora ověřeno za běhu (17. 9. 2026) — panely i čísla v overlayi kamery sedí.

- [x] Vlastní buňky pevné šířky, sdílený `SensorFrameInfoControl`, řádek „Snímek" u motoru (31. 8. 2026)
- [x] Ověřeno za běhu (sdělení autora) (17. 9. 2026)

[Views/README.md](../Src/ARBot/Views/README.md) · DevLog [2026-08-31](devlog.md#2026-08-31)

<a id="nast-world-mapa-nahled"></a>
### ✅ Mapa načtená z panelu World měla jinou šířku cest než síť, po které robot jede

`nast-world-mapa-nahled` · vada · **hotovo** · nalezeno 1. 9. 2026 · vyřešeno 1. 9. 2026

Mapa se do aplikace dostávala dvěma cestami — runtime přes `map=` a tlačítko v panelu World, které si soubor parsovalo samo s šířkou cest natvrdo 2 m proti výchozím 3 m runtime. Týž soubor tak vypadal v panelu jinak než síť, podle které robot jede. Autor upřesnil smysl tlačítka („vidět, co robot dostane, dřív než to dostane"), proto je načtená mapa samostatnou náhledovou vrstvou vedle navigační sítě, čte `roadwidth=` a použitou šířku píše do stavového řádku. Panel autor proklikal.

- [x] Náhledová vrstva, šířka z registru, přeuspořádaný panel (1. 9. 2026)

[world-view.md](world-view.md) · DevLog [2026-09-01](devlog.md#2026-09-01)

<a id="nast-analyze-katalog-zaznamu"></a>
### ✅ Analyzátor záznamů nikdy nepřečetl motorová data — katalog zpráv se rozešel

`nast-analyze-katalog-zaznamu` · vada · **hotovo** · nalezeno 2. 9. 2026 · vyřešeno 2. 9. 2026

Při hledání napětí baterie hlásil `ARBot.Analyze` nula motorových rámců, přestože index jich ukazoval přes osm tisíc; málem z toho vznikl závěr, že motor v tom běhu nejel. Nástroj si katalog zpráv skládal sám a chyběl mu `MotorStateBase` — chybějící typ se projeví jako zprávy, které „neexistují", ne jako chyba. Táž past kousla už 25. 8. u GPS. Léčba je společný `MessageCatalog.RecordDefaults()` pro oba konzumenty a test, který reflexí kontroluje každou zprávu z `ARBot.Common`.

- [x] Společný katalog `RecordDefaults()` a test `RecordCatalogTests` (2. 9. 2026)

[record-replay.md](record-replay.md) · DevLog [2026-09-02](devlog.md#2026-09-02)

<a id="nast-poskozeny-zaznam-index"></a>
### ✅ Záznam useknutý vybitou baterií nešel otevřít — index se opraví z dat

`nast-poskozeny-zaznam-index` · vada · **hotovo** · nalezeno 3. 9. 2026 · vyřešeno 3. 9. 2026

Dva záznamy z večera 2. 9. skončily uprostřed, když došla baterie; data přežila až na poslední snímek, ale sidecar index u jednoho ukazoval za konec dat a u druhého o 1 409 zpráv zaostal, takže View spadl na konci streamu. Index se teď načítá tolerantně, ověří proti datům a zbytek se dopočítá skenem hlaviček; opravený se zapíše na disk a původní zůstane jako `.idx.bad`. Napojeno do View i do analyzátoru, oba záznamy se otevřou.

- [x] Tolerantní načtení indexu a oprava skenem dat (3. 9. 2026)

[record-replay.md](record-replay.md) · DevLog [2026-09-03](devlog.md#2026-09-03)

<a id="nast-world-rychlostni-profil"></a>
### ✅ Plán v pohledu World obarvený rychlostí a graf rychlost vs. vzdálenost

`nast-world-rychlostni-profil` · záměr · **hotovo** · nalezeno 3. 9. 2026 · vyřešeno 3. 9. 2026

Tooltip nad pohybujícími se úseky plánu byl k ničemu, proto se úseky kreslí barvou podle stropu rychlosti a vpravo dole je graf rychlostního profilu podél dráhy, se společnou škálou. Model je v Common s testy, ověřeno snímkem ze self-testu. Graf hned ukázal, že robot jede trojnásobkem stropu prvního uzlu — z toho vzešel nález o neúčinné rychlostní obálce.

- [x] Model `PlanSpeedProfile`, kreslení a škála `SpeedPalette` (3. 9. 2026)

[world-view.md](world-view.md) · DevLog [2026-09-03](devlog.md#2026-09-03)

<a id="nast-timebase-sjednoceni"></a>
### ✅ Čtyři místa míchala dvě časové základny

`nast-timebase-sjednoceni` · vada · **hotovo** · nalezeno 4. 9. 2026 · vyřešeno 4. 9. 2026

Na pokyn autora „ať celá aplikace měří stejně" se prošlo, kde se čte čas: latence snímku v `CameraFrameProcessor`, hodiny `PerfMsg`, hodiny zpráv `Info` v záznamu a start mise Robotour braly `DateTime.Now` proti razítkům z `TimeBase`, který záměrně nesleduje skoky NTP a je bez offsetu zóny. Všechno šlo do záznamu nebo diagnostiky, takže posun o dvě hodiny nevypadal jako chyba, jen jako nesmyslné číslo. Pravidlo je v CLAUDE.md; audit 15. 9. dohledal ještě razítka T265 a D435 z hodin kamery a přidal strážný test.

- [x] Čtyři místa na `TimeBase.Now`, pravidlo v CLAUDE.md (4. 9. 2026)
- [x] Audit: razítka kamer ukotvená na `TimeBase`, `CasZTimeBaseTests` (15. 9. 2026)

[rozhodnutí 4. 9. 2026](decisions.md), [CLAUDE.md](../CLAUDE.md) · DevLog [2026-09-04](devlog.md#2026-09-04), [2026-09-15](devlog.md#2026-09-15)

<a id="nast-labelbox-klic-v-notebooku"></a>
### ✅ V notebooku ležel natvrdo API klíč s platností do roku 2042

`nast-labelbox-klic-v-notebooku` · vada · **hotovo** · nalezeno 7. 9. 2026 · vyřešeno 7. 9. 2026

Pravidlo „vše v repozitáři" přihlašovací údaje nevyjímalo, a v trénovacím notebooku byl LabelBox klíč s platností do roku 2042. Zachránilo to jen to, že soubor ještě nebyl commitnutý. Klíč autor zneplatnil a vydal nový s krátkou platností a menším oprávněním; notebook ho čte z Colab Secrets / proměnné prostředí a při chybějícím klíči padá hláškou, ne tichým `None`. Do `CLAUDE.md` přibyla výslovná výjimka z pravidla.

- [x] Klíč zneplatnit, vydat nový (4 týdny, Project lead) (7. 9. 2026)
- [x] Čtení z prostředí bez tichého fallbacku, výjimka v `CLAUDE.md` (7. 9. 2026)

[CLAUDE.md](../CLAUDE.md) · DevLog [2026-09-07](devlog.md#2026-09-07)

<a id="nast-virtualni-magnetometr"></a>
### ✅ Virtuální magnetometr s vnuceným železem

`nast-virtualni-magnetometr` · záměr · **hotovo** · nalezeno 8. 9. 2026 · vyřešeno 8. 9. 2026

Do té doby `VirtualImu` neposílalo ani magnetické pole, ani zrychlení, takže kalibrační mise v simulaci nedělala vůbec nic. Teď simulace umí tvrdé i plné měkké železo, šum, náklon a „otáčení robotem rukou" (motory to vyvolat nejde — mise zahodí regulátor). Nejcennější je test od začátku do konce: do simulace se vloží známé železo a mise musí vrátit právě ta čísla — jediná kontrola, která chytí záměnu rámců nebo obrácenou inverzi. Neověřuje to železo skutečného robota, jen náš řetěz.

- [x] Pole a gravitace z pózy, vnucené železo, `VirtualMagCalControl`, ovládání v panelu (8. 9. 2026)
- [x] Test „vložené železo = vrácené železo" a běh `mission=magcal` v simulaci (8. 9. 2026)

[virtual-hw.md](virtual-hw.md) · DevLog [2026-09-08](devlog.md#2026-09-08), [2026-09-11](devlog.md#2026-09-11)

<a id="nast-cfg-preklep-tiche-ignorovani"></a>
### ✅ Překlep `cfg=` místo `config=` shodil celý běh a vypadal jako vada motorů

`nast-cfg-preklep-tiche-ignorovani` · vada · **hotovo** · nalezeno 12. 9. 2026 · vyřešeno 12. 9. 2026

Neznámý klíč se jen s varováním ignoroval (mezi argumenty jsou i cizí přepínače), nenačetl se profil, bez mapy se nezaložil virtuální HW, nebyly motory a stránka hlásila, že nouzové zastavení nejde stisknout. Klíč podobný známému parametru (překlep nebo zkratka) je teď chyba při startu s návrhem správného jména; stisk virtuálního stopu bez virtuálního HW vrací 409 místo úspěchu. Ověřeno skutečným během headless v simulaci. Zapsala se i past v pořadí kroků: nová mise si hlídá stisk až od svého startu, takže stop se má držet, dokud stránka nenapíše „připravena k odjezdu".

- [x] Podobný neznámý klíč je chyba s návrhem (12. 9. 2026)
- [x] `POST /virtualestop` bez virtuálního HW vrací 409 (12. 9. 2026)

[configuration.md](configuration.md), [headless.md](headless.md) · DevLog [2026-09-12](devlog.md#2026-09-12)

<a id="nast-mapa-ve-view-jednou"></a>
### ✅ Ve View se ztrácela mapa ze záznamu a lokální vrstvy plavaly

`nast-mapa-ve-view-jednou` · vada · **hotovo** · nalezeno 14. 9. 2026 · vyřešeno 14. 9. 2026

Při přehrávání záznamu se nezobrazila mapa, trasa se pohybovala a značka cíle neseděla na zónu — tři příznaky jedné příčiny: `MapMsg` je v záznamu jen jednou (při startu) a kdo si World pohled otevřel až za ní, mapu neviděl; bez mapy spadne geografický počátek na nouzový dopočet z GPS fixu, který se s každým fixem posouvá. Nová vrstva zón vadu zviditelnila, protože jako jediná kreslí ze zeměpisných souřadnic. Mapa se teď odchytává z přehrávaného streamu i ve View a pohled říká nahlas, když jede na nouzovém počátku. Testem to pokrýt nešlo (zakládal by singleton runtime); scénář proklikal autor a je v pořádku (17. 9. 2026).

- [x] Odchyt `MapMsg` ve View, varování „BEZ MAPY“ v pohledu (14. 9. 2026)
- [x] Scénář proklikán autorem (otevřít záznam, pak World pohled) (17. 9. 2026)

[world-view.md](world-view.md), [record-replay.md](record-replay.md) · DevLog [2026-09-14](devlog.md#2026-09-14)

<a id="nast-zony-world-pohled"></a>
### ✅ Dojezdové zóny vidět i ve World pohledu aplikace

`nast-zony-world-pohled` · záměr · **hotovo** · nalezeno 14. 9. 2026 · vyřešeno 14. 9. 2026

Půdorys stránky náhledu kreslil zóny od 12. 9., v Avalonii vidět nebyly, protože ta logika seděla uvnitř webového náhledu. Výběr zón je teď ve sdíleném `GoalZones` (výstup v LLA, převod si dělá každý pohled sám) a World pohled má vrstvu „Zóny“. Dvě věci našlo až spuštění: Mapsui vrstvu nepřekresloval bez `DataHasChanged()`, a kružnice o poloměru 3 m má při běžném zoomu 3 pixely, takže vypadalo, že se zóny nekreslí vůbec — ke kružnici patří značka ve středu. Ověřeno v běžící aplikaci v simulaci.

- [x] `GoalZones` sdílený mezi webem a World pohledem, vrstva Zóny, značka ve středu (14. 9. 2026)

[world-view.md](world-view.md), [snímek z aplikace](media/world-view-zony-20260914.png) · DevLog [2026-09-14](devlog.md#2026-09-14)

## Web a dokumentace

<a id="web-arbot-cz-github-pages"></a>
### ⬜ Web arbot.cz převeden z Google Sites na GitHub Pages

`web-arbot-cz-github-pages` · záměr · **otevřeno** · nalezeno 13. 9. 2026

Autor zvolil GitHub Pages: statické HTML bez frameworku s relativními odkazy, sdílená sazba, web natrvalo tmavý. Přeneseno je vše ze Sites — sedm stránek, vzorce (doplněné z autorova LaTeXu, sázené MathJaxem), čtyři karusely fotek (30 snímků schovaných jako pozadí skrytých divů), tři schémata podvozku překreslená jako inline SVG (přebarvování rastrů byla slepá ulička), 22 odkazů v tabulce umístění, 11 článků z let 2009–2017 s obrázky a videi, ročníky 2024–2025 a nová úvodní stránka s fotkou a čtyřmi čísly. Podle autora Pages běží na `alesruda.github.io/ARBot3/`; doména `arbot.cz` přepnutá není a do té doby se publikace Sites nesmí zrušit. Web má 18 stránek s ručně opsanou hlavičkou — při první změně menu je to osmnáct souborů (vědomý dluh, generátor menu je jiná práce). 17. 9. se složka přejmenovala z `docs/` na `web/` (vedle `doc/` to byla past) a publikace přešla na workflow GitHub Actions, protože režim „z větve“ jiné jméno než `/docs` neumí — v nastavení repozitáře se to musí přepnout ve stejnou chvíli jako push, jinak web spadne.

- [x] Převod sedmi stránek + prezentace do repa (13. 9. 2026)
- [x] Vzorce z LaTeXu, karusely, tmavý web, SVG schémata podvozku, vztah (14) (14. 9. 2026)
- [x] Ročníky 2024 a 2025 v tabulce umístění (15. 9. 2026)
- [x] Odkazy v tabulce umístění, 11 článků přenesených ze Sites, chronologický seznam, nová úvodní stránka (16. 9. 2026)
- [x] Zapnout GitHub Pages (autor; web běží na github.io) (17. 9. 2026)
- [ ] Přepnout doménu `arbot.cz` (DNS, `CNAME`) a teprve pak zrušit publikaci Google Sites
- [ ] Dohledat starý blogový článek „Tuhnutí MD23“ (2012) a dva mrtvé odkazy (Wayback)
- [ ] Generátor hlavičky a menu (18 ručně opsaných stránek)
- [x] Přejmenování `docs/` → `web/`, oprava odkazů, workflow `pages.yml` (17. 9. 2026)
- [ ] Přepnout Settings → Pages → Source na GitHub Actions (autor, ve chvíli pushe)
- [x] Stránka „Čím si projekt prošel“ generovaná z registru úkolů (`doc/ukoly.yaml`) (17. 9. 2026)

[web/README.md](../web/README.md), [index.html](../web/index.html), [plan-ukoly.md](plan-ukoly.md) · DevLog [2026-09-13](devlog.md#2026-09-13), [2026-09-14](devlog.md#2026-09-14), [2026-09-15](devlog.md#2026-09-15), [2026-09-16](devlog.md#2026-09-16), [2026-09-17](devlog.md#2026-09-17)

<a id="web-dokumentace-devlog"></a>
### ✅ Dokumentace v repozitáři — rozcestník `CLAUDE.md`, doménové `doc/*.md` a DevLog

`web-dokumentace-devlog` · záměr · **hotovo** · nalezeno 10. 7. 2026 · vyřešeno 30. 7. 2026

Poznatky se od začátku vedou výhradně v repozitáři: `CLAUDE.md` jako rozcestník s pravidly, doménové dokumenty v `doc/` (fúze, rámce, platformy, UI) a od 30. 7. denní DevLog, jehož začátek se zpětně zrekonstruoval z gitu a z časů změn souborů (proto jsou záznamy do 9. 8. hrubší). Rozhodnutí se vedou v `decisions.md`. Vzor pro všechno další dokumentování projektu včetně tohoto registru.

- [x] `CLAUDE.md` rozcestník + doménové `doc/*.md` + `Views/README.md` (10. 7. 2026)
- [x] Zavedení DevLogu a zpětná rekonstrukce od 23. 6. (30. 7. 2026)

[devlog.md](devlog.md), [decisions.md](decisions.md) · DevLog [2026-07-10](devlog.md#2026-07-10), [2026-07-30](devlog.md#2026-07-30)

<a id="web-popularizacni-stranka"></a>
### ✅ Popularizační stránka o softwaru robota

`web-popularizacni-stranka` · záměr · **hotovo** · nalezeno 13. 9. 2026 · vyřešeno 13. 9. 2026

Stránka pro laika a středoškoláka: řídicí řetězec jako čtyři otázky, které si robot 10×/s odpovídá (co je kolem mě → kde jsem → kudy → jak jet), mise, ovládání z telefonu, simulace a záznam. Čtyři ručně kreslené SVG schémata, čísla výhradně z měření vedených v repu (88,2 % proti 78,0 % u rozpoznání cesty, 2,7 ms na NPU, kurz 24° → 3° po kalibraci) a sekce, která přiznává, co je ověřené jen v simulaci. K tomu podklad pro Google Sites (text blok po bloku, schémata do PNG přes headless Chrome); stránka se pak přestěhovala do webu.

- [x] Stránka + podklad pro Google Sites (13. 9. 2026)

[prezentace.html](../web/pages/prezentace.html), [prezentace-google-sites.md](prezentace-google-sites.md) · DevLog [2026-09-13](devlog.md#2026-09-13)

<a id="web-dokumentace-konzistence"></a>
### ✅ Kód, komentáře a dokumentace si odporovaly — hlídač odkazů a opravy

`web-dokumentace-konzistence` · vada · **hotovo** · nalezeno 15. 9. 2026 · vyřešeno 15. 9. 2026

Na podnět autora („uhlídat, aby si to odpovídalo, není jednoduché“) se strojově ověřilo, co šlo: přesun runtime do vlastního projektu nechal mrtvé odkazy v šesti doménových dokumentech a jedenáct dní si jich nikdo nevšiml; `CLAUDE.md` si odporovala sama se sebou o třicet řádků (systemd jednotka „neexistuje“ a o pár odrážek níž je popsaná); konfigurace tvrdila 62 parametrů, registr jich má 85. Opraveno a přibyl test, že každý odkaz v živé dokumentaci vede na existující soubor (DevLog a plány se schválně nehlídají — jsou to záznamy historie). Že komentář popisuje to, co kód dělá, strojově neověří nic; na tom stály dva omyly téhož dne.

- [x] `DokumentaceOdkazyTests`, opravy rozporů v `CLAUDE.md` a `doc/*.md` (15. 9. 2026)

[configuration.md](configuration.md) · DevLog [2026-09-15](devlog.md#2026-09-15)

<a id="web-clanek-regulator"></a>
### ✅ Článek o regulátoru sledování dráhy a skupina „Technické články“ na webu

`web-clanek-regulator` · záměr · **hotovo** · nalezeno 18. 9. 2026 · vyřešeno 18. 9. 2026

Třetí technický článek ve stylu *Modelu podvozku* a *Detekce kraje vozovky*: odvození poloměru oblouku vepsaného do rohu z tolerance uzlu, strop rychlosti z limitu otáčení, brzdná obálka ze zpětného průchodu, exekuce každých 100 ms — a jako pointa západka, kdy omezovač `v ≤ d/(k·T_rot)` dostával `d = max(d_min, τ·v)`, tedy veličinu odvozenou z vlastního výstupu; ukázáno, že větev `τ·v > d_min` je nesplnitelná a soustava se sesune na podlahu 0,048 m/s, což je přesně to, co se 14. 8. 2026 naměřilo na robotu. Tři ručně psaná inline SVG, souřadnice dopočítané skriptem z týchž vzorců, které stránka odvozuje. Čísla jsou přepočítaná z `Profile.cs` (v_max 1,2 m/s, a 0,5 m/s²), ne opsaná z `path-following.md`, kde zůstaly starší hodnoty 0,8 a 0,2 — proto vychází zlom mezi limitem otáčení a `v_max` na 32° a náběh rotace na 3,2°, ne na 40° a 8°. Zároveň se na přání autora přestalo menu prodlužovat s každým článkem: *Model podvozku* a *Detekce kraje vozovky* z lišty zmizely a nahradil je rozcestník *Technické články*. Rozbalovací podmenu se zamítlo — lišta by rostla donekonečna, dropdown chce vlastní CSS a na dotykovém displeji se hover chová hůř. Druhý tehdejší důvod („menu je natvrdo ve 22 místech, takže dropdown = 22 editací u každého článku“) padl ještě týž den, viz `web-menu-generator`; rozhodnutí na něm ale nestálo a platí dál.

- [x] Stránka `regulator-sledovani-drahy.html` (3 SVG, vzorce 1–11) (18. 9. 2026)
- [x] Rozcestník `technicke-clanky.html`, menu přepsané ve 21 HTML + generátoru (18. 9. 2026)

[regulator-sledovani-drahy.html](../web/pages/regulator-sledovani-drahy.html), [technicke-clanky.html](../web/pages/technicke-clanky.html), [path-following.md](path-following.md) · DevLog [2026-09-18](devlog.md#2026-09-18)

<a id="web-menu-generator"></a>
### ✅ Menu webu opsané ve 22 místech — generátor `tools/menu.cs`

`web-menu-generator` · záměr · **hotovo** · nalezeno 18. 9. 2026 · vyřešeno 18. 9. 2026

Z dotazu autora „přišlo mi komplikované dávat menu na 22 míst a bude jich více“. Hlavička `<header class="sitehead">` byla opsaná v každé stránce zvlášť — 21 HTML plus kopie v `tools/ukoly.cs` — a rostlo to s každou novou stránkou; `web/README.md` to vedlo jako vědomý dluh od 16. 9. Nový nástroj drží menu na jednom místě a přepisuje ten blok ve všech `web/**/*.html`; CI job `generovane-soubory` pustí `ukoly.cs`, pak `menu.cs` a porovná výsledek s commitem, takže ručně upravená hlavička i zapomenuté spuštění spadnou. Pořadí je povinné — `ukoly.cs` vypíše prázdnou hlavičku a naplní ji až `menu.cs`. Zamítnuty Jekyll (`_layouts`) i skládání stránek až ve workflow: obojí by porušilo to hlavní, co o složce `web/` platí — že je přesně tím, co se publikuje, takže se web dá prohlédnout lokálně a rozbitý výstup se pozná před pushem, ne až na živém webu. Ověření, že převod nic nezměnil, je silné: **první běh generátoru nepřepsal ani jeden z 21 souborů**, tedy vyrobil bajt po bajtu totéž, co tam bylo ručně. Navíc zmizel ruční `class="on"` — stránka bez vlastní položky v menu je zapsaná ve skupině (články o soutěžích, technické články) a zvýraznění vyrábí generátor. Stránka, kterou nástroj nezná, je chyba (kód 1, nepřepíše nic): jinak by na ní nebylo zvýrazněné nic.

- [x] `tools/menu.cs`, menu vyňaté z `tools/ukoly.cs`, CI job `generovane-soubory` (18. 9. 2026)
- [x] Ověřeno — první běh beze změny, změna položky propadne do 21 stránek, neznámá stránka spadne (18. 9. 2026)

[web/README.md](../web/README.md), [menu.cs](../tools/menu.cs) · DevLog [2026-09-18](devlog.md#2026-09-18)


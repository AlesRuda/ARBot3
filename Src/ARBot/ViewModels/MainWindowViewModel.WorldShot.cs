using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Projections;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Bezobsluzne poriz screenshot <b>World pohledu</b> do <c>doc/media/world-view.png</c> (pro denicek).
    /// Zapne se parametrem <c>worldshot=true</c>. Otevre World, nakrmi ho syntetickou trajektorii + polohou
    /// (aby byl videt tvar robota a stopa nad realnou mapou), pocka na dlazdice OSM, hluboko priblizi na
    /// robota a ulozi PNG hlavniho okna. Pak aplikaci ukonci. Slouzi k reprodukovatelnemu porizeni obrazku
    /// featury bez rucni obsluhy (obdoba self-testu <see cref="SelfTestConfig"/>, ale bez HW/Run).
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>Spusti porizeni screenshotu World pohledu, je-li vyzadano parametrem <c>worldshot=true</c>.</summary>
        private void StartWorldShotIfRequested()
        {
            if (!Program.GetParamBool("worldshot", false)) return;
            _ = RunWorldShotAsync();   // fire-and-forget; sam se ukonci
        }

        private async Task RunWorldShotAsync()
        {
            try
            {
                await Task.Delay(1500);   // nech UI ustalit

                WorldViewDocument? doc = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    OpenWorldView();
                    doc = _factory.DocumentDock?.VisibleDockables?
                        .FirstOrDefault(d => d.Id == "WorldView") as WorldViewDocument;
                });
                if (doc == null) return;

                // Zachyti hlavni okno do doc/media pod danym nazvem (kazdy snimek vlastni soubor).
                async Task CaptureWorld(string name)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        string path = Path.Combine(SelfTest.MediaDir(), name);
                        if (App.MainTopLevel is Visual v && ScreenCapture.SavePng(v, path))
                            System.Diagnostics.Debug.WriteLine("WorldShot: " + path);
                        else
                            System.Diagnostics.Debug.WriteLine("WorldShot: screenshot se nezdaril (" + name + ")");
                    });
                }

                // Synteticka trajektorie + poloha (Praha) - postupne, aby se stopa naakumulovala
                // (Post koalescuje "latest-wins", takze mezi body je potreba nechat probehnout flush).
                double lat0 = 50.08758, lon0 = 14.42076;
                const double dLat = 0.00001, dLon = 0.00002;   // ~1,1 m sever + ~1,4 m vychod / krok
                const int steps = 12;
                for (int i = 0; i < steps; i++)
                {
                    double lat = lat0 + i * dLat, lon = lon0 + i * dLon;
                    await Dispatcher.UIThread.InvokeAsync(() => doc.Post(new GPSState
                    {
                        Latitude = lat,
                        Longitude = lon,
                        Quality = GPSState.FixQuality.GpsFix,
                        NumberOfSatellites = 11,
                        Hdop = 0.8,
                    }));
                    await Task.Delay(40);   // nech probehnout Background flush (append do stopy)
                }
                // Kurz podel stopy (matematicky uhel: 0 = vychod, +CCW).
                await Dispatcher.UIThread.InvokeAsync(() => doc.Post(new RobotStateMsg
                {
                    Theta = Math.Atan2(dLat, dLon),
                    V = 0.6,
                }));

                await Task.Delay(3500);   // nech nacist dlazdice OSM

                // --- Snimek 1: obecny World pohled (robot jako metricky tvar), jeste BEZ OsmNav mapy. ---
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var (mx, my) = SphericalMercator.FromLonLat(lon0 + (steps - 1) * dLon, lat0 + (steps - 1) * dLat);
                    doc.Map.Navigator.CenterOnAndZoomTo(new MPoint(mx, my), 0.05);   // videt metricky tvar robota
                });
                await Task.Delay(2500);
                await CaptureWorld("world-view.png");

                // --- Snimek 2: sit z OsmNav se sirkami cest (vrstva "Mapa (sit)"). ---
                string osmPath = Path.Combine(Path.GetTempPath(), "arbot-worldshot.osm");
                await File.WriteAllTextAsync(osmPath, SampleOsm);
                await Dispatcher.UIThread.InvokeAsync(() => doc.LoadOsmMapAsync(osmPath));
                await Task.Delay(3000);   // nech sestavit + vykreslit sit
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var (mx, my) = SphericalMercator.FromLonLat(lon0 + (steps - 1) * dLon, lat0 + (steps - 1) * dLat);
                    doc.Map.Navigator.CenterOnAndZoomTo(new MPoint(mx, my), 0.10);   // cela sit + promenna sirka
                });
                await Task.Delay(3000);
                await CaptureWorld("world-view-road-width.png");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WorldShot chyba: " + ex);
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(0));
            }
        }

        // Mala vzorova OSM sit (pesi cesty) u synteticke polohy robota (Praha) - jen pro worldshot.
        private const string SampleOsm = @"<?xml version='1.0' encoding='UTF-8'?>
<osm version='0.6'>
  <node id='1' lat='50.08745' lon='14.42060'/>
  <node id='2' lat='50.08745' lon='14.42120'/>
  <node id='3' lat='50.08785' lon='14.42120'/>
  <node id='4' lat='50.08785' lon='14.42060'/>
  <node id='5' lat='50.08765' lon='14.42090'/>
  <way id='101'>
    <nd ref='1'/><nd ref='2'/><nd ref='3'/><nd ref='4'/><nd ref='1'/>
    <tag k='highway' v='footway'/>
    <tag k='width' v='6'/>
  </way>
  <way id='102'>
    <nd ref='1'/><nd ref='5'/><nd ref='3'/>
    <tag k='highway' v='footway'/>
  </way>
  <way id='103'>
    <nd ref='4'/><nd ref='5'/><nd ref='2'/>
    <tag k='highway' v='footway'/>
  </way>
</osm>";
    }
}

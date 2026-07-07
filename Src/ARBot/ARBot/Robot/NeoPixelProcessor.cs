using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.HAL;

namespace ARBot.Robot
{
    /// <summary>
    /// Rizeni adresovatelneho LED pasku (NeoPixel / WS2812) robota.
    /// Pasek ma 36 LED rozdelenych do sekci: predni svetla (16 LED) a zadni blikace (2x5 LED).
    /// Po zavolani <see cref="StartTask"/> bezi na pozadi smycka, ktera podle stavovych
    /// priznaku (blikace, brzdy, couvani, nouzove zastaveni, rezim prednich svetel)
    /// pocita barvy vsech pixelu a periodicky je posila do <see cref="INeoPixelDriver"/>.
    /// </summary>
    public class NeoPixelProcessor
    {
        /// <summary>Aktualni barvy vsech 36 LED (framebuffer posilany do driveru).</summary>
        Color[] pixels = new Color[36];

        /// <summary>Dvojice barev pro jeden krok animace prednich svetel v rezimu Alert.</summary>
        public class Info
        {
            /// <summary>Barva prvni skupiny LED.</summary>
            public Color Col1;
            /// <summary>Barva druhe skupiny LED.</summary>
            public Color Col2;
        }

        /// <summary>Index prvni LED prednich svetel (16 LED: FrontStart..FrontStart+15).</summary>
        private int FrontStart = 0;
        /// <summary>Index prvni LED praveho blikace (5 LED).</summary>
        private int RightBlinkerStart = 16;
        /// <summary>Index prvni LED leveho blikace (5 LED).</summary>
        private int LeftBlinkerStart = 21;

        /// <summary>Barva blikacu - oranzova.</summary>
        private Color BlinkerColor = new Color(0xff, 0x80, 0);
        /// <summary>Barva brzdovych svetel - cervena.</summary>
        private Color BreakColor = new Color(0xff, 0, 0);
        /// <summary>Barva svetel pri couvani - bila.</summary>
        private Color BackwardColor = new Color(0xff, 0xff, 0xff);
        /// <summary>Cervena slozka prednich svetel (napr. Knight Rider / Alert).</summary>
        private Color FrontRedColor = new Color(0xff, 0, 0);
        /// <summary>Modra slozka prednich svetel (napr. Alert).</summary>
        private Color FrontBlueColor = new Color(0, 0, 0xff);
        /// <summary>Zhasnuta LED - cerna.</summary>
        private Color DarkColor = new Color(0, 0, 0);

        /// <summary>Rezimy zobrazeni prednich svetel.</summary>
        public enum FrontLightsEnum
        {
            /// <summary>Efekt "Knight Rider" - cerveny bod prejizdejici sem a tam s dosvitem.</summary>
            KnightRider,
            /// <summary>Vystrazny (policejni) rezim - stridani cervene a modre.</summary>
            Alert,
            /// <summary>Predni svetla zhasnuta.</summary>
            Off,
            /// <summary>Testovaci rezim.</summary>
            Test
        }
        /// <summary>
        /// Levy blinker
        /// </summary>
        public volatile bool LeftBlinker;
        /// <summary>
        /// Pravy blinker
        /// </summary>
        public volatile bool RightBlinker;
        /// <summary>
        /// Brzdy
        /// </summary>
        public volatile bool Break;
        /// <summary>
        /// Couvani
        /// </summary>
        public volatile bool Backward;
        /// <summary>
        /// Zobrazuje nouzove zastaveni
        /// </summary>
        public volatile bool EmergencyStop;
        /// <summary>
        /// Predni zobrazeni
        /// </summary>
        private FrontLightsEnum frontLights;

        /// <summary>
        /// Predni zobrazeni
        /// </summary>
        public FrontLightsEnum FrontLights
        {
            get
            {
                return frontLights;
            }
            set
            {
                frontLights = value;
            }
        }

        /// <summary>
        /// Zastavuje task na pozadi
        /// </summary>
        public volatile bool CancelTask;
        /// <summary>
        /// Task pracuje
        /// </summary>
        public volatile bool IsBusy;

        /// <summary>Hardwarovy driver, ktery preposila barvy pixelu na LED pasek.</summary>
        INeoPixelDriver driver;

        /// <summary>
        /// Vytvori procesor nad zadanym driverem LED pasku.
        /// </summary>
        /// <param name="driver">Driver pro odeslani barev na fyzicky pasek.</param>
        public NeoPixelProcessor(INeoPixelDriver driver)
        {
            this.driver = driver;
        }

        /// <summary>
        /// Urci barvu jedne LED zadniho svetla (blikac + brzda/couvani) pro dany krok animace.
        /// Priorita: nouzove zastaveni > efekt bezici stopy blikace > brzda/couvani/zhasnuto.
        /// </summary>
        /// <param name="idx">Globalni citac kroku animace (pro strídani pri EmergencyStop).</param>
        /// <param name="val">Faze bezici stopy blikace (0..9); mimo aktivni okno LED sviti oranzove.</param>
        /// <param name="lightIdx">Poradi LED v ramci petice zadniho svetla (0..4).</param>
        /// <returns>Vyslednou barvu LED.</returns>
        private Color RearColor(int idx, int val, int lightIdx)
        {
            // Nouzove zastaveni: strídani cervene a modre po sousednich LED (efekt "majaku").
            if(EmergencyStop)
                return ((lightIdx+idx) % 2) == 0 ? BreakColor : FrontBlueColor;
            // Zakladni barva LED: pri couvani bila (vnitrni LED), pri brzdeni cervena (vnejsi LED), jinak zhasnuto.
            Color c = lightIdx < 3 && Backward ? BackwardColor : (lightIdx > 2 && Break ? BreakColor : DarkColor);
            // Blikac: dokud faze "val" neprejela pres tuto LED, sviti oranzove; jinak zakladni barva (bezici stopa).
            return lightIdx >= val || lightIdx < val-5 ? BlinkerColor : c;
        }

        /// <summary>
        /// Nastavi vychozi stav svetel a spusti animacni smycku na pozadi (Task).
        /// Smycka bezi, dokud neni nastaven <see cref="CancelTask"/>; po dobu behu je <see cref="IsBusy"/> true.
        /// </summary>
        public void StartTask()
        {
            // Vychozi stav - vse zhasnuto/vypnuto, predni svetla v rezimu Knight Rider.
            LeftBlinker = false;
            RightBlinker = false;
            Break = false;
            Backward = false;
            FrontLights = FrontLightsEnum.KnightRider;
            IsBusy = true;
            CancelTask = false;

            // Predpocitana tabulka kroku animace prednich svetel v rezimu Alert (24 kroku).
            // Kazdy krok urcuje barvy dvou skupin LED (Col1/Col2) - vznika stridavy cerveno-modry efekt.
            Info[] alertColors = new Info[]
            {
                new Info() {Col1=FrontRedColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=FrontRedColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=FrontBlueColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=FrontBlueColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=FrontBlueColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=FrontBlueColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=FrontRedColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=FrontRedColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor},
                new Info() {Col1=DarkColor, Col2=DarkColor}
            };

            // Pocatecni vyplneni celeho pasku modrou (nez smycka prepocita jednotlive sekce).
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = FrontBlueColor;

            Task.Run(() =>
                {
                    int idx=0;                  // globalni citac kroku (pro efekt majaku pri EmergencyStop)
                    int leftBlinkerVal = 0;     // faze animace leveho blikace (0..9)
                    int rightBlinkerVal = 0;    // faze animace praveho blikace (0..9)
                    int frontKRVal = 0;         // pozice bodu Knight Rider (-14..15)
                    int frontKRDiv = 0;         // delic rychlosti Knight Rider
                    int blinkerDIV = 0;         // delic rychlosti blikacu (posun faze kazdy 3. tik)
                    int frontAlertVal = 0;      // index kroku v tabulce alertColors


                    while (!CancelTask)
                    {
                        // Pokud blikac nesviti, "zaparkujeme" fazi na 5 -> RearColor da zakladni barvu (bez bezici stopy).
                        if (!LeftBlinker)
                            leftBlinkerVal = 5;

                        if (!RightBlinker)
                            rightBlinkerVal = 5;

                        // Pravy blikac - 5 LED v obracenem poradi (lightIdx 4..0).
                        pixels[RightBlinkerStart] = RearColor(idx, rightBlinkerVal, 4);
                        pixels[RightBlinkerStart + 1] = RearColor(idx, rightBlinkerVal, 3);
                        pixels[RightBlinkerStart + 2] = RearColor(idx, rightBlinkerVal, 2);
                        pixels[RightBlinkerStart + 3] = RearColor(idx, rightBlinkerVal, 1);
                        pixels[RightBlinkerStart + 4] = RearColor(idx, rightBlinkerVal, 0);

                        // Levy blikac - 5 LED v prirozenem poradi (lightIdx 0..4).
                        pixels[LeftBlinkerStart] = RearColor(idx, leftBlinkerVal, 0);
                        pixels[LeftBlinkerStart + 1] = RearColor(idx, leftBlinkerVal, 1);
                        pixels[LeftBlinkerStart + 2] = RearColor(idx, leftBlinkerVal, 2);
                        pixels[LeftBlinkerStart + 3] = RearColor(idx, leftBlinkerVal, 3);
                        pixels[LeftBlinkerStart + 4] = RearColor(idx, leftBlinkerVal, 4);


                        // Posun faze blikacu jen kazdy 3. tik smycky (zpomaleni animace).
                        if (blinkerDIV == 0)
                        {
                            idx++;
                            leftBlinkerVal++;
                            if (leftBlinkerVal >= 10)
                                leftBlinkerVal = 0;

                            rightBlinkerVal++;
                            if (rightBlinkerVal >= 10)
                                rightBlinkerVal = 0;
                            blinkerDIV = 2;
                        }
                        else
                            blinkerDIV--;


                        // --- Predni svetla dle zvoleneho rezimu ---

                        // Off: vsech 16 prednich LED zhasnuto.
                        if (FrontLights == FrontLightsEnum.Off)
                        {
                            for (int i = 0; i < 16; i++)
                                pixels[FrontStart + i] = DarkColor;
                        }

                        // KnightRider: jedna LED sviti cervene, ostatni pozvolna zhasinaji (nasobeni jasu 0.5 kazdy tik).
                        if (FrontLights == FrontLightsEnum.KnightRider)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                Color c=pixels[FrontStart + i];
                                if (c == null)
                                    c = new Color(0, 0, 0);
                                // Aktivni pozice (|frontKRVal|) cervena; ostatni ztlumene o polovinu -> dosvit.
                                pixels[FrontStart + i] = Math.Abs(frontKRVal) == i ? new Color(0xff, 0, 0) : new Color((byte)(c.R*0.5), (byte)(c.G*0.5), (byte)(c.B*0.5));
                            }

                            // Posun bodu; po dosazeni konce (16) skok na -14 -> bod se "odrazi" a jede zpet.
                            if (frontKRDiv == 0)
                            {
                                frontKRVal++;
                                if (frontKRVal == 16)
                                    frontKRVal = -14;
                                frontKRDiv = 1;
                            }
                            frontKRDiv--;
                        }
                        // Alert: predni LED se nastavi dle aktualniho kroku predpocitane tabulky alertColors.
                        if (FrontLights == FrontLightsEnum.Alert)
                        {
                            pixels[FrontStart + 0] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 1] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 2] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 3] = alertColors[frontAlertVal].Col2;
                            pixels[FrontStart + 4] = alertColors[frontAlertVal].Col2;
                            pixels[FrontStart + 5] = alertColors[frontAlertVal].Col2;
                            pixels[FrontStart + 6] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 7] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 8] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 9] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 10] = alertColors[frontAlertVal].Col2;
                            pixels[FrontStart + 11] = alertColors[frontAlertVal].Col2;
                            pixels[FrontStart + 12] = alertColors[frontAlertVal].Col2;
                            pixels[FrontStart + 13] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 14] = alertColors[frontAlertVal].Col1;
                            pixels[FrontStart + 15] = alertColors[frontAlertVal].Col1;

                            // Posun na dalsi krok tabulky, na konci zpet od zacatku (smycka).
                            if(frontAlertVal==alertColors.Length-1)
                                frontAlertVal=0;
                            else
                                frontAlertVal++;
                        }

                        // Test: vsech 16 prednich LED zhasnuto (rezervovano pro ladeni).
                        if (FrontLights == FrontLightsEnum.Test)
                        {
                            for (int i = 0; i < 16; i++)
                            {
                                pixels[FrontStart + i] = new Color(0, 0, 0);
                            }
                            pixels[FrontStart + 2] = new Color(0, 0, 0);
                        }

                        // Odeslani sestaveneho framebufferu na pasek a pauza (~20 FPS).
                        driver.Send(pixels);
                        Thread.Sleep(50);
                    }
                    // Smycka skoncila (CancelTask) - task uz nepracuje.
                    IsBusy = false;
                });
        }
    }
}

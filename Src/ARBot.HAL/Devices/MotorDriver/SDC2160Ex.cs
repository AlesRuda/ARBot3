using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ARBot.HAL.Devices.MotorDrivers
{
    /// <summary>
    /// Implement Roboteq SDC2160 driver.
    /// Vyzaduje nahrany ridici program v motorove jednotce (MicroBasic skript nize).
    ///
    /// <para><b>Primarni zdroj skriptu je <c>Src/RoboRun/RizeniDiffPodvozku.mbs</c></b> — ten se
    /// nahrava do jednotky. Text nize je jen KOPIE pro cteni u driveru: menit se ma nejdriv
    /// <c>.mbs</c> a sem prenest totez (1. 10. 2026 se radek <c>T=</c> omylem dostal jen sem).</para>
    ///
    /// <para><b>POZOR - skript nize NENI kompilovany kod.</b> Je to zdroj programu, ktery bezi
    /// V MOTOROVE JEDNOTCE; do zarizeni se nahrava zvlast (Roborun+ / MicroBasic upload). Zmena
    /// tady sama o sobe chovani robota NEZMENI, dokud se skript do jednotky nenahraje - a protoze
    /// jde o cestu nouzoveho zastaveni, je nutne ji po nahrani OVERIT NA ZARIZENI.</para>
    ///
    /// <para><b>Cas jednotky (radek <c>T=</c>, od 1. 10. 2026).</b> Skript posila pred kazdym
    /// blokem telemetrie svuj citac v ms (modulo <see cref="DeviceTimeModulus"/>). Driver z nej
    /// bere razitko vzorku i interval pro rychlost kol (<see cref="DeviceClock"/>). Razitko
    /// z casu prichodu neslo: Roboteq posila po USB CDC v davkach, razitka mela vzor 12 / 12 / 9 ms
    /// pri pravidelnem vzorkovani po 11 ms, rychlost <c>Δenkoder / Δrazitko</c> po kratkem
    /// intervalu hlasila o 33 % vic a fuze z toho nadsadila drahu o ~1,9 %
    /// (<c>lok-fuze-poza-pred-koly</c>, doc/ekf-fusion.md). Bez radku <c>T=</c> (stary skript
    /// v jednotce) driver jede po staru z casu prichodu; stary driver radek <c>T=</c> preskoci
    /// (ceka na <c>DI=</c>), takze novy skript jde nahrat i pod starou binarku.</para>
    /// </summary>
    /*

' var 1 - dopredna akcelerace v tisicinach max vykonu za s^2
' var 2 - rotacni akcelerace v tisicinach max vykonu za s^2
' var 3 - pozadovana dopredna rychlost v tisicinach max rychlosti
' var 4 - pozadovana rotacni rychlost v tisicinach max rychlosti, kladna hodnota je v matematickem smyslu
' var 5 - aktualni dopredna rychlost v miliontinach max rychlosti
' var 6 - aktualni rotacni rychlost v miliontinach max rychlosti, kladna hodnota je v matematickem smyslu
' var 7 - mark, pri jeho zmene se resetne timeout, po vyprseni timeoutu se robot zastavi
' var 8 - nepouzita (do 6. 10. 2026 navrzena jako samostatne bezne zpomaleni; zruseno - bezna jizda
'         ma JEDNU rampu var 1 pro rozjezd i brzdeni, jinak by zmena rychlosti nebyla symetricka)
' var 9 - dopredne zpomaleni pri NOUZOVEM zastaveni a watchdogu v tisicinach max rychlosti za s
'         (od verze 2.2; 0 = vychozi defEmDec). Zvlast proto, ze bezna jizda ma byt plynula
'         a odpovidat modelu planovace, kdezto nouzove zastaveni ma zastavit co nejdriv, co dovoli
'         trakce a naklad (doc/plan-drive-hold.md, registr hw-motor-rampa-jednotky).
'
' Jednotky zrychleni: tisiciny plneho rozsahu rychlosti za sekundu (plny rozsah = 1000 v var 3,
' tedy MaxTheoreticalSpeed hostitele, 2,16 m/s). Host je pocita MotorAcceleration.ToScriptUnits.
' Vychozi hodnoty plati, dokud host nic neposle (napr. po restartu jednotky) - skript tak
' NIKDY nebezi s nulovou rampou, ktera by zmrazila brzdeni.

Option Explicit

'timer 0 se pouziva pro mereni casu
'SetTimerCount(1, 0x7fffffff)
SetTimerCount(1, 12000)
'predchozi hodnota timeru
dim lastTimer as integer
'aktualni hodnota timeru
dim currentTimer as integer
'uplynuly cas tohoto vzorku
dim time as integer

dim acceleration as integer
dim rotacceleration as integer
dim reqSpeed as integer
dim reqRotSpeed as integer
dim curSpeed as integer
dim curRotSpeed as integer
dim di3 as integer
dim timeout as integer
dim lastMark as integer
dim mark as integer
'cas jednotky v ms pro razitko vzorku (radek T=), modulo 1000000000 (~11,6 dne);
'host ho prevadi na svuj cas (DeviceClock) a pocita z nej rychlost kol - viz SDC2160Ex
dim tick as integer
'od verze 2.2: nouzove zpomaleni, priznak nouze a rampa pro tento krok
dim emDeceleration as integer
dim emergency as integer
dim target as integer
dim rate as integer

'vychozi rampy, dokud host neposle vlastni (tisiciny max rychlosti za s pri plnem rozsahu 2,16 m/s):
'185 = 0,40 m/s2 (Profile.MaxAcceleration), 463 = 1,0 m/s2 (nouzove zastaveni - s tim robot
'fakticky jezdil do 5. 10. 2026, kdy byl prevod zrychleni chybne 2,6x strmejsi)
dim defAcc as integer
dim defEmDec as integer
defAcc=185
defEmDec=463

timeout=0
tick=0
currentTimer=GetTimerCount(1)

print("Version 2.2\r")

while true
	lastTimer=currentTimer
	currentTimer=GetTimerCount(1)
	'vypocet uplynuleho casu v ms
	time =lastTimer-currentTimer
	tick+=time
	if tick>=1000000000 then
		tick-=1000000000
	end if
	'pokud ma timer milou hodnotu tak ho restratnu
	if currentTimer<10000 then
		currentTimer=0x7fffffff
		SetTimerCount(1, currentTimer)
	end if

	mark=GetValue(_VAR, 7) 
	if mark<>lastMark then
		timeout=500
		lastMark=mark
	end if
	
	'print("time=", time, "\n")
	'print("timeout=", timeout, "\n")
	
	acceleration=GetValue(_VAR, 1)
	rotacceleration=GetValue(_VAR, 2)
	emDeceleration=GetValue(_VAR, 9)
	'nula (host jeste nic neposlal, restart jednotky) nesmi rampu zmrazit - vychozi hodnoty
	if acceleration<=0 then
		acceleration=defAcc
	end if
	if rotacceleration<=0 then
		rotacceleration=acceleration
	end if
	if emDeceleration<=0 then
		emDeceleration=defEmDec
	end if

	reqSpeed=GetValue(_VAR, 3)
	reqRotSpeed=GetValue(_VAR, 4)

	curSpeed=GetValue(_VAR, 5)
	curRotSpeed=GetValue(_VAR, 6)
	

	'zde osetrit emergency stop
	'pozadovana dopredna rychlost na nulu, pomale zpomaleni.
	'Rotaci nulujeme az kdyz robot skutecne stoji (curSpeed=0), aby bylo dobrzdeni RIZENE:
	'dokud se jeste jede, ma smysl drzet zatoceni podle regulatoru (jako kdyz se brzdi v zatacce);
	'jak robot stoji, rotaci nulujeme, aby se netocil na miste - a posledni odeslany prikaz je (0,0),
	'takze po uvolneni stopu nevznika zadny transient.
	'Predpoklad: acceleration > 0. Pri nule by rampa zamrzla a curSpeed by nuly nikdy nedosahl,
	'takze by se robot pod stopem vezl dal (a drzel posledni zatoceni); pri zaporne by dokonce
	'vyrazil na plnou opacnym smerem. Nulu od 2.2 nahradi vychozi hodnota (vyse), zapornou
	'hlida host - viz MotorAcceleration.ToScriptUnits.
	'Od 2.2 se pod stopem brzdi NOUZOVOU rampou (var 9), ne beznou.
	emergency=0
	di3=GetValue(_DI, 3)
	if di3=0 then
		emergency=1
		reqSpeed=0
		if curSpeed=0 then
			reqRotSpeed=0
		end if
	end if
	'PREDCHOZI VARIANTA (nulovala obe slozky hned; nahrazeno 2026-08-11, viz doc/robotour-mission.md):
	'	if di3=0 then
	'		reqSpeed=0
	'		reqRotSpeed=0
	'	end if

	'Watchdog (host uz 500 ms nemluvi) nuluje OBE slozky hned - zamerne jinak nez emergency stop:
	'pri mrtvem hostovi je posledni rotacni prikaz zastaraly a slepe zatoceni pri dojezdu je horsi
	'nez dojezd rovne. Pri emergency stopu host zije a jeho zatoceni je aktualni.
	timeout-=time
	if timeout<0 then
		emergency=1
		reqSpeed=0
		reqRotSpeed=0
		timeout=0
	end if


	'pocitani aktualni dopredne rychlosti
	'Bezna jizda: jedna rampa acceleration pro rozjezd i brzdeni (symetrie). Pod nouzovym
	'zastavenim nebo watchdogem se BRZDI emDeceleration. Brzdi se, kdyz se velikost rychlosti
	'zmensuje - u jizdy vzad (curSpeed<0) je to RUST hodnoty.
	target=1000*reqSpeed
	rate=acceleration
	if curSpeed>0 then
		if target<curSpeed then
			if emergency=1 then
				rate=emDeceleration
			end if
		end if
	end if
	if curSpeed<0 then
		if target>curSpeed then
			if emergency=1 then
				rate=emDeceleration
			end if
		end if
	end if
	if curSpeed<target then
		curSpeed+=time*rate
		if curSpeed>target then
			curSpeed=target
		end if
	end if
	if curSpeed>target then
		curSpeed-=time*rate
		if curSpeed<target then
			curSpeed=target
		end if
	end if
	'PREDCHOZI VARIANTA (do 2.1 jedna rampa acceleration pro rozjezd, brzdeni i nouzi):
	'	if curSpeed<1000*reqSpeed then
	'		curSpeed+=time*acceleration
	'		...
	'	if curSpeed>1000*reqSpeed then
	'		curSpeed-=time*acceleration
	'		...
	
	'pocitani aktualni rotacni rychlosti
	if curRotSpeed<1000*reqRotSpeed then
		curRotSpeed+=time*rotAcceleration
		if curRotSpeed>1000*reqRotSpeed then
			curRotSpeed=1000*reqRotSpeed
		end if
	end if		
	if curRotSpeed>1000*reqRotSpeed then
		curRotSpeed-=time*rotAcceleration
		if curRotSpeed<1000*reqRotSpeed then
			curRotSpeed=1000*reqRotSpeed
		end if
	end if		
	
	'pri otaceni omezim doprednou rychlost, aby nebyla prekrocena maximalni mozna rychlost kazdeho z kol
	if curSpeed>1000000-Abs(curRotSpeed) then
		curSpeed=1000000-Abs(curRotSpeed)
	end if
	
	if curSpeed<-1000000+Abs(curRotSpeed) then
		curSpeed=-1000000+Abs(curRotSpeed)
	end if

	
	'zde osetrit emergency stop
	'motory okamzite na nulu
'	di3=GetValue(_DI, 3)
'	if di3=0 then
'		curSpeed=0
'		curRotSpeed=0
'	end if
' 
'	timeout-=time
'	if timeout<0 then
'		curSpeed=0
'		curRotSpeed=0
'		timeout=0
'	end if
		
	SetCommand(_G, 1, -(curSpeed+curRotSpeed)/1000)
	SetCommand(_G, 2, (curSpeed-curRotSpeed)/1000)
	
		
	SetCommand(_VAR, 5, curSpeed)
	SetCommand(_VAR, 6, curRotSpeed)

	'ED= (od 2.2) - ucinne nouzove zpomaleni; host podle nej pozna, ze skript nouzovou rampu umi.
	'Stary host radek preskoci (ceka na DI=), stejne jako T=.
	print("ED=", emDeceleration, "\r")
	print("T=", tick, "\r")
	print("DI=", di3, "\r")
	print("C=", GetValue(_C, 1), ":", GetValue(_C, 2), "\r")
	print("V=", GetValue(_V, 2), "\r")
	print("A=", GetValue(_A, 1), ":", GetValue(_A, 2), "\r")
	wait(10)

end while


      
     
    */
    public class SDC2160Ex: UartSensorBase<IMotorState>, IMotorControl
    {
        double maxPossibleSpeed;
        double speedLimit;
        double enc2Dist;
        double wheelCircumference;
        double enc2Rotation;
        bool isEmergencyStop=true;

        /// <summary>
        /// Stav enkoderu a cas PREDCHOZIHO vzorku - rychlost kol si driver pocita ze sveho
        /// vzorkovaciho intervalu, aby nezavisela na tom, kdo a kdy mereni cte.
        /// Drive se odvozovala z <c>FramePickupPeriod</c>, takze bez vyzvedavani vychazela nula
        /// (v runtime se motory odebiraji jen udalosti). Viz doc/virtual-hw.md.
        /// </summary>
        double? prevRightEnc, prevLeftEnc;
        DateTime? prevEncTime;
        int cnt = 0;

        /// <summary>
        /// Nouzove zpomaleni, se kterym skript v jednotce POCITA (radek <c>ED=</c>, od skriptu 2.2),
        /// v jednotkach skriptu; <c>null</c> = radek jeste neprisel (stary skript, nebo zacatek).
        /// </summary>
        volatile object scriptEmergencyUnits;
        /// <summary>Kolik ramcu telemetrie uz prislo (kvuli hlaseni stareho skriptu).</summary>
        int framesSeen;
        /// <summary>Nouzove zpomaleni, ktere host poslal (jednotky skriptu); -1 = neposlal.</summary>
        volatile int sentEmergencyUnits = -1;
        /// <summary>Hlaseni o nouzove rampe uz odeslo (jednou za beh).</summary>
        bool rampReported;
        /// <summary>Po tolika ramcich bez <c>ED=</c> se ohlasi stary skript (~1 s pri 100 Hz).</summary>
        const int FramesBeforeOldScriptWarning = 100;

        /// <summary>
        /// Nouzove zpomaleni, se kterym skript v jednotce skutecne pocita [m/s²] (radek <c>ED=</c>);
        /// <c>null</c> = skript ho nehlasi, tedy je starsi nez 2.2 a pod nouzovym zastavenim brzdi
        /// beznou rampou.
        /// </summary>
        public double? ScriptEmergencyDeceleration
            => scriptEmergencyUnits is int u ? u / 1000.0 * maxPossibleSpeed : (double?)null;

        /// <summary>Citac casu ve skriptu jednotky bezi modulo tato hodnota [ms] (viz skript vyse).</summary>
        public const long DeviceTimeModulus = 1000000000;

        /// <summary>Prevod casu jednotky (radek <c>T=</c>) na cas aplikace.</summary>
        readonly DeviceClock deviceClock = new DeviceClock(DeviceTimeModulus);

        /// <summary>Kolikrat se hodiny jednotky (re)synchronizovaly; 0 = jednotka cas neposila.</summary>
        public int DeviceClockSyncs => deviceClock.Syncs;
        /// <summary>
        /// Construktor
        /// </summary>
        /// <param name="uart">UART used to comunication</param>
        public SDC2160Ex(IUart uart, double maxPossibleSpeed, double speedLimit, double wheelCircumference, double enc2Rotation):base(uart)
        {
            this.maxPossibleSpeed = maxPossibleSpeed;
            this.speedLimit = Math.Min(speedLimit, maxPossibleSpeed);
            this.wheelCircumference = wheelCircumference;
            this.enc2Rotation = enc2Rotation;

            this.enc2Dist = wheelCircumference / enc2Rotation;

            uart.WriteLine("^ECHOF 1");
            Drive(0, 0);

            Start();
        }

        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public override string Name => "SDC2160Ex";

        private int CalcSpeed(double speed)
        {
            double d = speed;
            int i = (int)(1000 * d / maxPossibleSpeed);
            return Math.Min(Math.Max(i, -1000), 1000);
        }

        /// <summary>
        /// Sets motors speed 
        /// </summary>
        /// <param name="forvardSpeed">Forvard speed (left and right motor common speed).</param>
        /// <param name="difSpeed">Diferencial speed. Positive value - right rotation, left motor is faster.</param>
        public void Drive(double forvardSpeed, double difSpeed)
        {
            if (forvardSpeed > speedLimit)
                forvardSpeed = speedLimit;
            if (forvardSpeed < -speedLimit)
                forvardSpeed = -speedLimit;

            uart.WriteLine(string.Format("!VAR 3 {0}", -CalcSpeed(forvardSpeed)));
            uart.WriteLine(string.Format("!VAR 4 {0}", -CalcSpeed(difSpeed)));
            uart.WriteLine(string.Format("!VAR 7 {0}", cnt++));
//            Debug.WriteLine(string.Format("!G {0} {1} {2} {3}", CalcSpeed(forvardSpeed), CalcSpeed(difSpeed), forvardSpeed, difSpeed));
        }

        /// <summary>
        /// Sets motor driver acceleration/deceleration
        /// </summary>
        /// <param name="acceleration"></param>
        /// <summary>
        /// Rampy zvlast (skript 2.2): <c>VAR 1/2</c> bezna jizda (rozjezd i brzdeni, dopredna
        /// i rotacni slozka), <c>VAR 9</c> nouzove zastaveni a watchdog — v jednotkach skriptu
        /// (<see cref="MotorAcceleration.ToScriptUnits"/>). Stary skript <c>VAR 9</c> ignoruje
        /// a brzdi vsude beznou rampou; ohlasi se to do Trace (<see cref="ReportEmergencyRampOnce"/>).
        /// </summary>
        public void SetRamps(MotorRamps ramps)
        {
            SetAcceleration(ramps.Acceleration);
            int em = MotorAcceleration.ToScriptUnits(ramps.EmergencyDeceleration, maxPossibleSpeed);
            uart.WriteLine(string.Format("!VAR 9 {0}", em));
            sentEmergencyUnits = em;
        }

        /// <summary>
        /// Jednou za beh ohlasi do Trace, s jakou nouzovou rampou skript v jednotce pocita — nebo
        /// ze ji nehlasi (stary skript pod 2.2: nouzove zastaveni pak brzdi beznou rampou, tedy po
        /// oprave prevodu z 5. 10. 2026 jen 0,40 m/s²). Trace, ne Debug: tohle je presne ta vec,
        /// ktera musi byt videt v zaznamu ze zarizeni (viz CLAUDE.md).
        /// </summary>
        void ReportEmergencyRampOnce()
        {
            if (rampReported) return;
            framesSeen++;
            if (scriptEmergencyUnits is int u)
            {
                // Dokud host rampy neposlal, skript hlasi svou vychozi hodnotu - pockat na shodu.
                if (sentEmergencyUnits >= 0 && u != sentEmergencyUnits && framesSeen < FramesBeforeOldScriptWarning)
                    return;
                rampReported = true;
                string shoda = sentEmergencyUnits < 0 ? "host rampy neposlal - vychozi ze skriptu"
                             : u == sentEmergencyUnits ? "shodne s nastavenim"
                             : $"NESHODA s nastavenim ({sentEmergencyUnits} jednotek)";
                Trace.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "SDC2160Ex: skript jednotky brzdi pri nouzovem zastaveni {0:F2} m/s² ({1} jednotek, {2}).",
                    u / 1000.0 * maxPossibleSpeed, u, shoda));
            }
            else if (framesSeen >= FramesBeforeOldScriptWarning)
            {
                rampReported = true;
                Trace.WriteLine("SDC2160Ex: skript v jednotce NEHLASI nouzovou rampu (ED=) - je v ni verze "
                                + "starsi nez 2.2 (Src/RoboRun/RizeniDiffPodvozku.mbs). Nouzove zastaveni "
                                + "brzdi BEZNOU rampou; nahrajte skript 2.2 (Roborun+).");
            }
        }

        public void SetAcceleration(double acceleration)
        {
            // Jednotky SKRIPTU (tisiciny plneho rozsahu za s), ne nativniho !AC - do 5. 10. 2026 tu
            // byl ToUnits a rampa vychazela 2,6x strmejsi (hw-motor-rampa-jednotky).
            int v = MotorAcceleration.ToScriptUnits(acceleration, maxPossibleSpeed);
            Debug.WriteLine(string.Format("Akceleration={0}", v));
            uart.WriteLine(string.Format("!VAR 1 {0}", v));
            uart.WriteLine(string.Format("!VAR 2 {0}", v));
        }

        private string GetValue(string str)
        {
            if (str == null)
                return "";
            int idx = str.IndexOf("=");
            if (idx > -1)
                return str.Substring(idx + 1);
            return str;
        }


        protected override IMotorState GetMeasurement()
        {
            string str, di;
            bool fail = false;
//            str= uart.ReadAll();
            var ts = TimeBase.Now;
            // Cas jednotky z radku T= pred DI= (novy skript); -1 = neprisel.
            long tick = -1;
            DateTime tickArrival = default;

            do
            {
                str = uart.ReadLine();
                // ED= (skript 2.2): nouzove zpomaleni, se kterym skript pocita.
                if (str != null && str.StartsWith("ED="))
                {
                    if (int.TryParse(GetValue(str), out int ed)) scriptEmergencyUnits = ed;
                }
                if (str != null && str.StartsWith("T="))
                {
                    var arrival = TimeBase.Now;
                    tick = long.TryParse(GetValue(str), out long t) ? t : -1;
                    tickArrival = arrival;
                }

                // Zastavujeme se: vratit rovnou null, ne fail-ramec. Cekat cele okno pri kazdem
                // pruchodu by Stop() zbytecne protahovalo (a s nekonecnym ReadTimeout, ktery mel
                // Uart do 15. 9. 2026, dokonce navzdy - viz SensorBase.StopTimeout).
                // ⚠️ null, a NE fail-ramec: ten znamena „nevim, co se deje, at robot stoji"
                // a brany mise ho berou jako „neznamo". Vyrabet ho pri regulernim vypnuti by
                // do zaznamu psalo poruchu, ktera se nestala.
                if (stopRequired)
                    return null;

                if((TimeBase.Now-ts).TotalMilliseconds>500)
                {
                    fail = true;
                    break;
                }
                if (str == null)
                    // Port nedostupny (ReadLine vraci null hned, ReOpen uz neblokuje) -
                    // kratky spanek, aby smycka behem 500ms okna nebusy-spinovala.
                    System.Threading.Thread.Sleep(10);
            }
            while (str == null || !str.StartsWith("DI="));
            di = GetValue(str);
            ReportEmergencyRampOnce();

            str = uart.ReadLine();
            str = GetValue(str);
            string[] enc = str.Split(new string[] { ":" }, StringSplitOptions.RemoveEmptyEntries);

            double leftEnc = 0;
            double rightEnc = 0;

            if (enc.Length > 0 && double.TryParse(enc[0], out rightEnc))
                rightEnc *= enc2Dist;
            else
                fail = true;

            if (enc.Length > 1 && double.TryParse(enc[1], out leftEnc))
                leftEnc *= -enc2Dist;
            else
                fail = true;

            str = uart.ReadLine();
            str = GetValue(str);
            double batVolts = 0;
            if (double.TryParse(str, out batVolts))
                batVolts /= 10;
            else
                fail = true;

            str = uart.ReadLine();
            str = GetValue(str);
            string[] amp = str.Split(new string[] { ":" }, StringSplitOptions.RemoveEmptyEntries);

            double leftCurrent = 0;
            double rightCurrent = 0;

            if (amp.Length > 0 && double.TryParse(amp[0], out leftCurrent))
                leftCurrent /= 10;
            else
                fail = true;
            if (amp.Length > 1 && double.TryParse(amp[1], out rightCurrent))
                rightCurrent /= 10;
            else
                fail = true;

            MotorStateBase s;
            if (fail)
                // Stop = true je FAIL-SAFE (nevime, co se deje -> at robot stoji), ale nuly
                // v enkoderech a rychlostech nikdo nemeril. Bez hasMeasurement: false by fuze
                // dostala "stojim" prave v okamziku, kdy o robotu nevime nic - a robot se pritom
                // muze pohybovat. Viz IMotorState.HasMeasurement.
                s= new MotorStateBase(true, 0, 0, 0, 0, 0, 0, 0, hasMeasurement: false) { TimeStamp = ts };
            else
            {
                // Cas jednotky: razitko i interval z jejich hodin (DeviceClock). Kdyz ho skript
                // neposila, nebo jde o prvni vzorek po (re)synchronizaci, interval z casu
                // prichodu jako driv - jeden vzorek s jitterem je lepsi nez rychlost 0 za jizdy.
                var mapped = tick >= 0 ? deviceClock.Map(tick, tickArrival) : null;
                if (mapped.HasValue)
                    ts = mapped.Value.Time;

                // Rychlost z vlastniho vzorkovaciho intervalu; prvni vzorek ji jeste nema.
                double dt = mapped?.DeltaMs is long dMs ? dMs / 1000.0
                          : prevEncTime.HasValue ? (ts - prevEncTime.Value).TotalSeconds : 0;
                double leftSpeed = 0, rightSpeed = 0;
                if (dt > 0.001)
                {
                    leftSpeed = (leftEnc - (prevLeftEnc ?? leftEnc)) / dt;
                    rightSpeed = (rightEnc - (prevRightEnc ?? rightEnc)) / dt;
                }

                // Enkodery se hlasi KUMULATIVNE - odberatel si prirustek spocte pres svuj interval
                // (a neprijde o nej, i kdyz nejaky vzorek preskoci).
                s = new MotorStateBase(isEmergencyStop = (di == "0"), leftEnc, rightEnc,
                                       batVolts, leftCurrent, rightCurrent,
                                       leftSpeed, rightSpeed,
                                       deviceTimeMs: mapped.HasValue ? tick : -1) { TimeStamp = ts };

                prevLeftEnc = leftEnc;
                prevRightEnc = rightEnc;
                prevEncTime = ts;
            }
            return s;
        }
    }
}

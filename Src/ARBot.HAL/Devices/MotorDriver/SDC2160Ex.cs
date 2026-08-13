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
    /// <para><b>POZOR - skript nize NENI kompilovany kod.</b> Je to zdroj programu, ktery bezi
    /// V MOTOROVE JEDNOTCE; do zarizeni se nahrava zvlast (Roborun+ / MicroBasic upload). Zmena
    /// tady sama o sobe chovani robota NEZMENI, dokud se skript do jednotky nenahraje - a protoze
    /// jde o cestu nouzoveho zastaveni, je nutne ji po nahrani OVERIT NA ZARIZENI.</para>
    /// </summary>
    /*

' var 1 - dopredna akcelerace v tisicinach max vykonu za s^2
' var 2 - rotacni akcelerace v tisicinach max vykonu za s^2
' var 3 - pozadovana dopredna rychlost v tisicinach max rychlosti
' var 4 - pozadovana rotacni rychlost v tisicinach max rychlosti, kladna hodnota je v matematickem smyslu
' var 5 - aktualni dopredna rychlost v miliontinach max rychlosti
' var 6 - aktualni rotacni rychlost v miliontinach max rychlosti, kladna hodnota je v matematickem smyslu
' var 7 - mark, pri jeho zmene se resetne timeout, po vyprseni timeoutu se robot zastavi

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

timeout=0
currentTimer=GetTimerCount(1)

while true
	lastTimer=currentTimer
	currentTimer=GetTimerCount(1)
	'vypocet uplynuleho casu v ms
	time =lastTimer-currentTimer
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
	'Pojistka acceleration<=0: kdyby dopredna rampa nemohla postupovat, curSpeed by nulu nikdy
	'nedosahl a robot by se pod stopem otacel na miste porad. Radeji rovnou obe nuly.
	di3=GetValue(_DI, 3)
	if di3=0 then
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
		reqSpeed=0
		reqRotSpeed=0
		timeout=0
	end if


	'pocitani aktualni dopredne rychlosti
	if curSpeed<1000*reqSpeed then
		curSpeed+=time*acceleration
		if curSpeed>1000*reqSpeed then
			curSpeed=1000*reqSpeed
		end if
	end if		
	if curSpeed>1000*reqSpeed then
		curSpeed-=time*acceleration
		if curSpeed<1000*reqSpeed then
			curSpeed=1000*reqSpeed
		end if
	end if		
	
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
        public void SetAcceleration(double acceleration)
        {
            int v = (int)Math.Round(10 * 60 * acceleration / wheelCircumference);
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

            do
            {
                str = uart.ReadLine();
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
                s= new MotorStateBase(true, 0, 0, 0, 0, 0, 0, 0) { TimeStamp = ts };
            else
            {
                // Rychlost z vlastniho vzorkovaciho intervalu; prvni vzorek ji jeste nema.
                double dt = prevEncTime.HasValue ? (ts - prevEncTime.Value).TotalSeconds : 0;
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
                                       leftSpeed, rightSpeed) { TimeStamp = ts };

                prevLeftEnc = leftEnc;
                prevRightEnc = rightEnc;
                prevEncTime = ts;
            }
            return s;
        }
    }
}

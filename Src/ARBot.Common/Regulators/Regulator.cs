using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.Models;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Regulator diferencialniho podvozku, kde motory akceleruji konstantni hodnototu.
    /// </summary>
    public class Regulator : IRegulator
    {
        public int MaxWayPoints {get{return 1;}}
        public double maxSpeed, acceleration, rozchod2, maxOrientationSpeed, stability=4;

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="maxSpeed">maximalni dopredna rychlost v m/s</param>
        /// <param name="maxOrientationSpeed">maximalni rychlost otaceni v radianech/s </param>
        /// <param name="acceleration">akcelerace v m/s^2</param>
        /// <param name="rozchod">Vzdalenost levoho a praveho kola</param>
        public Regulator(double maxSpeed, double maxOrientationSpeed, double acceleration, double rozchod)
        {
            this.maxSpeed = maxSpeed;
            this.maxOrientationSpeed = maxOrientationSpeed;
            this.acceleration = acceleration;
            this.rozchod2 = rozchod / 2.0;
        }

        public static double Dist2Speed(double dist, double startSpeed, double endSpeed, double maxSpeed, double acceleration, double tSam)
        {
            return Dist2Speed2(dist, startSpeed, endSpeed, maxSpeed, acceleration, tSam).Speed;
        }


            /*        
                    /// Tohle je matlab kod pro vypocet diskretniho regulatoru

            syms a d vs ve vm vm1 vmax x xs xe xm ts te tm t tsam ns ne nm vm1 ne1;
            % a - zrychleni
            % d - zpomaleni
            % tsam - vzorkovaci perioda
            % vs - pocatecni rychlost
            % ve - koncova rychlost
            % vm - maximalni rychlost, pokud budu jen zrychlovat a pak zpomalovat
            % vm1 - maximalni rychlost, je menzi jak vmax
            % vmax - maximalni rychlost, kterou muze robot jet
            % x - ujeta vzdalenost
            % xs - ujeta vzdalenost pri zrychlovani
            % xe - ujeta vzdalenost pri zpomalovani
            % xm - ujeta vzdalenost rychlosti vm1
            % ts - cas zrychlovani
            % te - cas zpomalovani
            % tm - cas jizdy vm1
            % ns - pocet vzroku tsam pri zrychlovani
            % ne - pocet vzroku tsam pri zpomalovani
            % nm - pocet vzroku tsam pri jizde vm1

            vma=vs+(ns-1)*a* tsam;
                    vmb=ve+(ne-1)*d* tsam;

                    xs=(ns* vs+ns* (ns-1)/2*a* tsam)*tsam;
            xe=(ne* vm-ne* (ne-1)/2*d* tsam)*tsam;

            [vm, ns, ne]=solve([vm-vma, vm-vmb, x-xs-xe], [vm, ns, ne])

            %ne=solve(x-xe, ne)
            %x=xs+xe;


            % rovnice pro vypocet casu regulace
            %u1=solve(r1, 'te');

            % cas regulace
            %te=u1(1);
            % maximalni rychlost
            %s=simplify(subs(sm, 'te', te));
            */

        /// <summary>
        /// Vypocet regulacniho zasahu.
        /// Pocita diskretni regulator, ktery meni zasahy v tSam periodi. Po celou dobu periody pocita konstantni zasah.
        /// </summary>
        /// <param name="dist"></param>
        /// <param name="startSpeed"></param>
        /// <param name="endSpeed"></param>
        /// <param name="maxSpeed"></param>
        /// <param name="acceleration"></param>
        /// <param name="tSam"></param>
        /// <remarks>
        /// 
        /// </remarks>
        /// <returns></returns>
        public static RegulatorResult Dist2Speed2(double dist, double startSpeed, double endSpeed, double maxSpeed, double acceleration, double tSam)
        {
            double x = dist;
            if (x < 0)
            {
                x = -x;
                endSpeed = -endSpeed;
                startSpeed = -startSpeed;
            }

            double a = acceleration;
            double d = acceleration;
            double a2 = a * a;
            double d2 = d * d;
            double tSam2 = tSam * tSam;
            double ve = endSpeed;
            double ve2 = ve*ve;
            double vs = startSpeed;
            double vs2 = vs*vs;


            //            double ne = Math.Floor((Math.Sqrt(a2 * d2 * tSam2 - a * d * tSam * ve - a * d * tSam * vs + 2 * x * a2 * d + a2 * ve2 - a * d2 * tSam * ve - a * d * tSam * vs + 2 * x * a * d2 + a * d * ve2 + a * d * vs2 + d2 * vs2) - a * ve - d * ve + a * d * tSam) / (d * tSam * (a + d)));

            double ne = (Math.Sqrt(a2 * d2 * tSam2 - a2 * d * tSam * ve - a2 * d * tSam * vs + 2 * x * a2 * d + a2 * ve2 - a * d2 * tSam * ve - a * d2 * tSam * vs + 2 * x * a * d2 + a * d * ve2 + a * d * vs2 + d2 * vs2) - a * ve - d * ve + d2 * tSam) / (d * tSam * (a + d));
            if (Math.Abs(ne) > 2)
                ne = Math.Floor(ne);

            double vm = (ve + (ne-1) * d * tSam)*0.9;
            double ns = Math.Max(0, (vm - vs) / (a * tSam)+1);

            if (vm<maxSpeed)
            {
                return new RegulatorResult() { Speed = vm * Math.Sign(dist), RegulationTime = (ns+ne)*tSam };
            }
            vm = maxSpeed;
            ne = (vm - ve) / (d * tSam)+1;
            ns = (vm-vs)/(a*tSam)+1;

            double xs = (ns * vs + ns * (ns - 1) / 2 * a * tSam) * tSam;
            double xe = (ne * ve + ne * (ne - 1) / 2 * d * tSam) * tSam;
            double nm=(x - xs - xe) / (vm*tSam);

            return new RegulatorResult() { Speed = vm * Math.Sign(dist), RegulationTime = (ns + nm + ne) * tSam };
        }

        public RegulatorResult Rot2RotSpeed(double beta, double startRotSpeed, double endRotSpeed)
        {
            var ret= Dist2Speed2(beta * rozchod2, startRotSpeed * rozchod2, endRotSpeed * rozchod2, maxOrientationSpeed * rozchod2, acceleration, 0.1);
            return new RegulatorResult() { RegulationTime = ret.RegulationTime, RotationSpeed = ret.Speed / rozchod2};
        }

        public RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed)
        {
            return Dist2Speed2(dist, startSpeed, endSpeed, maxSpeed, acceleration, 0.1);
        }

        public double Speed2Dist(double startSpeed, double endSpeed)
        {
            double s = Math.Abs(startSpeed - endSpeed);
            return s*s / (2*acceleration);
        }

        public RegulatorResult Control(IModelState state, RegulatorWayPoint[] points)
        {
            if(points.Length!=MaxWayPoints)
                throw new Exception("Nepodporovana delka");

            RegulatorWayPoint p=points[0];

            double dx=p.X-state.X;
            double dy=p.Y-state.Y;
            double d=Math.Sqrt(dx*dx+dy*dy);
            double beta=0;
            if (d > p.MaxPositionError || p.Speed>0)
                beta = Conversions.NormalizeOrientation(Math.Atan2(dy, dx) - state.Orientation);
            else
            {
//                beta = Conversions.NormalizeOrientation(p.Orientation - state.Orientation);
                d = 0;
            }

            var retRot= Rot2RotSpeed(beta, state.OrientationVelocity, 0);

            var ret=Dist2Speed2(d, state.Velocity, p.Speed, maxSpeed, acceleration, 0.1);
            //            Debug.WriteLine(string.Format("dx={0}, dy={1}, d={2}, beta={3}, sRot={4}, tRot={5}, s={6}, t={7}", dx, dy, d, beta, sRot, tRot, s, t));

            // dopredna rychlost je zhora omezena max. rychlosti, aby se robot stacil otocit
            double s = SpeedLimit(ret.Speed, d, retRot);

            // pokud jsem otocen smerem od cile tak dopredna rychlost je nula
            if (Math.Abs(beta) > Math.PI / 2)
                s = 0;

            return new RegulatorResult() { Speed=s, RotationSpeed=retRot.RotationSpeed, RegulationTime=Math.Max(ret.RegulationTime, retRot.RegulationTime) };
        }
        /// <summary>
        /// Omezi doprednou rychlost na zaklade tychlosti rotace
        /// </summary>
        /// <param name="speed">dopredna rychlost </param>
        /// <param name="d">vzdalenost na ktere musi dojit k otoceni</param>
        /// <param name="rotationResul">Vysledek vypoctu rotacni rychlosti</param>
        /// <returns></returns>
        public double SpeedLimit(double speed, double d, RegulatorResult rotationResul)
        {
            if (rotationResul.RegulationTime != 0)
            {
                var sl = d / (stability * rotationResul.RegulationTime);
//                Debug.WriteLine(string.Format("d={0}, rt={1}, sl={2}", d, rotationResul.RegulationTime, sl));
                return Math.Min(speed, sl);
            }
            return speed;
        }
    }
}

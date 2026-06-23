using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public class LidarFolowMeNavigation
    {
        double minLeg = 0.02;
        double maxLeg = 0.16;
        double minDist = 0.08;
        double maxDist = 0.76;
        double maxSpeed = 2;


        public Vector3D? Position { get; private set; }
        public Leg Leg1 { get; private set; }
        public Leg Leg2 { get; private set; }
        public Dictionary<RayEx, double?> Hrany { get; private set; }

        private DateTime lastTimeStamp;
        private int faildCount = 0;
        private Vector3D lastPosition;

        public List<Leg> Legs { get; private set; }

        public class Leg
        {
            public Vector3D Position;
            public double Width;
            public double Distance;

            public Leg(RayEx r1, RayEx r2)
            {
                Width = (r1.Target - r2.Target).Value.Length;
                Position = (r1.Target + r2.Target).Value / 2;
                Distance = Position.Length;
            }/*
            public Noha(Noha n1, Noha n2)
            {
                double x1 = n1.X;
                double y1 = n1.Y;
                double x2 = n2.X;
                double y2 = n2.Y;

                double x = x1 - x2;
                double y = y1 - y2;

                X = (x1 + x2) / 2;
                Y = (y1 + y2) / 2;
                Sirka = Math.Sqrt(x * x + y * y);
                Distance = Math.Sqrt(X * X + Y * Y);
            }*/
        }

        int Index(int idx, int cnt)
        {
            if (idx < 0)
                idx += cnt;
            return idx % cnt;
        }
        /*
                List<Leg> CalcLegs(IList<RayEx> rays)
                {
                    List<Leg> ret = new List<Leg>();

                    int cnt = rays.Count;
                    for (int i = 0; i < cnt; i++)
                    {
                        RayEx r1l = rays[i];
                        for (int j = 0; j < cnt; j++)
                        {
                            int idx1 = Index(i + j, cnt);
                            RayEx r1r = rays[idx1];

                            Leg n = new Leg(r1l, r1r);

                            if (n.Width > minLeg)
                            {
                                if (n.Width > maxLeg)
                                    break;
                                ret.Add(n);
                            }
                        }
                    }
                    Legs = ret;
                    return ret;
                }
                */

        List<Leg> CalcLegs(IList<RayEx> rays)
        {
            List<Leg> ret = new List<Leg>();

            int cnt = rays.Count;
            for (int i = 0; i < cnt; i++)
            {
                RayEx r1l = rays[i];
                int idx1 = Index(i + 1, cnt);
                RayEx r1r = rays[idx1];

                Leg n = new Leg(r1l, r1r);

                if (n.Width > minLeg && n.Width < maxLeg)
                    ret.Add(n);
            }
            Legs = ret;
            return ret;
        }

        Vector3D? CalcPosition(IList<RayEx> rays, ref Leg l1, ref Leg l2)
        {
            if (rays.Count < 2)
                return null;

            Vector3D? pos = lastPosition;
            if (pos == null)
                pos = new Vector3D();

            var diffs = rays.Conv(new double[] { -1, 0, 1 }, 1, true).Where((kv)=>kv.Value.GetValueOrDefault(0)!=0 && kv.Key.Target!=null).ToList();
            if (!diffs.Any())
                return null;
            var avg = diffs.Average((kv) => Math.Abs(kv.Value.Value));
            Hrany = diffs.Where((kv) => Math.Abs(kv.Value.Value) > avg).ToDictionary((kv) => kv.Key, (kv) => kv.Value);
            var hrany = Hrany.Select((kv)=>kv.Key).OrderBy((kv)=>kv.Angle).ToList();

            Legs = CalcLegs(hrany);


            foreach (var d in Legs.OrderBy((l) => (l.Position - pos).Value.Length))
            {
                var v = Legs.Select((i) => new { Leg = i, Distance = (i.Position - d.Position).Length });
                var n1 = v.Where((i) => i.Distance > minDist && i.Distance < maxDist).OrderBy((i) => i.Distance).FirstOrDefault()?.Leg;

                if (n1 != null)
                {
                    l1 = d;
                    l2 = n1;
                    return (d.Position + n1.Position) / 2;
                }
            }
            return null;
        }

        public void Process(IList<RayEx> rays)
        {
            Leg l1 = null;
            Leg l2 = null;
            var p = CalcPosition(rays, ref l1, ref l2);

            if(p==null)
            {
                faildCount++;
            }
            else
            {
                DateTime dt = TimeBase.Now;
                if ((p.Value - lastPosition).Length / (dt - lastTimeStamp).TotalSeconds > maxSpeed)
                    faildCount++;
                else
                    faildCount = 0;

                if(faildCount==0)
                {
                    lastPosition = p.Value;
                    Position = lastPosition;
                    lastTimeStamp = dt;
                    Leg1 = l1;
                    Leg2 = l2;
//                    Debug.WriteLine(string.Format("FM {0}, {1}", Position.Value.X, Position.Value.Y));

                }
            }
            if (faildCount > 3)
            {
                Position = null;
                Leg1 = null;
                Leg2 = null;
            }

        }

    }
}

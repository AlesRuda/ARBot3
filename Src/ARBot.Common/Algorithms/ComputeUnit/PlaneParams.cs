using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.ComputeUnit
{
	[StructLayout(LayoutKind.Sequential)]
	public struct PlaneParams
	{
		public float SumX, SumY, SumZ, Count1;
		public float SumXY, SumYZ, SumZX, Count2;
		public float SumXX, SumYY, SumZZ, Count3;
		public Point4D v; //z'=v.p=v.x*p.x+v.y*p.y+v.z*p.z+v.a*p.a, pokud v.x=a, v.y=b, v.z=0, v.a=c, dostavam rovnici roviny z=a*x+b*y+c

		public void Calc()
		{
			float sx = SumX;
			float sy = SumY;
			float sz = SumZ;
			float sxz = SumZX;
			float sxy = SumXY;
			float syz = SumYZ;
			float sxx = SumXX;
			float syy = SumYY;
			float szz = SumZZ;
			float n = Count1;

			float d = (syy * sx * sx - 2 * sx * sxy * sy + n * sxy * sxy + sxx * sy * sy - n * sxx * syy);
			if (d != 0)
			{
				float a = (sxz * sy * sy + n * sxy * syz - n * sxz * syy - sx * sy * syz + sx * syy * sz - sxy * sy * sz) / d;
				float b = (sx * sx * syz + n * sxy * sxz - n * sxx * syz - sx * sxz * sy - sx * sxy * sz + sxx * sy * sz) / d;
				float c = (sxy * sxy * sz - sx * sxy * syz + sx * sxz * syy - sxy * sxz * sy + sxx * sy * syz - sxx * syy * sz) / d;

				v.X = -a;
				v.Y = -b;
				v.Z = 1;
				v.A = -c;
			}
			else
            {
				v.X = 0;
				v.Y = 0;
				v.Z = 1;
				v.A = 0;
			}
		}
		public PlaneParams(IEnumerable<Point4D> pts)
		{
			SumX = 0;
			SumY = 0;
			SumZ = 0;
			Count1 = 0;
			SumXY = 0;
			SumYZ = 0;
			SumZX = 0;
			Count2 = 0;
			SumXX = 0;
			SumYY = 0;
			SumZZ = 0;
			Count3 = 0;
			float x, y, z;

			foreach (var p in pts)
            {
				x = p.X;
				y = p.Y;
				z = p.Z;

				SumX += x;
				SumY += y;
				SumZ += z;
				SumXY += x*y;
				SumYZ += y*z;
				SumZX += z*x;
				SumXX += x*x;
				SumYY += y*y;
				SumZZ += z*z;
				Count1++;
			}
			v = new Point4D();
			Calc();
		}
	}
}

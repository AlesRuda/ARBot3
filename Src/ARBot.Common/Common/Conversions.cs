using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public static class Conversions
    {
        private static double degToRad = Math.PI / 180;
        /// <summary>
        /// Konverze stupnu na radiany.
        /// </summary>
        /// <param name="azimut"></param>
        /// <returns></returns>
        public static double Deg2Rad(double deg)
        {
            return deg * degToRad;
        }

        /// <summary>
        /// Konverze radianu na stupne
        /// </summary>
        /// <param name="azimut"></param>
        /// <returns></returns>
        public static double Rad2Deg(double rad)
        {
            return rad / degToRad;
        }


        /// <summary>
        /// Vypocet prumerneho radianoveho uhlu
        /// https://en.wikipedia.org/wiki/Circular_mean
        /// </summary>
        /// <param name="pars"></param>
        /// <returns></returns>
        public static double CircularMean(params double[] pars)
        {
            var cnt = pars.Count();
            if (cnt == 0)
                throw new ArgumentException("pars can't be empty");
            if (cnt == 1)
                return pars[0];
            var s = pars.Average(i => Math.Sin(i));
            var c = pars.Average(i => Math.Cos(i));
            return Math.Atan2(s, c);
        }
        /// <summary>
        /// Upravuje orientaci do rozsahu +-Pi.
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        public static double NormalizeOrientation(double o)
        {
            if (o > Math.PI)
                o = o - 2 * Math.PI * Math.Floor((o + Math.PI) / (2 * Math.PI));
            else if (o <= -Math.PI)
                o = o - 2 * Math.PI * Math.Ceiling((o - Math.PI) / (2 * Math.PI));
            return o;
        }

        /// <summary>
        /// Upravuje orientaci do rozsahu +-Pi/2.
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        public static double NormalizeHalfOrientation(double o)
        {
            if (o > Math.PI / 2)
                o = o - Math.PI * Math.Floor((o + Math.PI / 2) / (Math.PI));
            else if (o <= -Math.PI / 2)
                o = o - Math.PI * Math.Ceiling((o - Math.PI / 2) / (Math.PI));
            return o;
        }

        /// <summary>
        /// Upravuje orientaci toHalf (prictenim PI) tak aby rozdil mezi primary byl minimalni.
        /// Vysledek je v rozmeni +-PI
        /// </summary>
        /// <param name="primary"></param>
        /// <param name="toHalf">Smer. Je chapan vpred a zaroven i vzad.</param>
        /// <returns></returns>
        public static double NormalizePrimaryOrientation(double primary, double toHalf)
        {
            var h = NormalizeOrientation(toHalf);
            var p = NormalizeOrientation(primary);
            if (Math.Abs(NormalizeOrientation(h - p)) > Math.PI / 2)
                return NormalizeOrientation(h + Math.PI);
            return h;
        }


        /// <summary>
        /// Upravuje azimut do rozsahu +-180 stupnu.
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        public static double NormalizeAzimut(double o)
        {
            if (o > 180)
                o = o - 360 * Math.Floor((o + 180) / 360);
            else if (o <= -180)
                o = o - 360 * Math.Ceiling((o - 180) / 360);
            return o;
        }

        /// <summary>
        /// Konverze mezi smerem kompasu a matematickym smerem. Oboji v radianech.
        /// </summary>
        /// <param name="azimut"></param>
        /// <returns></returns>
        public static double Azimut2Orientation(double azimut)
        {
            return NormalizeOrientation(Math.PI / 2 - azimut);
        }

        /// <summary>
        /// Konverze mezi matematickym smerem a smerem kompasu v radianech
        /// </summary>
        /// <param name="orientation"></param>
        /// <returns></returns>
        public static double Orientation2Azimut(double orientation)
        {
            return NormalizeOrientation(Math.PI / 2 - orientation);
        }

        /// <summary>
        /// Spocte transformaci souradnic kamery (x roste vpravo, y roste dolu a z smerem pohledu kamery) do 
        /// svetovych souradnic (x roste na vychod, y roste na sever a z roste smerem nahoru)
        /// </summary>
        /// <param name="roll">Pravoleve otoceni oproti vodorovne rovine v radianech </param>
        /// <param name="pitch">Predozadni naklon oproti vodorovne rovine v radianech, kladna hodnota je smerem nahoru</param>
        /// <param name="yaw">Otoceni oproti vychodu v radianech a matematickem smyslu.
        /// </param>
        /// <param name="offset">Posunuti kamery v metrech vzhledem k rovine po ktere jede robot.</param>
        /// <returns></returns>pootoceni kamery vhledem k realnemu svetu.
        /*        public static Matrix3D CameraToWordTransform(double yaw, double pitch, double roll, Vector3D offset)
                {
                    var m = Matrix3D.Identity;

                    m.Rotate(new Quaternion(new Vector3D(1, 0, 0), -90 + Conversions.Rad2Deg(pitch)));
                                m.Rotate(new Quaternion(new Vector3D(0, 1, 0), Conversions.Rad2Deg(roll)));
                                m.Rotate(new Quaternion(new Vector3D(0, 0, 1), Conversions.Rad2Deg(yaw) - 90));

                    m.Translate(offset);
                    return m;
                }*/


        public static Matrix4x4 CameraToWordTransform(double yaw, double pitch, double roll, Vector3 offset)
        {
            // 1. Přepočet úhlů na radiány s tvými posunů o -90 stupňů
            float p = (float)pitch + (-(float)Math.PI / 2f);
            float r = (float)roll;
            float y = (float)yaw + (-(float)Math.PI / 2f);

            // 2. Vytvoření samostatných kvaternionů pro každou osu
            Quaternion qPitch = Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), p);
            Quaternion qRoll = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), r);
            Quaternion qYaw = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), y);

            // 3. Složení kvaternionů (Obrácené pořadí pro 1:1 shodu s WPF)
            // Výsledný kvaternion reprezentuje celou rotaci najednou a bez Gimbal Locku!
            Quaternion qTotal = qYaw * qRoll * qPitch;

            // 4. Vytvoření rotační matice z výsledného kvaternionu
            Matrix4x4 mRotation = Matrix4x4.CreateFromQuaternion(qTotal);

            // 5. Přidání posunu (translace)
            Matrix4x4 mTranslate = Matrix4x4.CreateTranslation(offset);

            return mRotation * mTranslate;
        }

        /// <summary>
        /// Spocte transformaci ve svetovych souradnicich (x roste na vychod, y roste na sever a z roste smerem nahoru)
        /// </summary>
        /// <param name="roll">Pravoleve otoceni oproti vodorovne rovine v radianech </param>
        /// <param name="pitch">Predozadni naklon oproti vodorovne rovine v radianech, kladna hodnota je smerem dolu</param>
        /// <param name="yaw">Otoceni podel svisle osy v radianech a matematickem smyslu.</param>
        /// <param name="offset">Posunuti kamery v metrech vzhledem k rovine po ktere jede robot.</param>
        /// <returns></returns>pootoceni kamery vhledem k realnemu svetu.
/*        public static Matrix3D WordToWordTransform(double yaw, double pitch, double roll, Vector3D offset)
        {
            var m = Matrix3D.Identity;

            m.Rotate(new Quaternion(new Vector3D(1, 0, 0), Conversions.Rad2Deg(roll)));
            m.Rotate(new Quaternion(new Vector3D(0, 1, 0), Conversions.Rad2Deg(-pitch))); // minus tu vychazi protoze Z u mne smeruje nahoru, ale v modelech smeruje dolu https://en.wikipedia.org/wiki/Aircraft_principal_axes
            m.Rotate(new Quaternion(new Vector3D(0, 0, 1), Conversions.Rad2Deg(yaw)));

            m.Translate(offset);
            return m;
        }*/
        public static Matrix4x4 WordToWordTransform(double yaw, double pitch, double roll, Vector3 offset)
        {
            // 1. Přepočet úhlů na radiány (s tvým otočeným znaménkem u pitch)
            float r = (float)roll;
            float p = (float)(-pitch);
            float y = (float)yaw;

            // 2. Vytvoření samostatných kvaternionů pro každou osu
            Quaternion qRoll = Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), r);
            Quaternion qPitch = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), p);
            Quaternion qYaw = Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), y);

            // 3. Složení kvaternionů (Obrácené pořadí pro 1:1 shodu s WPF)
            Quaternion qTotal = qYaw * qPitch * qRoll;

            // 4. Vytvoření rotační matice z výsledného kvaternionu
            Matrix4x4 mRotation = Matrix4x4.CreateFromQuaternion(qTotal);

            // 5. Přidání posunu (translace)
            Matrix4x4 mTranslate = Matrix4x4.CreateTranslation(offset);

            return mRotation * mTranslate;
        }

        /// <summary>
        /// Rotacni transformace ktera vektor from pootoci do smeru vektoru to.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <remarks>
        /// https://math.stackexchange.com/questions/180418/calculate-rotation-matrix-to-align-vector-a-to-vector-b-in-3d
        /// </remarks>
        //        public static Matrix3D VectoToVector(Vector3D from, Vector3D to)
        //        {
        //            from.Normalize();
        //            to.Normalize();
        //            var v = Vector3D.CrossProduct(from, to);
        //            if (v.Length == 0)
        //                return Matrix3D.Identity;
        //            var l = (from.Length * to.Length);
        //            var s = v.Length / l;
        //            var c = Vector3D.DotProduct(from, to)/l;
        //            var vx = new Matrix(new double[3, 3]
        //                {
        //                    { 0, -v.Z, v.Y },
        //                    { v.Z, 0, -v.X },
        //                    { -v.Y, v.X, 0 }
        //                }
        //                );

        //            var m = new Matrix(Matrix.Identity(3)) + vx + (vx * vx) * ((1 - c) / (s * s));
        ///*            return new Matrix3D(
        //                m[0, 0], m[0, 1], m[0, 2], 0,
        //                m[1, 0], m[1, 1], m[1, 2], 0,
        //                m[2, 0], m[2, 1], m[2, 2], 0,
        //                0, 0, 0, 1
        //                );
        //*/
        //            return new Matrix3D(
        //                m[0, 0], m[1, 0], m[2, 0], 0,
        //                m[0, 1], m[1, 1], m[2, 1], 0,
        //                m[0, 2], m[1, 2], m[2, 2], 0,
        //                0, 0, 0, 1
        //                );
        //        }


        public static Matrix4x4 VectoToVector(Vector3 from, Vector3 to)
        {
            // 1. Normalizace obou vektorů
            Vector3 fromNorm = Vector3.Normalize(from);
            Vector3 toNorm = Vector3.Normalize(to);

            // 2. Skalární součin (Dot product) - odpovídá tvému 'c'
            float dot = Vector3.Dot(fromNorm, toNorm);

            // 3. Ošetření mezních stavů
            // Pokud vektory míří stejným směrem, rotace netřeba (Identity)
            if (dot > 0.999999f)
            {
                return Matrix4x4.Identity;
            }
            // Pokud míří přesně proti sobě (180 stupňů), musíme najít jakoukoli kolmou osu a otočit o 180°
            if (dot < -0.999999f)
            {
                Vector3 orthogonal = Vector3.Cross(fromNorm, Vector3.UnitX);
                if (orthogonal.LengthSquared() < 0.00001f)
                {
                    orthogonal = Vector3.Cross(fromNorm, Vector3.UnitY);
                }
                orthogonal = Vector3.Normalize(orthogonal);
                return Matrix4x4.CreateFromAxisAngle(orthogonal, MathF.PI);
            }

            // 4. Standardní případ: Vektorový součin nám dá osu rotace
            Vector3 cross = Vector3.Cross(fromNorm, toNorm);

            // Výpočet úhlu rotace v radiánech
            // Původně jsi měl: s = v.Length, c = dot. Úhel = atan2(s, c)
            float angle = (float)Math.Atan2(cross.Length(), dot);

            // 5. Vytvoření rotační matice kolem nalezené osy o spočítaný úhel
            Vector3 axis = Vector3.Normalize(cross);
            return Matrix4x4.CreateFromAxisAngle(axis, angle);
        }
        /// <summary>
        /// Rotacni transformace ktera vektor from pootoci do smeru vektoru to.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <remarks>
        /// https://math.stackexchange.com/questions/180418/calculate-rotation-matrix-to-align-vector-a-to-vector-b-in-3d
        /// </remarks>
        /*    public static Matrix3D VectoToVectorRodrigues(Vector3D from, Vector3D to)
                {
                    var ab = new Matrix( (from + to));
                    var abt = Matrix.Transpose(ab);
                    var m = 2*(ab * abt)/(abt*ab)[0,0]- new Matrix(Matrix.Identity(3));
                    return new Matrix3D(
                        m[0, 0], m[1, 0], m[2, 0], 0,
                        m[0, 1], m[1, 1], m[2, 1], 0,
                        m[0, 2], m[1, 2], m[2, 2], 0,
                        0, 0, 0, 1
                        );
                }
            }*/


        public static Matrix4x4 VectoToVectorRodrigues(Vector3 from, Vector3 to)
        {
            // 1. Normalizace a součet (from + to)
            Vector3 u = Vector3.Normalize(from) + Vector3.Normalize(to);

            // 2. Skalární součin u * u (ve spodku tvého zlomku)
            float dotU = Vector3.Dot(u, u);
            if (dotU < 0.000001f) return Matrix4x4.Identity; // Pojistka proti dělení nulou

            // 3. Výpočet koeficientu
            float scale = 2.0f / dotU;

            // 4. Přímé vytvoření matice (System.Numerics plní matice po řádcích, 
            // proto tvůj transponovaný zápis odpovídá tomuto přímému zadání)
            return new Matrix4x4(
                (scale * u.X * u.X) - 1f, (scale * u.Y * u.X), (scale * u.Z * u.X), 0f,
                (scale * u.X * u.Y), (scale * u.Y * u.Y) - 1f, (scale * u.Z * u.Y), 0f,
                (scale * u.X * u.Z), (scale * u.Y * u.Z), (scale * u.Z * u.Z) - 1f, 0f,
                0f, 0f, 0f, 1f
            );
        }
    }
}

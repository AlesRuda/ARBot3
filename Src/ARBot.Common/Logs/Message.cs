using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Point4D = ARBot.Common.Common.Point4D;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public abstract class Message
    {
        public Message(string name, int verze)
        {
            MsgName=name;
            Verze = verze;
        }
        public string MsgName { get; protected set; }
        public int Verze { get; set; }

        public abstract void ToData(BinaryWriter bw);
        public byte[] ToData(Encoding encoding)
        {
            byte[] data = new byte[0];
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter bw = new BinaryWriter(ms, encoding))
                {
                    ToData(bw);
                    bw.Flush();
                    // ToArray() vraci presne zapsane bajty; GetBuffer() by vratil cele pole
                    // vcetne nuloveho paddingu (u velkych zprav jako Blob zbytecne misto).
                    data = ms.ToArray();
                }
            }
            return data;
        }
        public abstract void FromData(BinaryReader br);

        public virtual void FromData(Encoding encoding, byte[] data)
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                using (BinaryReader br = new BinaryReader(ms, encoding))
                {
                    try
                    {
                        FromData(br);
                    }
                    catch(Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(MsgName +"-"+ex.ToString());
                        throw;
                    }
                }
            }
        }

        public abstract Message Build();
        public Message Build(Encoding encoding, byte[] data)
        {
            Message msg = Build();
            msg.FromData(encoding, data);
            return msg;
        }

        protected void Write(BinaryWriter bw, int? v)
        {
            bw.Write(v.HasValue);
            if (v.HasValue)
                bw.Write(v.Value);
        }
        protected void Write(BinaryWriter bw, double? v)
        {
            bw.Write(v.HasValue);
            if (v.HasValue)
                bw.Write(v.Value);
        }
        protected void Write(BinaryWriter bw, Int64? v)
        {
            bw.Write(v.HasValue);
            if (v.HasValue)
                bw.Write(v.Value);
        }

        protected void Write(BinaryWriter bw, Matrix<double> m)
        {
            bw.Write((short)m.RowCount);
            bw.Write((short)m.ColumnCount);

            for (int i = 0; i < m.RowCount; i++)
            {
                for (int j = 0; j < m.ColumnCount; j++)
                {
                    bw.Write(m[i, j]);
                }
            }
        }

        protected void Write(BinaryWriter bw, string[] a)
        {
            bw.Write((short)a.Length);

            foreach (var s in a)
                bw.Write(s);
        }

        protected void Write(BinaryWriter bw, Point2D v)
        {
            bw.Write(v.X);
            bw.Write(v.Y);
        }

        protected void Write(BinaryWriter bw, Point2D? v)
        {
            bw.Write(v.HasValue);
            if (v.HasValue)
                Write(bw, v.Value);
        }

        protected void Write(BinaryWriter bw, Point4D v)
        {
            bw.Write(v.X);
            bw.Write(v.Y);
            bw.Write(v.Z);
            bw.Write(v.A);
        }

        protected void Write(BinaryWriter bw, Point4D? v)
        {
            bw.Write(v.HasValue);
            if (v.HasValue)
                Write(bw, v.Value);
        }

        protected void Write(BinaryWriter bw, Point v)
        {
            bw.Write(v.X);
            bw.Write(v.Y);
        }

        protected void Write(BinaryWriter bw, Point? v)
        {
            bw.Write(v.HasValue);
            if (v.HasValue)
                Write(bw, v.Value);
        }

        protected void Write(BinaryWriter bw, Vector3 v)
        {
            bw.Write(v.X);
            bw.Write(v.Y);
            bw.Write(v.Z);
        }

        protected void Write(BinaryWriter bw, Vector3? v)
        {
            bw.Write(v.HasValue);
            if (v.HasValue)
                Write(bw, v.Value);
        }

        protected void Write(BinaryWriter bw, Quaternion v)
        {
            bw.Write(v.X);
            bw.Write(v.Y);
            bw.Write(v.Z);
            bw.Write(v.W);
        }

        protected void Write(BinaryWriter bw, Quaternion? v)
        {
            bw.Write(v.HasValue);
            if (v.HasValue)
                Write(bw, v.Value);
        }

        protected void Write(BinaryWriter bw, SensorStateBase s)
        {
            bw.Write(s.FrameNum);
            bw.Write(s.DropedOutNum);
            bw.Write(s.FrameReceivePeriod.Ticks);
            bw.Write(s.FramePickupPeriod.Ticks);
            bw.Write(s.TimeStamp.Ticks);
        }

        protected void Write(BinaryWriter bw, DateTime dt)
        {
            bw.Write(dt.ToBinary());
        }

        protected void Write(BinaryWriter bw, IMUState v)
        {
            bw.Write(v!=null);
            if (v != null)
            {
                Write(bw, (SensorStateBase)v);
                bw.Write(v.Confidence);
                Write(bw, v.Acceleration);
                Write(bw, v.Velocity);
                Write(bw, v.AngularAcceleration);
                Write(bw, v.AngularVelocity);
                Write(bw, v.Translation);
                Write(bw, v.Rotation);
            }
        }

        protected DateTime ReadDateTime(BinaryReader br)
        {
            return DateTime.FromBinary(br.ReadInt64());
        }

        protected int? ReadInt32(BinaryReader br)
        {
            bool n = br.ReadBoolean();
            if (n)
                return br.ReadInt32();
            return null; 
        }
        protected long? ReadInt64(BinaryReader br)
        {
            bool n = br.ReadBoolean();
            if (n)
                return br.ReadInt64();
            return null;
        }
        protected double? ReadDouble(BinaryReader br)
        {
            bool n = br.ReadBoolean();
            if (n)
                return br.ReadDouble();
            return null;
        }

        protected Matrix<double> ReadMatrixDouble(BinaryReader br)
        {
            int r = br.ReadInt16();
            int c = br.ReadInt16();
            double[,] a = new double[r, c];

            for (int i = 0; i < r; i++)
            {
                for (int j = 0; j < c; j++)
                {
                    a[i, j] = br.ReadDouble();
                }
            }
            return Matrix<double>.Build.DenseOfArray(a);
        }

        protected Vector3 ReadVector3(BinaryReader br)
        {
            var v = new Vector3();
            v.X = br.ReadSingle();
            v.Y = br.ReadSingle();
            v.Z = br.ReadSingle();
            return v;
        }

        protected Vector3? ReadNullableVector3(BinaryReader br)
        {
            if (br.ReadBoolean())
                return ReadVector3(br);
            return null;
        }
        protected Point2D ReadPoint2D(BinaryReader br)
        {
            var v = new Point2D();
            v.X = br.ReadSingle();
            v.Y = br.ReadSingle();
            return v;
        }

        protected Point2D? ReadNullablePoint2D(BinaryReader br)
        {
            if (br.ReadBoolean())
                return ReadPoint2D(br);
            return null;
        }

        protected Point4D ReadPoint4D(BinaryReader br)
        {
            var v = new Point4D();
            v.X = br.ReadSingle();
            v.Y = br.ReadSingle();
            v.Z = br.ReadSingle();
            v.A = br.ReadSingle();
            return v;
        }

        protected Point4D? ReadNullablePoint4D(BinaryReader br)
        {
            if (br.ReadBoolean())
                return ReadPoint4D(br);
            return null;
        }

        protected Point ReadPoint(BinaryReader br)
        {
            var v = new Point();
            v.X = br.ReadInt32();
            v.Y = br.ReadInt32();
            return v;
        }

        protected Point? ReadNullablePoint(BinaryReader br)
        {
            if (br.ReadBoolean())
                return ReadPoint(br);
            return null;
        }


        protected Quaternion ReadQuaternion(BinaryReader br)
        {
            var v = new Quaternion();
            v.X = br.ReadSingle();
            v.Y = br.ReadSingle();
            v.Z = br.ReadSingle();
            v.W = br.ReadSingle();
            return v;
        }

        protected Quaternion? ReadNullableQuaternion(BinaryReader br)
        {
            if (br.ReadBoolean())
                return ReadQuaternion(br);
            return null;
        }

        protected string[] ReadStringArray(BinaryReader br)
        {
            var cnt=br.ReadInt16();

            string[] a = new string[cnt];

            for (int i = 0; i < cnt; i++)
                a[i] = br.ReadString();

            return a;
        }


        protected void SensorStateBase(BinaryReader br, SensorStateBase s)
        {
            s.FrameNum = br.ReadUInt32();
            s.DropedOutNum = br.ReadUInt32();
            s.FrameReceivePeriod=new TimeSpan(br.ReadInt64());
            s.FramePickupPeriod = new TimeSpan(br.ReadInt64());
            s.TimeStamp = new DateTime(br.ReadInt64());
        }

        protected IMUState ReadIMUState(BinaryReader br)
        {
            if (!br.ReadBoolean())
                return null;
            var v = new IMUState();
            SensorStateBase(br, v);
            v.Confidence= br.ReadDouble();
            v.Acceleration = ReadNullableVector3(br);
            v.Velocity = ReadNullableVector3(br);
            v.AngularAcceleration = ReadNullableVector3(br);
            v.AngularVelocity = ReadNullableVector3(br);
            v.Translation = ReadNullableVector3(br);
            v.Rotation = ReadNullableQuaternion(br);
            return v;
        }
    }
}

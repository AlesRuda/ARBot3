using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using System.Globalization;
using ThreadSafeCollection;
using System.IO;
using System.Xml.Serialization;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;

namespace ARBot.Driver
{
    [Serializable()]
    public class ARBotStateCollection : ThreadSafeObservableCollection<ARBotState>
    {
        public class Module
        {
            public int ID { get; set; }
            public string Name { get; set; }
            public bool Enabled { get; set; }
            public double CPU { get; set; }

            public void Parse(string line)
            {
                string[] sa = line.Split(new string[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
                ID = int.Parse(sa[0]);
                Name = sa[1];
                Enabled = int.Parse(sa[2]) != 0;
                if(sa.Length>=4)
                    CPU = double.Parse(sa[3], CultureInfo.InvariantCulture);
            }
        }

        [XmlIgnore]
        public ARBot.Common.Maps.Map Map { get; set; }
        [XmlIgnore]
        public ThreadSafeWrapperCollection<Message> Info = null;

        [XmlIgnore]
        public MessageCollection MsgCol { get; private set; }

        public ARBotStateCollection():this(new MessageCollection())
        {
        }

        public ARBotStateCollection(MessageCollection col)
        {
            MsgCol = col;
            foreach (Message m in col)
                Add(m);

            Info=new ThreadSafeWrapperCollection<Message>(col);
            col.CollectionChanged += col_CollectionChanged;
        }

        void col_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                throw new NotSupportedException("mazani neni podporovano");
            if (e.NewItems != null)
                foreach (Message m in e.NewItems)
                    Add(m);
        }

        ARBotState tempState = null;

        ARBotState Collect(Message msg)
        {
            ARBotState ret=null;
            if (msg is State)
            {
                if (tempState != null)
                {
                    tempState.SetFromState(msg as State);
                    ret=tempState;
                }
                tempState = new ARBotState(tempState);
            }
            if (tempState != null)
            {
                tempState.Msgs.Add(msg);
                if (msg is Blob)
                    tempState.AddBlob(msg as Blob);
                if (msg is Marker)
                    tempState.AddMarker(msg as Marker);
                /*                if (msg is Module)
                                    this[Count - 1].Modules.Add(msg as Blob);*/
                if (msg is ARBot.Common.Logs.VFH)
                    tempState.VFH = msg as ARBot.Common.Logs.VFH;
                if (msg is ARBot.Common.Logs.EKFStepMsg)
                    tempState.EKFStep = msg as ARBot.Common.Logs.EKFStepMsg;
                if (msg is ARBot.Common.Logs.ICPMsg)
                    tempState.ICP = msg as ARBot.Common.Logs.ICPMsg;
                if (msg is ARBot.Common.Logs.ColliderMsg)
                    tempState.Collider = msg as ARBot.Common.Logs.ColliderMsg;
                var gnm = msg as ARBot.Common.Logs.GraphNavigationMsg;
                if (gnm!=null && (gnm.Name==null || gnm.Name == "GN"))
                    tempState.GraphNavigation = gnm;
                if (gnm != null && gnm.Name == "Map")
                    tempState.Map = gnm;
                if (msg is ARBot.Common.Logs.Lidar)
                {
                    ARBot.Common.Logs.Lidar l = msg as ARBot.Common.Logs.Lidar;
                    if (!tempState.Lidar.ContainsKey(l.Name))
                        tempState.Lidar.Add(l.Name, l);
                    else
                        tempState.Lidar[l.Name] = l;
                }
                if (msg is PathEdgeMsg)
                {
                    PathEdgeMsg pe = msg as PathEdgeMsg;
                    if (!tempState.PathEdges.ContainsKey(pe.Name))
                        tempState.PathEdges.Add(pe.Name, pe);
                    else
                        tempState.PathEdges[pe.Name] = pe;
                }
            }
            return ret;
        }

        void Add(Message msg)
        {
            ARBotState s = Collect(msg);
            if (s!=null)
            {
                    this.Add(s);
            }
        }

        public static ARBotStateCollection Deserialize(string fn)
        {
            ARBotStateCollection c = null;
            using (Stream stream = new FileStream(fn, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read))
            {
                XmlSerializer xmlserializer = new XmlSerializer(typeof(ARBotStateCollection), new Type[] { typeof(ARBotState) });
                c = xmlserializer.Deserialize(stream) as ARBotStateCollection;
                ARBotState p=null;
                foreach (ARBotState s in c)
                {
                    s.Owner = c;
                    s.Previous = p;
                    p = s;
                }

            }
            return c;
        }

/*        public void PostProcess1()
        {
            IFilter yuvHist = new YUVHistFilter();
            CameraFilter camera = new CameraFilter();
            if (Map != null)
            {
                ARBot.Common.Coordinates.Transform t = new ARBot.Common.Coordinates.Transform(new ECEF(Ellipsoid.Wgs84, Map.Points[0].LLA), true);

                foreach (ARBotState s in this)
                {
                    camera.SetState(s);

                    BitmapSource bs = Map.DrawMap(128, 128, s.MapX, s.MapY, CameraFilter.r, Colors.White, t);
                    ARBotBlob b = new ARBotBlob() { Data = ARBotState.GetGrayPixels(bs), Height = bs.PixelHeight, Width = bs.PixelWidth, Type = ARBot.Driver.ARBotBlob.BlobType.Gray, Name = "Map draw" };
                    s.AddFullBlob(b);

                    ARBotImage i = null;
                    if (s.JpgBytes != null)
                        i = ARBotImage.FromRGBFile(s.JpgBytes).ConvertTo<YUV>((c1) => new YUV(c1));
                    else
                    {
                        ARBotBlob b1 = s.FullBlobs(this).FirstOrDefault((mm) => mm.Name == "Camera");
                        if (b1 != null)
                            i = b1.ImageYUV();
                    }

                    if (i != null)
                    {
                        ARBotImage<GrayPixel> g = (ARBotImage<GrayPixel>)yuvHist.Apply(i);
                        ARBotBlob b2 = new ARBotBlob(camera.Apply(g) as ARBotImage<BayesGrayPixel>);
                        b2.Name = "LM";
                        s.AddFullBlob(b2);


                        Int64 sumsum = 0, sumx = 0, sumx2 = 0, sumy = 0, sumy2 = 0, sumxy = 0;
                        int kw = b.Width - b2.Width;
                        int kh = b.Height - b2.Height;
                        byte[] p1 = b.Data;
                        byte[] p2 = b2.Data;
                        int[] p3 = new int[kw * kh];
                        int maxsum = 0;
                        for (int kx = 0; kx < kw; kx++)
                        {
                            for (int ky = 0; ky < kh; ky++)
                            {
                                int sum = 0;
                                for (int y = 0; y < b2.Height; y++)
                                {
                                    int idx1 = kx + (ky + y) * b.Width;
                                    int idx2 = y * b2.Width;
                                    for (int x = 0; x < b2.Width; x++)
                                        sum += (int)p1[idx1 + x] * (int)p2[idx2 + x];
                                }
                                p3[kx + ky * kw] = sum;
                                if (maxsum < sum)
                                    maxsum = sum;

                                int dx=kx-kw/2;
                                int dy=ky-kh/2;

                                sumsum += sum;
                                sumx += dx * sum;
                                sumy += dy * sum;
                                sumx2 += dx*dx * sum;
                                sumy2 += dy*dy * sum;
                                sumxy += dx * dy * sum;
                            }
                        }

                        byte[] p4 = new byte[kw * kh];
                        if (maxsum != 0)
                        {
                            for (int kx = 0; kx < kw; kx++)
                            {
                                for (int ky = 0; ky < kh; ky++)
                                {
                                    p4[kx + ky * kw] = (byte)((255 * (Int64)p3[kx + ky * kw]) / maxsum);
                                }
                            }
                        }
                        ARBotBlob b3 = new ARBotBlob() { Data = p4, Width = kw, Height = kh, Name = "Corel", Type = ARBotBlob.BlobType.Gray };
                        s.AddFullBlob(b3);

                        if (sumsum != 0)
                        {
                            double ax = (double)sumx / (double)sumsum;
                            double ay = (double)sumy / (double)sumsum;
                            double sx = (double)sumx2 / (double)sumsum - ax * ax;
                            double sy = (double)sumy2 / (double)sumsum - ay * ay;
                            double sxy = (double)sumxy / (double)sumsum - ax * ay;

                            s.MapCorX = ax * CameraFilter.r * 0.01;
                            s.MapCorY = -ay * CameraFilter.r * 0.01;
                        }
                    }
                }
            }
        }
 */
        public new void Clear()
        {
            base.Clear();
            MsgCol.Clear();
        }

        public static string ToGPX(IEnumerable<ARBotState> states, bool gps)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(@"<gpx creator=""ARBot"" version=""1.1"">
  <trk>
    <name>Log</name>
    <trkseg>");
            foreach (var s in states)
            {
                var str = s.ToGPX(gps);
                if(str!=null)
                    sb.AppendLine(str);
            }

            sb.AppendLine(@"</trkseg>
  </trk>
</gpx>");
            return sb.ToString();
        }
        public string ToGPX(bool gps)
        {
            return ToGPX(this, gps);
        }
        public void ResetEKF()
        {
            var fs = this.First();
//            ARBotState.Model.SetOrietantionPosition(fs.ARBotHeading, fs.ARBotX, fs.ARBotY);
//            ARBotState.Model.SetOrietantionPosition(0, 0, 0);

            foreach (var i in this)
                i.ResetEKF();
            ARBotState.Model.Step.Index = 0;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.ComponentModel;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Common;
using ARBot.Common.Models;
using ARBot.Common;
using System.IO;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;

namespace ARBot.Driver
{
    [Serializable()]
    public class ARBotState
    {
        public ARBotState()
        {
        }

        public ARBotState(ARBotState prev)
        {
            Msgs = new List<Message>();
            Lidar = new Dictionary<string, Common.Logs.Lidar>();
            PathEdges = new Dictionary<string, PathEdgeMsg>();
            Previous = prev;
        }

        public void SetFromState(State state)
        {
            Time = state.Time;
            Ts = state.Ts;
            ARBotSpeed = state.Speed;
            ARBotHeading = state.Orientation;
            ARBotX = state.X;
            ARBotY = state.Y;
            ReqSpeed = state.ReqSpeed;
            ReqRotationSpeed = state.ReqRotationSpeed;
            ReqRotationAcceleration = state.ReqRotationAcceleration;
            Yaw = Conversions.Rad2Deg(state.Yaw);
            Pitch = Conversions.Rad2Deg(state.Pitch);
            Roll = Conversions.Rad2Deg(state.Roll);

            TrackingCameraState = state.TrackingCametaState;
            TimeStamp = state.TimeStamp;
            IMUState = state.IMUState;
            LeftWheelSpeed = state.LeftWheelSpeed;
            RightWheelSpeed = state.RightWheelSpeed;
            LeftMotorsCurrent = state.LeftMotorsCurrent;
            RightMotorsCurrent = state.RightMotorsCurrent;
            MotorsVoltage = state.MotorsVoltage;
            CPUUtilization = state.CPUUtilization;
            Sonars = state.Sonars;

            GPSFix = state.GPSFix;
            GPSFlags = state.GPSFlags;
            GPSnumSV = state.GPSnumSV;
            GPSpDOP = state.GPSpDOP;
            GPSLatitude = state.GPSLatitude;
            GPSLongitude = state.GPSLongitude;
            GPSOriantation = state.GPSOriantation;
            GPSSpeed = state.GPSSpeed;
            GPSUtc = state.GPSUTC;
            GpsXY = state.GPSLocation;

            RefLatitude = Conversions.Rad2Deg(state.RefLatitude);
            RefLongitude = Conversions.Rad2Deg(state.RefLongitude);

            WayIndex = state.WayIndex;
            PointIndex = state.PointIndex;

            PointDistance = state.PointDistance;
            TargetDistance = state.TargetDistance;
            WayDistance = state.WayDistance;


            LocalMapCorrelX = state.LocalMapCorrelX;
            LocalMapCorrelY = state.LocalMapCorrelY;

            TrackinkCameraRotationSpeed = state.TrackinkCameraRotationSpeed;
            TrackinkCameraSpeed = state.TrackinkCameraSpeed;
            OdometryRotationSpeed = state.OdometryRotationSpeed;
            CompasRotationSpeed = state.CompasRotationSpeed;
            CameraOrientation = state.CameraOrientation;
            CameraToWayOrientation = state.CameraToWayOrientation;

            SumLocalMapCorrelX = (Previous?.SumLocalMapCorrelX??0) + LocalMapCorrelX;
            SumLocalMapCorrelY = (Previous?.SumLocalMapCorrelY??0) + LocalMapCorrelY;
        }


        [Browsable(false)]
        [XmlIgnore()]
        public ARBotStateCollection Owner { get; set; }

        [Browsable(false)]
        [XmlIgnore()]
        public ARBotState Previous { get; set; }

        public DateTime TimeStamp { get; set; }
        public int Time { get; set; }
        public double Ts { get; set; }
        public double ARBotX { get; set; }
        public double ARBotY { get; set; }
        public double ReqSpeed { get; set; }
        public double ReqRotationSpeed { get; set; }
        public double Yaw { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }

        public IMUState TrackingCameraState { get; set; }
        public IMUState IMUState { get; set; }


        public double LeftWheelSpeed { get; set; }
        public double RightWheelSpeed { get; set; }
        public double LeftMotorsCurrent { get; set; }
        public double RightMotorsCurrent { get; set; }
        public double MotorsVoltage { get; set; }

        public double CPUUtilization { get; set; }

        public ARBot.Common.Logs.VFH VFH;
        public ARBot.Common.Logs.EKFStepMsg EKFStep;
        public ICPMsg ICP;
        public ColliderMsg Collider;
        public GraphNavigationMsg GraphNavigation;
        /// <summary>
        /// mapa okoli robotu
        /// </summary>
        public GraphNavigationMsg Map;

        public Dictionary<string, Lidar> Lidar;
        public Dictionary<string, PathEdgeMsg> PathEdges;
        public List<Message> Msgs;

        /// <summary>
        /// Matematicka orientace robotu v radianech. 0 na vychod, PI/2 na sever
        /// </summary>
        public double ARBotHeading { get; set; }
        public double ARBotSpeed { get; set; }
        public State.SonarState[] Sonars { get; set; }

        public int GPSiTOW { get; set; }
        public int GPSfTOW { get; set; }
        public int GPSWeek { get; set; }
        public int GPSFix { get; set; }
        public int GPSFlags { get; set; }
        public double GPSpDOP { get; set; }
        public int GPSnumSV { get; set; }
        public DateTime? GPSUtc { get; set; }

        /// <summary>
        /// Smer urceny z GPS, udaj z VTG zpravy
        /// Orientace v radianech a matematickem smyslu.
        /// </summary>
        public double? GPSOriantation { get; set; }
        /// <summary>
        /// Rychlost urceny z GPS v m/s, udaj z VTG zpravy
        /// </summary>
        public double? GPSSpeed { get; set; }

        /// <summary>
        /// Zemepisna sirka ve stupnich
        /// </summary>
        public double GPSLatitude { get; set; }
        /// <summary>
        /// Zemepisna delka ve stupnich
        /// </summary>
        public double GPSLongitude { get; set; }

        public double RefLatitude { get; set; }
        public double RefLongitude { get; set; }

        public long PointIndex { get; set; }
        public long WayIndex { get; set; }
        public double TargetDistance { get; set; }
        public double PointDistance { get; set; }
        public double WayDistance { get; set; }
        public double LocalMapCorrelX { get; set; }
        public double LocalMapCorrelY { get; set; }
        public double SumLocalMapCorrelX { get; set; }
        public double SumLocalMapCorrelY { get; set; }

        /// <summary>
        /// Rotacni rychlost spoctena z trekovaci kamery ve stupnich/s
        /// </summary>
        public double? TrackinkCameraRotationSpeed { get; set; }
        /// <summary>
        /// Rychlost urcena z trekovaci kamery v/s
        /// </summary>
        public double? TrackinkCameraSpeed { get; set; }
        /// <summary>
        /// Rotacni rychlost spoctena z odometrie ve stupnich/s
        /// </summary>
        public double? OdometryRotationSpeed { get; set; }
        /// <summary>
        /// Rotacni rychlost spoctena z kompasu ve stupnich/s
        /// </summary>
        public double? CompasRotationSpeed { get; set; }
        /// <summary>
        /// Orientace robotu spoctena ze smeru cesty v obraze a smeru cesty podle mapy.
        /// Udaj je v radianech v matematickem smyslu.
        /// </summary>
        public double? CameraOrientation { get; set; }
        /// <summary>
        /// Orientace kamery relativne k ceste v radianech a matematickem smyslu.
        /// </summary>
        public double? CameraToWayOrientation { get; set; }
        /// <summary>
        /// Pozadovana rotacni akcelerace v deg/s^2
        /// </summary>
        public double? ReqRotationAcceleration { get; set; }


        [Browsable(false)]
        public List<Marker> Markers { get; set; }

        public void AddMarker(Marker m)
        {
            fullMarkers = null;
            if (Markers == null)
                Markers = new List<Marker>();
            Marker m1 = Markers.FirstOrDefault((mm) => mm.Name == m.Name);
            if (m1 != null)
                Markers.Remove(m1);
            Markers.Add(m);
        }


        [XmlIgnore()]
        private List<Marker> fullMarkers;
        public List<Marker> FullMarkers()
        { 
            if(fullMarkers==null)
            {
                if (Previous != null)
                {
                    if (Previous.fullMarkers == null)
                    {
                        Stack<ARBotState> stack = new Stack<ARBotState>();
                        ARBotState s = Previous;
                        while (s != null && s.fullMarkers == null)
                        {
                            stack.Push(s);
                            s = s.Previous;
                        }
                        while (stack.Count > 0)
                            stack.Pop().FullMarkers();
                    }

                    fullMarkers = new List<Marker>(Previous.FullMarkers());
                }
                else
                    fullMarkers = new List<Marker>();
                /*
                                Marker m1 = fullMarkers.FirstOrDefault((mm) => mm.Name == "ARBot");
                                if (m1 != null)
                                    fullMarkers.Remove(m1);
                                fullMarkers.Add(new Marker() { Name = "ARBot", X = ARBotX, Y = ARBotY, Type = Marker.MarkerType.Cross });
                                */
                Marker m1 = fullMarkers.FirstOrDefault((mm) => mm.Name == "Reference");
                if (m1 != null)
                    fullMarkers.Remove(m1);
                fullMarkers.Add(new Marker() { Name = "Reference", X = 0, Y = 0, Type = Marker.MarkerType.Cross });

                m1 = fullMarkers.FirstOrDefault((mm) => mm.Name == "NotCorected");
                if (m1 != null)
                    fullMarkers.Remove(m1);
                fullMarkers.Add(new Marker() { Name = "NotCorected", X = ARBotX-SumLocalMapCorrelX, Y = ARBotY - SumLocalMapCorrelY, Type = Marker.MarkerType.Cross });

                m1 = fullMarkers.FirstOrDefault((mm) => mm.Name == "EKF");
                if (m1 != null)
                    fullMarkers.Remove(m1);
                fullMarkers.Add(new Marker() { Name = "EKF", X = Step.PredictedState.X, Y = Step.PredictedState.Y, Type = Marker.MarkerType.Cross });

                m1 = fullMarkers.FirstOrDefault((mm) => mm.Name == "GPS");
                if (m1 != null)
                    fullMarkers.Remove(m1);
                if(GpsXY!=null)
                    fullMarkers.Add(new Marker() { Name = "GPS", X = GpsXY.Value.X, Y = GpsXY.Value.Y, Type = Marker.MarkerType.Cross });


                if (Markers != null)
                {
                    foreach (Marker m in Markers)
                    {
                        m1 = fullMarkers.FirstOrDefault((mm) => mm.Name == m.Name);
                        if (m1 != null)
                            fullMarkers.Remove(m1);
                        fullMarkers.Add(m);
                    }
                }
            }
            return fullMarkers;
        }

        [Browsable(false)]
        public List<Blob> Blobs { get; set; }

        [XmlIgnore()]
        private List<Blob> fullBlobs;
        public List<Blob> FullBlobs()
        {
            if (fullBlobs == null)
            {
                if (Previous != null)
                {
                    if (Previous.fullBlobs == null)
                    {
                        Stack<ARBotState> stack = new Stack<ARBotState>();
                        ARBotState s = Previous;
                        while (s != null && s.fullBlobs == null)
                        {
                            stack.Push(s);
                            s = s.Previous;
                        }
                        while (stack.Count > 0)
                            stack.Pop().FullBlobs();
                    }
                    fullBlobs = new List<Blob>(Previous.FullBlobs());
                }
                else
                    fullBlobs = new List<Blob>();
                if (Blobs != null)
                {
                    foreach (Blob b in Blobs)
                    {
                        Blob m1 = fullBlobs.FirstOrDefault((mm) => mm.Name == b.Name);
                        if (m1 != null)
                            fullBlobs.Remove(m1);
                        fullBlobs.Add(b);
                    }
                }
            }
            return fullBlobs;
        }

        public void AddBlob(Blob b)
        {
            fullBlobs = null;
            if (Blobs == null)
                Blobs = new List<Blob>();
            Blob m1 = Blobs.FirstOrDefault((mm) => mm.Name == b.Name);
            if (m1 != null)
                Blobs.Remove(m1);
            Blobs.Add(b);
        }


        [XmlIgnore()]
        [Browsable(false)]
        public LLA GpsLLA
        {
            get
            {
                return new LLA(Conversions.Deg2Rad(GPSLatitude), Conversions.Deg2Rad(GPSLongitude));
            }
        }

        [XmlIgnore()]
        [Browsable(false)]
        public Point2D? GpsXY { get; set; }
/*        {
            get
            {
                if (GPSFix < 2)
                    return null;
                ECEF t = ReferenceTransform.Transform(new ECEF(Ellipsoid.Sphere, GpsLLA));
                return new Point2D(t.Y, t.Z);
            }
        }
*/
        public double? GpsX
        {
            get
            {
                return GpsXY?.X;
            }
        }

        public double? GpsY
        {
            get
            {
                return GpsXY?.Y;
            }
        }

        private LLA arBotLLA;
        [XmlIgnore()]
        [Browsable(false)]
        public LLA ARBotLLA
        {
            get
            {
                if(object.ReferenceEquals(arBotLLA, null))
                    arBotLLA = new LLA(Ellipsoid.Sphere, ARBotECEF);
                return arBotLLA;
            }
        }

        /// <summary>
        /// Souradnice robotu podle EKF
        /// </summary>
        public LLA ARBotEKF_LLA
        {
            get
            {
                return new LLA(Ellipsoid.Sphere, ARBotEKF_ECEF); ;
            }
        }


        private ARBot.Common.Coordinates.Transformation referenceRevTransform;
        [XmlIgnore()]
        [Browsable(false)]
        /// <summary>
        /// Transformace ze souradnic robotu do globalnich
        /// </summary>
        public ARBot.Common.Coordinates.Transformation ReferenceRevTransform
        {
            get
            {
                if (object.ReferenceEquals(referenceRevTransform, null))
                    referenceRevTransform = new ARBot.Common.Coordinates.Transformation(Conversions.Deg2Rad(RefLatitude), Conversions.Deg2Rad(RefLongitude), true);
                return referenceRevTransform;
            }
        }
        private ARBot.Common.Coordinates.Transformation referenceTransform;
        [XmlIgnore()]
        [Browsable(false)]
        /// <summary>
        /// Transformace ECEF urcene z LLA GPS na lokalni souradnice robotu [ECEF.X, ECEF.Y, ECEF.Z]->[cca SemiMajorAxis, ARBot.X, ARBot.Y].
        /// </summary>
        public ARBot.Common.Coordinates.Transformation ReferenceTransform
        {
            get
            {
                if (object.ReferenceEquals(referenceTransform, null))
                    referenceTransform = new ARBot.Common.Coordinates.Transformation(Conversions.Deg2Rad(RefLatitude), Conversions.Deg2Rad(RefLongitude), false);
                return referenceTransform;
            }
        }



        private ECEF arBotECEF;
        [XmlIgnore()]
        [Browsable(false)]
        public ECEF ARBotECEF
        {
            get
            {
                if(object.ReferenceEquals(arBotECEF, null))
                    arBotECEF=ReferenceRevTransform.Transform(new ECEF() { X = Ellipsoid.Sphere.SemiMajorAxis, Y = ARBotX, Z = ARBotY });
                return arBotECEF;
            }
        }

        /// <summary>
        /// Souradnice robotu podle EKF
        /// </summary>
        public ECEF ARBotEKF_ECEF
        {
            get
            {
                return ReferenceRevTransform.Transform(new ECEF() { X = Ellipsoid.Sphere.SemiMajorAxis, Y = Step?.CurrentState?.X??0, Z = Step?.CurrentState?.Y ?? 00 });
//                return new ECEF();
            }
        }

        /// <summary>
        /// v radianech a matematickem smyslu
        /// </summary>
        public double? CameraYaw
        {
            get
            {
                if (CameraOrientation != null)
                    return CameraOrientation;
                if (!PathEdges.ContainsKey("Road"))
                    return null;
                var pe = PathEdges["Road"];
                if (Map == null)
                    return null;
                var w = Map.Edges.FirstOrDefault(i => i.ID == WayIndex);
                if (pe == null || w == null || pe.Angle == null || w.Line==null)
                    return null;
//                return Conversions.Rad2Deg(Conversions.Orientation2Azimut( w.Line.Angle));
                return Conversions.NormalizeHalfOrientation(Conversions.Azimut2Orientation(Conversions.Deg2Rad(-Yaw)) - pe.Angle.Value) + w.Line.Angle;
            }
        }




        public override string ToString()
        {
            return string.Format("X={0}, Y={1}, Heading={2}", ARBotX, ARBotY, ARBotHeading/Math.PI*180);
        }

        /// <summary>
        /// Jeden nod do GPX
        /// </summary>
        /// <returns></returns>
        public string ToGPX(bool gps)
        {
            LLA lla;
            if (gps)
                lla = GpsLLA;
            else
                lla = ARBotLLA;
            if(lla.Latitude!=0 && lla.Longitude!=0)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, @"<trkpt lat=""{0}"" lon=""{1}"" ><time>{2:yyyy-MM-ddTHH:mm:ss.fffZ}</time></trkpt>", Conversions.Rad2Deg(lla.Latitude), Conversions.Rad2Deg(lla.Longitude), TimeStamp);
            return null;
        }

        private List<Blob> GetToSave()
        {
            var blobs = FullBlobs().ToList();

            var left = blobs.FirstOrDefault(b => b.Name == "Left_Camera");
            var right = blobs.FirstOrDefault(b => b.Name == "Right_Camera");
            if(left!=null && right!=null && left.Width==right.Width && left.Height==right.Height)
            {
                var li = left.ToBGR32Image();
                var ri = right.ToBGR32Image();

                var i = new Image<BGR32>(li.Width * 2, li.Height);
                for(int x=0;x<li.Width;x++)
                {
                    for (int y = 0; y < li.Height; y++)
                    {
                        i[x, y].Color = li[x, y].Color;
                        i[x+li.Width, y].Color = ri[x, y].Color;
                    }
                }

                blobs.Add(Blob.FromImage("Camera", i, true));

            }
            return blobs;
        }

        static int cnt = 1;
        public void SaveImages(string prefix)
        {
            var blobs = GetToSave().Select(b => new { Name = prefix + cnt.ToString() + "_" + b.Name + (b.Type == Blob.BlobType.Jpeg ? ".jpg" : ".blob"), Blob = b });
            while (blobs.Any(b => File.Exists(b.Name)))
                cnt++;
            foreach (var b in blobs)
            {
                if (b.Blob.Type == Blob.BlobType.Jpeg)
                    File.WriteAllBytes(b.Name, b.Blob.Data);
                else
                {
                    using (FileStream s = new FileStream(b.Name, FileMode.Create))
                    {
                        using (var w = new MessageWriter(s, Encoding.UTF8))
                        {
                            w.Write(b.Blob);
                        }
                    }
                }
            }
        }

        //private EKFStep<EKFModel2State, EKFModel2Measurement, EKFModel2Input> step;
        //public static EKFModel2 Model = new EKFModel2(null, Profile.Rozchod);
        //public EKFStep<EKFModel2State, EKFModel2Measurement, EKFModel2Input> Step
        //{
        //    get
        //    {
        //        if (step == null)
        //        {
        //            if (Previous?.Step != null)
        //                Model.Step = Previous.Step;


        //            EKFModel2Input u = new EKFModel2Input()
        //            {
        //                Pitch = Pitch,
        //                Ts = Ts,
        //                TimeStamp = TimeBase.Now
        //            };
        //            /*            EKFModel2Measurement yy = new EKFModel2Measurement();
        //                        yy.in_Mat = CalcOutput(Step.PredictedState, u).in_Mat;
        //            */
        //            EKFModel2Measurement y = new EKFModel2Measurement()
        //            {
        //                CompasOrientation = Conversions.Azimut2Orientation(Conversions.Deg2Rad(-Yaw)),
        //                GPSOriantation = (RightWheelSpeed + LeftWheelSpeed) / 2 > 0.3 ? GPSOriantation ?? double.NaN : double.NaN,
        //                CameraOrientation = CameraOrientation ?? double.NaN,

        //                CompasRotationSpeed = CompasRotationSpeed ?? double.NaN,
        //                OdometryRotationSpeed = OdometryRotationSpeed ?? double.NaN,
        //                TrackinkCameraRotationSpeed = TrackinkCameraRotationSpeed ?? double.NaN,

        //                OdometrySpeed = (RightWheelSpeed + LeftWheelSpeed) / 2,
        //                GPSSpeed = GPSSpeed ?? double.NaN,
        //                TrackinkCameraSpeed = TrackingCameraState?.Velocity?.Length ?? double.NaN,


        //                LocalMap_X = ARBotX + LocalMapCorrelX,
        //                LocalMap_Y = ARBotY + LocalMapCorrelY,
        //                GPS_X = GpsXY?.X ?? double.NaN,
        //                GPS_Y = GpsXY?.Y ?? double.NaN
        //            };



        //            Model.Update(y, u);
        //            step = Model.Step;
        //        }
        //        return step;
        //    }
        //}

// maximalni rotacni akcelerace je ve skutecnosti dana nastavenim ridici jednotky motoru a je vetsi
//        public static EKFModel3 Model = new EKFModel3(Profile.MaxAcceleration/Profile.Rozchod, Profile.MaxAcceleration);

        private T Process<T>(Func<ARBotState, T> get, Action<ARBotState> calc)
        {
            var v = get(this);
            if (v != null)
                return v;

            Queue<ARBotState> q = new Queue<ARBotState>();
            ARBotState s = this;
            while (s != null && get(s) == null)
            {
                q.Enqueue(s);
                s = s.Previous;
            }

            while (q.Count > 0)
            {
                s = q.Dequeue();
                calc(s);
            }
            return get(this);
        }

//        private EKFStep<EKFModel3State, EKFModel3Measurement, EKFModel3Input> step;
//        public EKFStep<EKFModel3State, EKFModel3Measurement, EKFModel3Input> Step 
//        { 
//            get
//            {
//                return Process((s) => s.step, (s) =>
//                  {
//                      if (Previous == null)
//                      {
//                          Model = new EKFModel3(Profile.MaxAcceleration / Profile.Rozchod, Profile.MaxAcceleration);
////                          Model.SetOrietantionPosition(0, 0, 0);
//                          Model.SetOrietantionPosition(ARBotHeading, ARBotX, ARBotY);
//                      }

//                      if (Previous?.step != null)
//                          Model.Step = Previous.step;

//                      Model.Update(this);
//                      step = Model.Step;
//                  });
//            }
//        }

//        public void ResetEKF()
//        {
//            step = null;
//            fullMarkers = null;
//        }
    }
}

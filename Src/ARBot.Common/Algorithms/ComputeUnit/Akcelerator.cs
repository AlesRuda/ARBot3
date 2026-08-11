using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace ARBot.Common.Common
{

    public class Akcelerator
    {
        [DllImport("AkceleratorDll.dll", EntryPoint = "ComputeAlloc", SetLastError = true, CallingConvention =CallingConvention.Winapi)]
        private static extern IntPtr ComputeAlloc(int maxPoints, int width, int height, int xOff, int yOff, float resolution);
        [DllImport("AkceleratorDll.dll", EntryPoint = "ComputeFree", SetLastError = true)]
        private static extern void ComputeFree(IntPtr ci);

        [DllImport("AkceleratorDll.dll", EntryPoint = "Segment2", SetLastError = true)]
        private static extern void Segment2(IntPtr ci, byte[] leftDist, float[] leftTransformMatrix, Point2D[,] leftTransform,
            byte[] rightDist, float[] rightTransformMatrix, Point2D[,] rightTransform,
            float[] globalTransformMatrix,
            int len, float maxZ);

        private IntPtr computeInfoPtr;
        ComputeInfo? computeInfo;
        public ComputeInfo ComputeInfo
        {
            get
            {
                if(computeInfo==null)
                    computeInfo=(ComputeInfo)Marshal.PtrToStructure(computeInfoPtr, typeof(ComputeInfo));
                return computeInfo.Value;
            }
        }

        Point4D[] obstaclePoints;
        public Point4D[] ObstaclePoints
        {
            get
            {
                if (obstaclePoints == null)
                {
                    var ci = ComputeInfo;
                    float[] f = new float[ci.ObstaclePointsCount * 4];
                    Marshal.Copy(ci.ObstaclePointsPtr, f, 0, ci.ObstaclePointsCount * 4);
                    Point4D[] o = new Point4D[ci.ObstaclePointsCount];
                    for (int i = 0; i < ci.ObstaclePointsCount; i++)
                    {
                        o[i] = new Point4D() { X = f[i * 4], Y = f[i * 4 + 1], Z = f[i * 4 + 2], A = f[i * 4 + 3] };
                    }
                    obstaclePoints = o;
                }
                return obstaclePoints;
            }
        }

        Point4D[] worldPoints;
        public Point4D[] WorldPoints
        {
            get
            {
                if (worldPoints == null)
                {
                    var ci = ComputeInfo;
                    float[] f = new float[ci.WorldPointsCount * 4];
                    Marshal.Copy(ci.WorldPointsPtr, f, 0, ci.WorldPointsCount * 4);
                    Point4D[] o = new Point4D[ci.WorldPointsCount];
                    for (int i = 0; i < ci.WorldPointsCount; i++)
                    {
                        o[i] = new Point4D() { X = f[i * 4], Y = f[i * 4 + 1], Z = f[i * 4 + 2], A = f[i * 4 + 3] };
                    }
                    worldPoints = o;
                }
                return worldPoints;
            }
        }

        Point4D[] worldObstaclePoints;
        public Point4D[] WorldObstaclePoints
        {
            get
            {
                if (worldObstaclePoints == null)
                {
                    var ci = ComputeInfo;
                    if (ci.WorldObstaclePointsCount == 0)
                        return ObstaclePoints;
                    float[] f = new float[ci.WorldObstaclePointsCount * 4];
                    Marshal.Copy(ci.WorldObstaclePointsPtr, f, 0, ci.WorldObstaclePointsCount * 4);
                    Point4D[] o = new Point4D[ci.WorldObstaclePointsCount];
                    for (int i = 0; i < ci.WorldObstaclePointsCount; i++)
                    {
                        o[i] = new Point4D() { X = f[i * 4], Y = f[i * 4 + 1], Z = f[i * 4 + 2], A = f[i * 4 + 3] };
                    }
                    worldObstaclePoints = o;
                }
                return worldObstaclePoints;
            }
        }

        Point4D[] cameraPoints;
        public Point4D[] CameraPoints
        {
            get
            {
                if (cameraPoints == null)
                {
                    var ci = ComputeInfo;
                    float[] f = new float[ci.CameraPointsCount * 4];
                    Marshal.Copy(ci.CameraPointsPtr, f, 0, ci.CameraPointsCount * 4);
                    Point4D[] o = new Point4D[ci.CameraPointsCount];
                    for (int i = 0; i < ci.CameraPointsCount; i++)
                    {
                        o[i] = new Point4D() { X = f[i * 4], Y = f[i * 4 + 1], Z = f[i * 4 + 2], A = f[i * 4 + 3] };
                    }
                    cameraPoints = o;
                }
                return cameraPoints;
            }
        }

        public AggregateItem? GetAggregateItem(int x, int y)
        {
            var ci = ComputeInfo;
            x += ci.xOff;
            y += ci.yOff;
            if (x < 0 || y < 0 || x >= ci.Width || y >= ci.Height)
                return null;
            int idx = x + y * ci.Width;
            return (AggregateItem)Marshal.PtrToStructure(IntPtr.Add(ci.AggregatesPtr, idx* Marshal.SizeOf(typeof(AggregateItem))), typeof(AggregateItem));
        }

        public Akcelerator(int maxPoints, int width, int height, int xOff, int yOff, float resolution)
        {
            computeInfoPtr = ComputeAlloc(maxPoints, width, height, xOff, yOff, resolution);
        }

        private float[] Transformation(Matrix3D m)
        {
            float[] l = new float[16];

            l[0] = (float)m.M11;
            l[1] = (float)m.M12;
            l[2] = (float)m.M13;
            l[3] = (float)m.M14;

            l[4] = (float)m.M21;
            l[5] = (float)m.M22;
            l[6] = (float)m.M23;
            l[7] = (float)m.M24;

            l[8] = (float)m.M31;
            l[9] = (float)m.M32;
            l[10] = (float)m.M33;
            l[11] = (float)m.M34;

            l[12] = (float)m.OffsetX;
            l[13] = (float)m.OffsetY;
            l[14] = (float)m.OffsetZ;
            l[15] = (float)m.M44;

            return l;
        }

        public void Segment(Image<Gray16> leftImage, IDepthCameraProjection leftProjection, Image<Gray16> rightImage, IDepthCameraProjection rightProjection, Matrix3D globalTransform)
        {
            float[] lt = Transformation(leftProjection.Transformation);
            Point2D[,] lct = leftProjection.Camera2DToCamera3D;

            float[] rt = Transformation(rightProjection.Transformation);
            Point2D[,] rct = rightProjection.Camera2DToCamera3D;

            float[] gt = Transformation(globalTransform);

            Segment2(computeInfoPtr, leftImage?.Data, lt, lct, rightImage?.Data, rt, rct, gt, leftImage.Width * leftImage.Height, 0.1f);
            computeInfo = null;
            obstaclePoints = null;
            worldPoints = null;
            worldObstaclePoints = null;
            cameraPoints = null;
        }

        ~Akcelerator()
        {
            ComputeFree(computeInfoPtr);
        }
    }
}

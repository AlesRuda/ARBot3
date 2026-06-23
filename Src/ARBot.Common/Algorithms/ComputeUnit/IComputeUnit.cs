using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.ComputeUnit
{
    /// <summary>
    /// Rozhrani pro vypocetni jednotku. Umoznuje prenest vypocty na ruzny HW.
    /// </summary>
    public interface IComputeUnit : IBackProject
    {
        // rozliseni agregacniho pole
        float AggregateResolution { get; }
        PlaneParams LeftCameraParams { get; }
        PlaneParams RightCameraParams { get; }
        // pocet bodu v poli WordPoints, muze byt rychlejsi nez WordPoints.Count
        int WordPointsCount {get; }
        /// <summary>
        /// Body prekazek  - xyz body v orientaci kamery tj. podle left/right TransformMatrix - x roste na vychod, y roste na sever a z smerem nahoru
        /// </summary>
        Point4D[] ObstaclePoints { get; }
        /// <summary>
        /// xyz body ve svetove orientaci - x roste na vychod, y roste na sever a z smerem nahoru
        /// </summary>
        Point4D[] WordPoints { get; }
        /// <summary>
        /// Body prekazek  - xyz body ve svetove orientaci - x roste na vychod, y roste na sever a z smerem nahoru
        /// </summary>
        Point4D[] WordObstaclePoints { get; }
        Point4D[] CameraPoints { get; }
        AggregateItem? GetAggregateItem(int x, int y);
        /// <summary>
        /// Hleda prekazky v hloubkovem obraze.
        /// </summary>
        /// <param name="leftImage">Levy hloubkovy obraz</param>
        /// <param name="leftProjection">Leva hloubkova projekce. Soucasti je transformace hloubkoveho obrazu na 3D body a rotace kamery vuci horizontale.</param>
        /// <param name="rightImage">Pravy hloubkovy obraz</param>
        /// <param name="rightProjection">Prava hloubkova projekce. Soucasti je transformace hloubkoveho obrazu na 3D body a rotace kamery vuci horizontale.</param>
        /// <param name="globalTransform">Finalni pootoceni do svetovych souradnic.</param>
        void Segment(Image<Gray16> leftImage, IDepthCameraProjection leftProjection, Image<Gray16> rightImage, IDepthCameraProjection rightProjection, System.Numerics.Matrix4x4 globalTransform);
        /// <summary>
        /// Hleda prekazky v hloubkovem obraze.
        /// </summary>
        /// <param name="image">Hloubkovy obraz</param>
        /// <param name="projection">Hloubkova projekce. Soucasti je transformace hloubkoveho obrazu na 3D body a rotace kamery vuci horizontale.
        /// Rotace kolem svisle osy je oddelena do globalTransform.
        /// </param>
        /// <param name="globalTransform">Finalni pootoceni do svetovych souradnic.</param>
        void Segment(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform);
        /// <summary>
        /// Hleda prekazky v hloubkovem obraze.
        /// </summary>
        /// <param name="image">Hloubkovy obraz</param>
        /// <param name="projection">Hloubkova projekce. Soucasti je transformace hloubkoveho obrazu na 3D body a rotace kamery vuci horizontale.
        /// Rotace kolem svisle osy je oddelena do globalTransform.
        /// </param>
        /// <param name="globalTransform">Finalni pootoceni do svetovych souradnic.</param>
        void SegmentNew(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform);
        void SegmentNew1(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform);
        void SegmentNew2(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform);
        void SegmentNew3(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform);
        void SegmentNew4(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform);
        void SegmentNew5(Image<Gray16> image, IDepthCameraProjection projection, System.Numerics.Matrix4x4 globalTransform, float zLimit, float r2);
        /// <summary>
        /// Hleda hranice cesty v pravdepodobnostnim obrazku
        /// </summary>
        /// <param name="image"></param>
        /// <param name="scaleX"></param>
        /// <param name="scaleY"></param>
        /// <returns></returns>
        List<PathEdge> PathEdges(Image<Gray> image, double scaleX, double scaleY);
    }
};

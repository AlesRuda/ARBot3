using ARBot.Common.Common;
using ARBot.Common.LocalMaps;
using System.Collections.Generic;
using System.Numerics;

namespace ARBot.Common.Coordinates
{
    /// <summary>
    /// Projekce kamery
    /// </summary>
    public interface ICameraProjection
    {
/*        /// <summary>
        /// Spocte transformaci 
        /// </summary>
        /// <param name="yaw">Orientace oproti vychodu v radianech a matematickem smyslu.</param>
        /// <param name="pitch">Predozadni naklon oproti vodorovne rovine v radianech. 0 vodorovne. Roste smerem nahoru.</param>
        /// <param name="roll">Pravoleve otoceni oproti vodorovne rovine v radianech </param>
        /// <param name="offset">Posunuti kamery v metrech vzhledem k rovine po ktere jede robot.</param>
        void SetOrientation(double yaw, double pitch, double roll, Vector3D offset);
        */
        /// <summary>
        /// Nastavi orientaci kamery a pozici kamery
        /// </summary>
        /// <param name="transform">Natoceni a pozice kamery</param>
        void SetOrientation(Matrix4x4 transform);
        /// <summary>
        /// Transformuje souradnice v rovine po niz jede robot (pocatek v miste robotu) do roviny kamery (pocatek vlevo nahore).
        /// </summary>
        /// <param name="x">Roste smerem na vychod v metrech.</param>
        /// <param name="y">Roste smerem na sever v metrech.</param>
        /// <param name="xc">X v rovine kamery. Roste smerem doprava v pixlech.</param>
        /// <param name="yc">Y v rovine kamery. Roste smerem dolu v pixlech.</param>
        bool Transform(float x, float y, ref float xc, ref float yc);
        /// <summary>
        /// Transformuje souradnice v rovine kamery (pocatek vlevo nahore) do roviny po niz jede robot (pocatek v miste robotu).
        /// Roli hraje nastavena orientace kamery pomoci SetOrientation.
        /// </summary>
        /// <param name="xc">X v rovine kamery. Roste smerem doprva v pixlech.</param>
        /// <param name="yc">Y v rovine kamery. Roste smerem dolu v pixlech.</param>
        /// <param name="x">Roste smerem na vychod v metrech.</param>
        /// <param name="y">Roste smerem na sever v metrech.</param>
        bool TransformBack(float xc, float yc, ref float x, ref float y);
        /// <summary>
        /// Polygon oznacujici kam se na vozovce promitne obraz kamery
        /// </summary>
        List<Point2D> TargetPoly { get; }
    }
    /// <summary>
    /// Projekce kamery
    /// </summary>
    public interface ICameraProjection<TImage>
    {
        /*        /// <summary>
                /// Prepocte bod v realnem svete (pocatek souradnic je robot) do souradnice camery v pixelech.
                /// </summary>
                /// <param name="world"></param>
                /// <param name="xc"></param>
                /// <param name="yc"></param>
                /// <returns></returns>
                bool Transform(Point3D world, out double xc, out double yc);
                */
        /*        /// <summary>
                /// Spocte transformaci 
                /// </summary>
                /// <param name="yaw">Orientace oproti vychodu v radianech a matematickem smyslu.</param>
                /// <param name="pitch">Predozadni naklon oproti vodorovne rovine v radianech. 0 vodorovne. Roste smerem nahoru.</param>
                /// <param name="roll">Pravoleve otoceni oproti vodorovne rovine v radianech </param>
                /// <param name="offset">Posunuti kamery v metrech vzhledem k rovine po ktere jede robot.</param>
                void SetOrientation(double yaw, double pitch, double roll, Vector3D offset);*/
        /// <summary>
        /// Nastavi orientaci kamery a pozici kamery
        /// </summary>
        /// <param name="transform">Natoceni a pozice kamery</param>
        void SetOrientation(Matrix4x4 transform);

        /// <summary>
        /// Promitne image. Kam? to zalezi na implementaci.
        /// </summary>
        /// <param name="image">Obrazek</param>
        void Transform(TImage image);
    }
    /// <summary>
    /// Projekce 3d kamery
    /// </summary>
    public interface IDepthCameraProjection
    {
        /*        /// <summary>
                /// Prepocte bod v realnem svete (pocatek souradnic je robot) do souradnice camery v pixelech.
                /// </summary>
                /// <param name="world"></param>
                /// <param name="xc"></param>
                /// <param name="yc"></param>
                /// <returns></returns>
                bool Transform(Point3D world, out double xc, out double yc);
                */
        /*        /// <summary>
                /// Spocte transformaci 
                /// </summary>
                /// <param name="yaw">Orientace oproti vychodu v radianech a matematickem smyslu.</param>
                /// <param name="pitch">Predozadni naklon oproti vodorovne rovine v radianech. 0 vodorovne. Roste smerem nahoru.</param>
                /// <param name="roll">Pravoleve otoceni oproti vodorovne rovine v radianech </param>
                /// <param name="offset">Posunuti kamery v metrech vzhledem k rovine po ktere jede robot.</param>
                void SetOrientation(double yaw, double pitch, double roll, Vector3D offset);*/
        /// <summary>
        /// Nastavi orientaci kamery a pozici kamery
        /// </summary>
        /// <param name="transform">Natoceni a pozice kamery</param>
        void SetOrientation(Matrix4x4 transform);
        /// <summary>
        /// Vrati seznam 3d bodu na zaklade hloubkoveho obrazu.
        /// Body jsou tranformovany do svetovych souradnic.
        /// </summary>
        /// <param name="image">Obrazek</param>
        List<Common.Point4D> GetPointCloud(Image<Gray16> depth);
        /// <summary>
        /// Transformace spoctena metodou SetOrientation
        /// </summary>
        Matrix4x4 Transformation { get; }
        /// <summary>
        /// Serializovatelny popis projekce (intrinsics + transformace), ze ktereho ji lze postavit
        /// znovu - uklada se do <c>CameraFrame.Projection</c> kvuli offline prepoctu ze zaznamu.
        /// Vychozi implementace vraci <c>null</c> ("nemam"), aby jednoduche/testovaci projekce
        /// nemusely nic doplnovat. Viz doc/occupancy-and-local-planning.md.
        /// </summary>
        CameraProjectionInfo Info => null;
        /// <summary>
        /// Transformace bodu z plochy kamery do xyz prosotru kamery xyz=(Camera2DToCamera3D[x, y].xy*Dist[x, y], Dist[x, y])
        /// </summary>
        Point2D[,] Camera2DToCamera3D { get; }
        /// <summary>
        /// Polygon oznacujici kam se na vozovce promitne obraz kamery
        /// </summary>
        List<Point2D> TargetPoly { get; }
        /// <summary>
        /// Transformuje souradnice v rovine color kamery (pocatek vlevo nahore) do svetovych souradnic robotu (pocatek v miste robotu).
        /// Roli hraje nastavena orientace kamery pomoci SetOrientation.
        /// </summary>
        /// <param name="points">Body v rovine kamery. Roste smerem doprava a dolu v pixlech.</param>
        /// <param name="depth">Hloubkova mapa korespondujici k bodum points</param>
        /// <returns>Pole tranformovanych bodu do svetovych souradnic. Pokud je A slozka bodu rovna 0 (vlastne cely bod bude identicky 0) je tento bod nevalidni.</returns>
        List<Common.Point4D> TransformBack(List<Point> points, Image<Gray16> depth);
    }
}
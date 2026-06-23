using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;
using ARBot.Common.Coordinates;
using ARBot.Common.Algorithms.Statistic;

namespace ARBot.Common.Maps
{
    public class MapPoint
    {
        public MapPoint()
        {
            Ways = new MapWayCollection();
            MinDistance = 2;
            WidthPropability = new MovingStat();
        }

        /// <summary>
        /// Timto bodem neni mozno projet.
        /// </summary>
        public bool NonDrivable;
        /// <summary>
        /// Vypocet vzdalenosti je dokoncen tj. neexistuje kratsi cesta do tohoto bodu
        /// </summary>
        public bool Final;
        /// <summary>
        /// Vzdalenost do bodu je spocitana, ale jeste nemusi byt finalne urcena, tj. mozna existuje kratsi
        /// </summary>
        public bool DistanceCalculated;
        /// <summary>
        /// Indikuje cilovy bod, nema vliv na vypocty v mape
        /// </summary>
        public bool Target;
        public bool Temporary;

        /// <summary>
        /// Vzdalenost k cili, je nastaven behem vypoctu nejkratsi cesty CalculateDistances.
        /// </summary>
        public double WeigthDistance { get; set; }

        /// <summary>
        /// Vzdalenost k cili, je nastaven behem vypoctu nejkratsi cesty CalculateDistances
        /// </summary>
        public double Distance { get; set; }

        /// <summary>
        /// Vzdalenost od bodu ve ktere se povazuje bod za dosazeny, vyhleda se nejblizsi point k cili do ktereho vede z tohoto cesta 
        /// </summary>
        public double MinDistance { get; set; }

        public LLA LLA { get; set; }
        public ECEF Position { get; set; }
        public long ID { get; set; }
        public double Width { get; set; }

        public MovingStat WidthPropability { get; set; }

        public MapWayCollection Ways { get; set; }
        /// <summary>
        /// Predpocitana X souradnice pro kresleni mapy
        /// </summary>
        public double X;
        /// <summary>
        /// Predpocitana Y souradnice pro kresleni mapy
        /// </summary>
        public double Y;

        public void UpdatePosition(Transformation t)
        {
            Position = t.Transform(new ECEF(Ellipsoid.Sphere, LLA));
//            X=a.Y;
  //          Y = a.Z;
        }

        public override string ToString()
        {
            return string.Format("ID={0}, Distance={1}, Final={2}, DistanceCalculated={3}, Target={4}, Temporary={5}", ID, Distance, Final, DistanceCalculated, Target, Temporary);
        }

        public void GetWays(Dictionary<MapWay, bool> ways, double radius)
        {
            foreach(MapWay way in Ways)
            {
                if(way.Distance<radius && !ways.ContainsKey(way))
                {
                    ways.Add(way, true);
                    if (way.Start.ID == ID)
                        way.End.GetWays(ways, radius - way.Distance);
                    else
                        way.Start.GetWays(ways, radius - way.Distance);
                }
            }
        }

        public void UpdateWidth(double w)
        {
            WidthPropability.Add(w);
            if (WidthPropability.Count > 4)
                WidthPropability.RemoveFirst();
            Width = w;
        }
    }
}

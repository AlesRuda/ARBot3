using ARBot.Common.SLAM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.LocalMaps
{
    public class GraphMap: GraphMapBase
    {
        public Dictionary<int, GraphMapType> Parameters { get; set; } = new Dictionary<int, GraphMapType>();

        public override void Solve(List<ICPObservationPoint> data)
        {
            using (new PerformanceToken("Graph.Solve"))
            {
                foreach (var g in data.GroupBy(i => i.Type))
                {
                    using (new PerformanceToken(g.Key.ToString()))
                    {
                        var par = Parameters[g.Key];
                        par.Update(this, g.Key, g);
                    }
                }
            }
        }
    }
}

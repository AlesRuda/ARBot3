using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public class Histogram<TKey, TValue> : IEnumerable<KeyValuePair<TKey, int>>
    {
        Dictionary<TKey, int> dic = new Dictionary<TKey, int>();
        Func<TValue, TKey> getter;

        public Histogram(Func<TValue, TKey> getter)
        {
            this.getter = getter;
        }

        public void Add(TValue v)
        {
            var k = getter(v);
            if (!dic.ContainsKey(k))
                dic.Add(k, 0);
            dic[k]++;
        }

        public IEnumerator<KeyValuePair<TKey, int>> GetEnumerator()
        {
            return dic.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return dic.GetEnumerator();
        }

    }
    public class Histogram<TValue> : Histogram<double, TValue>
    {
        public Histogram(Func<TValue, double> getter):base(getter)
        {
        }

        public Image<BGR> GetImage(int width, int height)
        {
            var min = this.Min(v => v.Key);
            var max = this.Max(v => v.Key);
            return GetImage(width, height, min, max, 0, this.Max(v => v.Value));
        }
        public Image<BGR> GetImage(int width, int height, double minx, double maxx)
        {
            return GetImage(width, height, minx, maxx, 0, this.Max(v => v.Value));
        }

        public Image<BGR> GetImage(int width, int height, double minx, double maxx, int miny, int maxy)
        {
            var i = new Image<BGR>(width, height);
            DrawEngine de = new DrawEngine() { XMin = 0, XMax = width - 1, YMin = 0, YMax = height, Clipping=true };
            de.PixelSetter = (x, y) => i[x, height-y].R = 255;

            foreach(var v in this.GroupBy(kv=>(int)(width*(kv.Key-minx)/(maxx-minx))).Select(g=>new { X=g.Key, Min=g.Min(v=>v.Value), Max=g.Max(v => v.Value) }))
            {
                de.Line(new Point(v.X, 0), new Point(v.X, height*(v.Max-miny)/(maxy-miny)));
            }

            return i;
        }
    }
}

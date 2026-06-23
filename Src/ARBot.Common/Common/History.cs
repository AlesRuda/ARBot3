using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Historie stavu a jejich interpolace
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    public class History<TItem> where TItem: IHistoryItem<TItem>
    {
        List<TItem> list = new List<TItem>();
        private int maxCount;

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="maxCount"></param>
        public History(int maxCount)
        {
            this.maxCount = maxCount;
        }

        /// <summary>
        /// Maximalni pocet prvku v historii
        /// </summary>
        public int MaxCount
        {
            get
            {
                return maxCount;
            }
        }
        /// <summary>
        /// Pridava novy vzorek
        /// </summary>
        /// <param name="state"></param>
        public void Add(TItem state)
        {
            if (list.Count > 0)
            {
                DateTime ts = list[list.Count - 1].TimeStamp;
                if (state.TimeStamp < ts)
                    throw new ArgumentException(string.Format("State must be newest. Curret TimeStamp {0:HH:mm:ss.fff}, last TimeStamp {1:HH:mm:ss.fff}.", state.TimeStamp, ts), "state");
                if (state.TimeStamp == ts)
                    return;
            }
            if (maxCount == list.Count)
                list.RemoveAt(0);
            list.Add(state);
        }
        /// <summary>
        /// Smaze celou historii
        /// </summary>
        public void Clear()
        {
            list.Clear();
        }


        private int BinaryIndex(DateTime ts)
        {
            int l = 0;
            int h = list.Count-1;
            int idx = h;
            while (l<=h)
            {
                idx = (l + h) / 2;
                if (list[idx].TimeStamp == ts)
                    return idx;
                else
                {
                    if (list[idx].TimeStamp > ts)
                        h = idx - 1;
                    else
                        l = idx + 1;
                }
            }
            return idx;
        }
        /// <summary>
        /// Vraci odhad stavu pro zadany cas.
        /// </summary>
        /// <param name="now"></param>
        /// <returns></returns>
        public TItem this[DateTime now]
        {
            get
            {
                TItem next = default(TItem);
                TItem prev = default(TItem);
                /*
                next = list.FirstOrDefault((i) => i.TimeStamp >= now);
                prev = list.LastOrDefault((i) => i.TimeStamp <= now);
                */
                int idx = BinaryIndex(now);
                if (idx >= 0 && idx < list.Count)
                {
                    prev = list[idx];
                    if (prev.TimeStamp <= now)
                    {
                        if (idx + 1 < list.Count)
                            next = list[idx + 1];
                    }
                    else if (idx > 0)
                    {
                        next = prev;
                        prev = list[idx - 1];
                    }
                }

                if (next == null)
                {
                    Debug.WriteLine(string.Format("History {0} - next not found.", typeof(TItem).Name));
                    return prev;
                }
                if (prev == null)
                {
                    Debug.WriteLine(string.Format("History {0} - prev not found.", typeof(TItem).Name));
                    return next;
                }

                if (next == null && prev == null)
                    return default(TItem);

                if (next.TimeStamp == prev.TimeStamp)
                    return next;
                float d = (float)((now - prev.TimeStamp).TotalSeconds / (next.TimeStamp - prev.TimeStamp).TotalSeconds);

                TItem s = prev.Interpolate(prev, next, d);

                s.TimeStamp = now;

                return s;
            }
        }
    }
}

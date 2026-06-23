using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Prioritni fronta.
    /// </summary>
    /// <typeparam name="TPriority"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class PriorityQueue<TPriority, TValue>
    {
        private SortedDictionary<TPriority, List<TValue>> dic = new SortedDictionary<TPriority, List<TValue>>();
        /// <summary>
        /// Odstranuje specificky zaznam 
        /// </summary>
        /// <param name="priority"></param>
        /// <param name="value"></param>
        public void Remove(TPriority priority, TValue value)
        {
            List<TValue> list;
            if (dic.TryGetValue(priority, out list))
            {
                list.Remove(value);
                if (list.Count == 0)
                    dic.Remove(priority);
            }
        }
        /// <summary>
        /// Vklada zaznam do fronty
        /// </summary>
        /// <param name="priority"></param>
        /// <param name="value"></param>
        public void Enqueue(TPriority priority, TValue value)
        {
            List<TValue> list;
            if (!dic.TryGetValue(priority, out list))
                dic.Add(priority, list = new List<TValue>());
            list.Add(value);
        }
        /// <summary>
        /// Vybira prvni zaznam s nejnizsi prioritou
        /// </summary>
        /// <returns></returns>
        public TValue Dequeue()
        {
            var kv = dic.First();
            var ret = kv.Value[kv.Value.Count - 1];
            kv.Value.RemoveAt(kv.Value.Count - 1);
            if (kv.Value.Count == 0)
                dic.Remove(kv.Key);
            return ret;
        }

        /// <summary>
        /// Indikuje prazdnou frontu.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                return dic.Count == 0;
            }
        }
    }
}

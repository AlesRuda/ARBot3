using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.Servos
{
    public abstract class ServoDriverBase<T>:IEnumerable<T> where T: ServoBase, new()
    {
        public int Count { get { return serva.Count; } }
        List<T> serva;

        public ServoDriverBase()
        {
        }

        protected void Init(int cnt)
        {
            serva = new List<T>();
            for (int i = 0; i < cnt; i++)
                serva.Add(new T() { Channel = i });
        }

        public T this[int index]
        {
            get
            {
                return serva[index];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return serva.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return serva.GetEnumerator();
        }

        public abstract void Move(double? time);
        public abstract void QueryPositions();

    }
}

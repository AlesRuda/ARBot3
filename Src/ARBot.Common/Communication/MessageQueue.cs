using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    public class MessageQueue
    {
        public class ItemInfo
        {
            public Message Msg;
            public int Count;
            public int CountLimit;
        }

        private Dictionary<string, ItemInfo> cfg = new Dictionary<string, ItemInfo>();
        private AutoResetEvent autoEvent = new AutoResetEvent(false);
        private Queue<Message> queue = new Queue<Message>();

        public Dictionary<string, ItemInfo> Cfg { get { return cfg; } }
        public AutoResetEvent AutoEvent { get { return autoEvent; } }

        public int Count { get { return queue.Count; } }

        public MessageQueue()
        {
            Message m=new State();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
            m = new EKFStepMsg();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
            m = new Info();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit=100 });
            m = new Blob();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
            m = new Marker();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 1000 });
            m = new Module();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 1000 });
            m = new VFH();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
            m = new Lidar();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
            m = new ICPMsg();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
            m = new ColliderMsg();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
            m = new PathEdgeMsg();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
            m = new GraphNavigationMsg();
            Cfg.Add(m.MsgName, new ItemInfo() { Msg = m, CountLimit = 10 });
        }

        public Message Dequeue()
        {
            Message m=null;
            lock(queue)
                m= queue.Dequeue();
            if (m != null)
            {
                ItemInfo info;
                if (cfg.TryGetValue(m.MsgName, out info))
                {
                    if (info != null)
                    {
                        lock (info)
                        {
                            info.Count--;
                        }
                    }
                }
            }
            return m;
        }
        public bool Enqueue(Message msg)
        {
            if (msg != null)
            {
                ItemInfo info;
                if (cfg.TryGetValue(msg.MsgName, out info))
                {
                    int cnt=0;
                    if (info != null)
                    {
                        lock (info)
                        {
                            cnt = info.CountLimit-info.Count;
                            if (cnt >= 0)
                                info.Count++;
                        }
                    }
                    if (cnt >= 0)
                    {
                        autoEvent.Set();

                        lock (queue)
                            queue.Enqueue(msg);
                        return true;
                    }
                }
            }
            return false;
        }
        public void Clear()
        {
            lock(queue)
                queue.Clear();
        }
    }
}

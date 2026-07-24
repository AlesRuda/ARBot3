using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Logs
{
    public class MessagesFilter
    {
        public MessagesFilter()
        {
            messages = new Dictionary<string, MessageFilter>();
            MaxIndex = 0;
        }
        public int CurrentIndex;
        public int? IndexFrom { get; set; }
        public int? IndexTo { get; set; }
        public int MaxIndex { get; set; }

        public int CurrentTime;
        public int? TimeFrom { get; set; }
        public int? TimeTo { get; set; }
        public int MaxTime { get; set; }

        private Dictionary<string, MessageFilter> messages;
        public ObservableCollection<MessageFilter> Messages => new ObservableCollection<MessageFilter>(messages.Values);

        public void Add(Message m)
        {
            MaxIndex++;
            if (m is State s)
                MaxTime = Math.Max(MaxTime, s.Time);

            if (messages.ContainsKey(m.MsgName))
            {
                var mf = messages[m.MsgName];
                mf.Add(m);
            }
            else
                messages.Add(m.MsgName, new MessageFilter(m));
        }

        public void Reset()
        {
            CurrentIndex = 0;
            CurrentTime = 0;
        }
        public bool Filter(Message m)
        {
            if (IndexFrom != null && IndexFrom.Value > CurrentIndex)
                return false;
            if (IndexTo != null && IndexTo.Value < CurrentIndex)
                return false;

            CurrentIndex++;

            if (m is State s)
                CurrentTime = Math.Max(CurrentTime, s.Time);

            if (TimeFrom != null && TimeFrom.Value > CurrentTime)
                return false;
            if (TimeTo != null && TimeTo.Value < CurrentTime)
                return false;

            MessageFilter mf;
            if(messages.TryGetValue(m.MsgName, out mf))
            {
                if (!mf.Read)
                    return false;
                else
                    return mf.Filter(m);
            }

            return true;
        }
    }
}

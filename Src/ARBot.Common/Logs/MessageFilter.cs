using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Logs
{
    public class MessageFilter
    {
        public MessageFilter(Message m)
        {
            MsgName = m.MsgName;
            Read = true;
            names = new Dictionary<string, NameFilter>();
            Add(m);
        }
        public string MsgName { get; private set; }
        public bool Read { get; set; }
        private Dictionary<string, NameFilter> names;
        public ObservableCollection<NameFilter> Names => new ObservableCollection<NameFilter>(names.Values);
        public void Add(Message m)
        {
            if(m is INamedMessage nm)
            {
                if (!names.ContainsKey(nm.Name))
                    names.Add(nm.Name, new NameFilter(nm.Name));
            }
        }

        public bool Filter(Message m)
        {
            NameFilter nf;
            if (m is INamedMessage nm)
            {
                if (names.TryGetValue(nm.Name, out nf))
                {
                    return nf.Read;
                }
            }

            return true;
        }

    }
}

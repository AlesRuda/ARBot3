using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;
using ARBot.Common.Communication;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class MessageCollection : ObservableCollection<Message> //ThreadSafeObservableCollection<ARBotState>
    {
        public MessageCollection()
        {
        }


        public Dictionary<string, Message> Msgs
        {
            get
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(s => s.GetTypes())
                    .Where(p => typeof(Message).IsAssignableFrom(p) && !p.IsAbstract)
                    .Select(t=>(Message)Activator.CreateInstance(t))
                    .ToDictionary(m=>m.MsgName);
            }
        }

        public void Load(Stream s, Encoding encoding, Dictionary<string, Message> msgs, Func<Message, bool> filter = null)
        {
            using (MessageReader mr = new MessageReader(s, encoding, msgs))
            {
                while (s.Length > s.Position)
                {
                    Message msg = mr.Read();
                    if (msg == null)
                        break;
                    if(filter==null || filter(msg))
                        Add(msg);
                }
            }
        }

        public MessagesFilter Analyze(Stream s, Encoding encoding, Dictionary<string, Message> msgs, int cnt)
        {
            var mf = new MessagesFilter();
            using (MessageReader mr = new MessageReader(s, encoding, msgs))
            {
                while (s.Length > s.Position && cnt>0)
                {
                    cnt--;
                    Message msg = mr.Read();
                    if (msg == null)
                        break;
                    mf.Add(msg);
                }
            }
            return mf;
        }

        public MessagesFilter Analyze(Stream s, int cnt)
        {
            return Analyze(s, Encoding.UTF8, Msgs, cnt);
        }

        public void Load(Stream s, Func<Message, bool> filter = null)
        {
            Load(s, Encoding.UTF8, Msgs, filter);
        }

        public void Save(Stream s, Encoding encoding)
        {
            using (MessageWriter mw = new MessageWriter(s, encoding))
            {
                foreach (Message msg in this)
                    mw.Write(msg);
            }
        }

        public void Save(Stream s)
        {
            Save(s, Encoding.UTF8);
        }

        public static MessageCollection Deserialize(string fn)
        {
            MessageCollection c = null;
            using (Stream stream = new FileStream(fn, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read))
            {
                XmlSerializer xmlserializer = new XmlSerializer(typeof(MessageCollection), new Type[] { typeof(Message) });
                c = xmlserializer.Deserialize(stream) as MessageCollection;
/*
                foreach (State s in c)
                    s.Owner = c;
                */
            }
            return c;
        }
    }
}

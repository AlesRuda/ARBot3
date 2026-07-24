using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Logs
{
    public class NameFilter
    {
        public NameFilter(string name)
        {
            Name = name;
            Read = true;
        }
        public string Name { get; private set; }
        public bool Read { get; set; }
    }
}

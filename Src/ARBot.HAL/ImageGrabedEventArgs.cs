using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    public class ImageGrabedEventArgs: EventArgs
    {
        public List<CameraFrame> Frames { get; set; }
    }
}

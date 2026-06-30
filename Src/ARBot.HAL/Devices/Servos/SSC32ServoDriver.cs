using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.Servos
{
    public class SSC32ServoDriver:ServoDriverBase<ServoBase>
    {
        IUart uart;

        public SSC32ServoDriver(IUart uart, int cnt)
        {
            this.uart = uart;
            Init(cnt);
        }

        public int QueryPosition(int channel)
        {
            uart.WriteLine(string.Format("QP {0}", channel));
/*            Task<Byte[]> t = uart.ReadAsync(1);
            t.Wait();
            return ((int)t.Result[0])*10;
 */
            byte[] buf = uart.Read(1);
            return ((int)buf[0]) * 10;

        }

        public void QueryPosition(IEnumerable<ServoBase> c)
        {
            DateTime dt = TimeBase.Now;
            List<ServoBase> l=new List<ServoBase>(c);
            int cnt = l.Count;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < cnt; i++)
                sb.AppendLine(string.Format("QP {0}", l[i].Channel));
            uart.WriteLine(sb.ToString());
//            Debug.WriteLine(sb.ToString());
/*            Task<byte[]> t = uart.ReadAsync(Count);
            t.Wait();
            byte[] buf = t.Result;*/

            byte[] buf = uart.Read(Count);
//            Debug.WriteLine(buf.Length.ToString());

            for (int i = 0; i < Count; i++)
            {
                this[l[i].Channel].CurrentPulseLen = ((int)buf[i]) * 10;
                this[l[i].Channel].TimeStamp = dt;

                //              Debug.WriteLine(string.Format("{0}:{1}", i, ((int)buf[i]) * 10));
            }
        }

        public override void QueryPositions()
        {
            QueryPosition(this);
        }

        private string GetCommand(ServoBase servo)
        {
            if (servo.MaxPulseSpeed.HasValue)
                return string.Format("#{0} P{1} S{2}", servo.Channel, servo.PulseLen, servo.MaxPulseSpeed);
            else
                return string.Format("#{0} P{1}", servo.Channel, servo.PulseLen);
        }

        public void Move(double? time, params ServoBase[] c )
        {
            StringBuilder sb = new StringBuilder();
            foreach (ServoBase s in c)
            {
                sb.Append(GetCommand(s));
                sb.Append(" ");
            }
            if (time.HasValue)
                sb.Append(string.Format("T {0}", ((int)time * 1000)));
  //          Debug.WriteLine(sb.ToString());
            uart.WriteLine(sb.ToString());
        }

        public override void Move(double? time)
        {
            Move(time, this.ToArray());
        }

        public bool IsMoving
        {
            get
            {
                uart.WriteLine("Q");
                Task<Byte[]> t = uart.ReadAsync(1);
                t.Wait();
                return t.Result[0]=='+';
            }
        }
    }
}

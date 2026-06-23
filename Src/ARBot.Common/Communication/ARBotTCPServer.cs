using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    public class ARBotTCPServer:ARBotTCPClient
    {
        private TcpListener tcpListener;

        public ARBotTCPServer(int port):base()
        {
            this.tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            Task.Run(()=>Listen());
            Task.Run(() => SendLoop());
        }
        protected void Listen()
        {
            while (true)
            {
                try
                {
                    SetTCPClient(tcpListener.AcceptTcpClient());
                    ReceiveLoop();
                }
                catch(Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }
                finally
                {
                    SetTCPClient(null);
                }
            }
        }
    }
}

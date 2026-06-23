using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    public class ARBotTCPClient
    {
        protected Dictionary<string, Message> msgs = new Dictionary<string, Message>();

        protected TcpClient tcpClient;
        protected MessageReader mr;
        protected MessageWriter mw;
        protected Encoding encoding;

        protected Queue<Message> inQueue;
        protected MessageQueue outQueue;
        Task st;
        Task rt;
        bool kill = false;

        public int InCount
        {
            get
            {
                return inQueue.Count;
            }
        }

        protected ARBotTCPClient()
        {
            encoding = Encoding.UTF8;
            inQueue = new Queue<Message>();
            outQueue = new MessageQueue();
            msgs=outQueue.Cfg.ToDictionary((i)=>i.Key, (i)=>i.Value.Msg);
        }

        public ARBotTCPClient(TcpClient c, Action finish)
            : this()
        {
            SetTCPClient(c);
            st = Task.Run(() => SendLoop());
            rt = Task.Run(() =>
                {
                    try
                    {
                        ReceiveLoop();
                    }
                    catch(Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                    }
                });
            Wait(finish, rt);
        }

        [DebuggerStepThrough]
        protected async void Wait(Action finish, params Task[] tasks)
        {
            await Task.Run(() =>
            {
                try
                {
                    Task.WaitAll(tasks);
                }
                catch
                {
                }
            });
            if (finish != null)
                finish();
        }

        protected void SetTCPClient(TcpClient c)
        {
            lock (outQueue)
            {
                TcpClient old = tcpClient;
                tcpClient = c;
                if (c != null)
                {
                    NetworkStream ns = tcpClient.GetStream();
                    mr = new MessageReader(ns, encoding, msgs);
                    mw = new MessageWriter(ns, encoding);
                }
                else
                {
                    mr = null;
                    mw = null;
                }
                if (old != null)
                    old.Close();
                if (outQueue != null)
                    outQueue.AutoEvent.Set();
            }
        }

        public void Close()
        {
            SetTCPClient(null);
        }

//        [DebuggerStepThrough]
        protected void SendLoop()
        {
            while (!kill)
            {
                try
                {
                    outQueue.AutoEvent.WaitOne();
                    if (tcpClient != null)
                    {
                        if (outQueue.Count > 0)
                        {
                            lock (outQueue)
                            {
                                if (mw != null)
                                {
                                    while (outQueue.Count > 0)
                                        mw.Write(outQueue.Dequeue());
                                    mw.Flush();
                                }
                            }
                        }
                    }
                    else
                        lock (outQueue)
                        {
                            outQueue.Clear();
                        }

                }
                catch (Exception ex)
                {
                    lock (outQueue)
                    {
                        if (mw != null)
                        {
                            mw.Write(new Info(ex.ToString()));
                            mw.Flush();
                        }
                    }
                }
            }
        }
        [DebuggerStepThrough]
        protected void ReceiveLoop()
        {
            MessageReader r;
            while ((r=mr)!=null)
            {
                Message msg = r.Read();
                if (msg != null)
                {
                    lock (inQueue)
                    {
                        inQueue.Enqueue(msg);
                    }
                }
            }
        }
        public void Send(Message msg)
        {
            if (msg == null)
                throw new ArgumentNullException("msg");
            outQueue.Enqueue(msg);
        }
        public Message Receive()
        {
            Message msg = null;
            if (inQueue.Count == 0)
                return null;
            lock (inQueue)
            {
                msg=inQueue.Dequeue();
            }
            return msg;
        }

        public void Dispouse()
        {
            kill = true;
            SetTCPClient(null);
        }
    }
}

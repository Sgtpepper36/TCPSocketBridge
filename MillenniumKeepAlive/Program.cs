using System;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Timers;

namespace MillenniumKeepAlive
{
    class Program
    {
        private static System.Timers.Timer aTimer;
        public static string RemoteServerIP = Properties.Settings.Default.RemoteServerIP;
        public static int RemoteServerPort = Properties.Settings.Default.RemoteServerPort;
        public static int LocalServerPort = Properties.Settings.Default.LocalServerPort;
        public static TcpClient RemoteServerSocket;
        public static TcpListener LocalServerSocket;
        public static TcpClient LocalServerSocketClient;
        public static NetworkStream RemoteStream;
        public static NetworkStream LocalStream;
        public static Thread RemoteStreamThread;
        public static Thread LocalStreamThread;
        public static int Status = 0;
        public static bool InUse = false;

        static void Main(string[] args)
        {
            ConnectRemoteServer(RemoteServerIP, RemoteServerPort);
            OpenLocalServer(LocalServerPort);
            RemoteStreamThread = new Thread(() => ExchangePackets(RemoteStream, LocalStream));
            RemoteStreamThread.Name = "RemoteStream";
            RemoteStreamThread.Start();
            LocalStreamThread = new Thread(() => ExchangePackets(LocalStream, RemoteStream));
            LocalStreamThread.Name = "LocalStream";
            LocalStreamThread.Start();
            while(true)
            {
                if (!RemoteStreamThread.IsAlive)
                {
                    if (RemoteServerSocket.Connected)
                    {
                        RemoteStream.Close();
                        RemoteServerSocket.Close();
                        
                        Console.WriteLine("Remote stream closed...");
                    }
                    Console.WriteLine("Remote socket closed...");
                    LocalStreamThread.Join();
                    //Thread.Sleep(5000);
                    Status = 0;
                    InUse = false;
                    RemoteServerSocket = null;
                    if (!LocalStreamThread.IsAlive)
                    {
                        ConnectRemoteServer(RemoteServerIP, RemoteServerPort);
                        RemoteStreamThread = new Thread(() => ExchangePackets(RemoteStream, LocalStream));
                        RemoteStreamThread.Name = "RemoteStream";
                        RemoteStreamThread.Start();
                        LocalStreamThread = new Thread(() => ExchangePackets(LocalStream, RemoteStream));
                        LocalStreamThread.Name = "LocalStream";
                        LocalStreamThread.Start();
                        Console.WriteLine(LocalServerSocketClient.Connected);
                    }
                }
            }
        }
        public static void ConnectRemoteServer(string RemoteServerIP, int RemoteServerPort)
        { 
            while (RemoteServerSocket == null)
            {
                try
                {
                    RemoteServerSocket = new TcpClient(RemoteServerIP, RemoteServerPort);
                    RemoteStream = RemoteServerSocket.GetStream();
                    //RemoteStream.ReadTimeout = 5000;
                    if (RemoteServerSocket.Connected)
                    {
                        Console.WriteLine("Connected to remote server...");
                        if (RemoteStream.CanRead & RemoteStream.CanWrite)
                        {
                            Console.WriteLine("Read/Write stream available on remote server...");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Remote Server Connect Error: " + ex.Message);
                }
            }
        }

        public static void OpenLocalServer(int LocalServerPort)
        {
            System.Timers.Timer ClientConnectTimer = new System.Timers.Timer(4000);
            try
            {
                while (LocalServerSocket == null)
                {
                    LocalServerSocket = new TcpListener(System.Net.IPAddress.Any, LocalServerPort);
                    LocalServerSocket.Start();
                    Console.WriteLine("Awaiting connection from local client...");
                    //while (!LocalServerSocket.Pending())
                    //{

                    //    Console.Write(".");
                    //}
                    LocalServerSocketClient = LocalServerSocket.AcceptTcpClient();
                    LocalStream = LocalServerSocketClient.GetStream();
                    //LocalStream.ReadTimeout = 5000;
                    if (LocalServerSocketClient.Connected)
                    {
                        Console.WriteLine("Connected to local client...");
                        if (LocalStream.CanRead & LocalStream.CanWrite)
                        {
                            Console.WriteLine("Read/Write stream available on local client...");
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("LocalServer Startup Error: " + ex.Message);
            }
        }



        /// Hanldes the bridge of data streams from Millennium interface to Opera server through this middleware, simply takes incoming and sends outgoing data
        private static void ExchangePackets(NetworkStream FirstStream, NetworkStream SecondStream)
        {
            SetTimer(SecondStream);
            Console.WriteLine(Thread.CurrentThread.Name + " has started...");
            while (Status == 0)
            {
                
                try
                {
                    //Console.WriteLine("Still a connection");

                    if (FirstStream.CanRead)
                    {
                        // create datastream format
                        byte[] myReadBuffer = new byte[1024];
                        //byte[] myReadBuffer2 = new byte[1024];
                        StringBuilder myCompleteMessage = new StringBuilder();
                        MemoryStream ms = new MemoryStream();
                        int numberOfBytesRead = 0;
                        //int numberOfBytesRead2 = 0;
                        int TotalBytesRead = 0;
                        // Incoming message may be larger than the buffer size. 
                        while (FirstStream.DataAvailable & Status == 0)
                        {
                            numberOfBytesRead = FirstStream.Read(myReadBuffer, 0, myReadBuffer.Length);
                            ms.Write(myReadBuffer, TotalBytesRead, numberOfBytesRead);
                            myCompleteMessage.AppendFormat("{0}", Encoding.ASCII.GetString(myReadBuffer, 0, numberOfBytesRead));
                            TotalBytesRead = TotalBytesRead + numberOfBytesRead;
                            Thread.Sleep(500);
                        }
                        // write data to console and then send on to other leg of stream
                        if (SecondStream.CanWrite & Status == 0 & numberOfBytesRead > 0)
                        {
                            InUse = true;
                            Console.WriteLine("You received the following message from " + Thread.CurrentThread.Name + " :" +
                             myCompleteMessage);
                            byte[] myWriteBuffer = ms.ToArray();
                            SecondStream.Write(myWriteBuffer, 0, myWriteBuffer.Length);
                            InUse = false;
                        }
                    }
                    else
                    {
                        Status = 1;
                        aTimer.Stop();
                        aTimer.Close();
                        break;
                    }
                }
                 catch (Exception ex)
            {
                    Console.WriteLine(Thread.CurrentThread.Name + " error: " + ex.Message);
                    aTimer.Stop();
                    aTimer.Close();
                }
            }
            Console.WriteLine(Thread.CurrentThread  + " closed");
            Status = 1;
            aTimer.Stop();
            aTimer.Close();
        }

        private static void SetTimer(NetworkStream Stream)
        {
            // Create a timer with a 4 minute interval.
            aTimer = new System.Timers.Timer(10000);
            // Hook up the Elapsed event for the timer. 
            aTimer.Elapsed += (sender, e) => OnTimedEvent(sender, e, Stream);
            aTimer.AutoReset = true;
            aTimer.Enabled = true;
        }

        /// executes at the end of the timer and sends message to opera server 
        static void OnTimedEvent(object sender, ElapsedEventArgs e, NetworkStream stream)
        {
            byte[] myWriteBuffer = Encoding.ASCII.GetBytes(" ");
            try
            {
                if (!InUse)
                {
                    stream.Write(myWriteBuffer, 0, myWriteBuffer.Length);
                }
            } catch (Exception ex)
            {
                Console.WriteLine("Timer Write Error: " + ex.Message);
                Status = 1;
               
                aTimer.Stop();
                aTimer.Close();
            }

        }
    }
}

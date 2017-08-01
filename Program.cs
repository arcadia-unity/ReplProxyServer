using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

/***
 * 
 * [ host (clojure socket repl) ] <---> [ local (PassThroughReplServer) ] <--->  [ client (editor/user) ]
 * 
 */
namespace PassThroughReplServer
{
    class Server
    {
        const int BUFFER_SIZE = 0x1000;

        TcpListener localListener;
        string hostAddress;
        int hostPort;

        static IPAddress GetAddress(string host)
        {
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length > 0)
                return addresses[0];
            return null;
        }

		/// <summary>
		/// Reads from input and writes to output. Blocks.
		/// 
		/// Modified from https://stackoverflow.com/questions/129305/how-to-write-the-content-of-one-stream-into-another-stream-in-net
		/// </summary>
		/// <returns>The number of bytes copied.</returns>
		/// <param name="input">Input stream</param>
		/// <param name="output">Output stream</param>
		static int CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            int bytesRead;
            bytesRead = input.Read(buffer, 0, buffer.Length);
            output.Write(buffer, 0, bytesRead);
            return bytesRead;
        }

        public Server(int localPort, string localAddress = "127.0.0.1", int hostPort = 5555, string hostAddress = "127.0.0.1")
        {
            localListener = new TcpListener(GetAddress(localAddress), localPort);
            this.hostAddress = hostAddress;
            this.hostPort = hostPort;
        }

        /// <summary>
        /// Spawn threads to copy bytes arriving from fromClient to toClient
        /// </summary>
        /// <param name="fromClient">Source client</param>
        /// <param name="toClient">Destination client</param>
        void PipeClients(TcpClient fromClient, TcpClient toClient)
        {
            new Thread(() =>
            {
                try
                {
                    int b;
                    while (fromClient.Connected && toClient.Connected)
                    {
                        b = CopyStream(fromClient.GetStream(), toClient.GetStream());
                        if (b == 0)
                            break;
                    }
                    if (!fromClient.Connected)
                        Console.WriteLine(fromClient.Client.LocalEndPoint + " disconnected");
                    if (!toClient.Connected)
                        Console.WriteLine(toClient.Client.LocalEndPoint + " disconnected");

                }
                catch (IOException)
                {
                    if (!fromClient.Connected)
                        Console.WriteLine(fromClient.Client.LocalEndPoint + " disconnected");
                    if (!toClient.Connected)
                        Console.WriteLine(toClient.Client.LocalEndPoint + " disconnected");
                }
            }).Start();
        }

        /// <summary>
        /// Spawn worker threads for new connection.
        /// 
        /// Loop to reconnect to host if host goes down.
        /// </summary>
        /// <param name="clientConnection">New client connection</param>
        public void AcceptConnection(TcpClient clientConnection)
        {
            Console.WriteLine("New client " + clientConnection.Client.LocalEndPoint);
            TcpClient hostClient = new TcpClient();
            new Thread(() =>
            {
                while (clientConnection.Connected)
                {
                    if (!hostClient.Connected)
                    {
                        try
                        {
                            Console.Write("Connecting " + clientConnection.Client.LocalEndPoint + " to " + hostAddress + ":" + hostPort + "... ");
                            hostClient = new TcpClient();
                            hostClient.Connect(GetAddress(hostAddress), hostPort);
                            Console.WriteLine("OK");
                            PipeClients(hostClient, clientConnection);
                            PipeClients(clientConnection, hostClient);
                        }
                        catch (SocketException ex)
                        {
                            Console.WriteLine(ex.Message);
                            Thread.Sleep(1000);
                        }
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                }
            }).Start();
        }

        /// <summary>
        /// Listen for incoming connections and spawn worker threads for them
        /// </summary>
        public void Listen()
        {
            try
            {
                Console.Write("Listening on " + localListener.LocalEndpoint + "... ");
                localListener.Start();
                Console.WriteLine("OK");
                while (true)
                {
                    AcceptConnection(localListener.AcceptTcpClient());
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("Quitting");
                return;
            }
        }
    }

    class Program
    {
        public static void Usage()
        {
            Console.WriteLine("PassThroughReplServer HOSTADDR HOSTPORT [LOCALHOST [LOCALPORT]]");
            Environment.Exit(0);
        }

        public static void Main(string[] args)
        {
            string localAddress = "127.0.0.1";
            int localPort = 5555;
            string hostAddress = "";
            int hostPort = 0;

            switch (args.Length)
            {
                case 4:
                    localPort = int.Parse(args[3]);
                    goto case 3;
                case 3:
                    localAddress = args[2];
                    goto case 2;
                case 2:
                    hostPort = int.Parse(args[1]);
                    hostAddress = args[0];
                    break;
                default:
                    Usage();
                    break;
            }

            var server = new Server(localPort, localAddress, hostPort, hostAddress);
            server.Listen();
        }
    }
}

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using VirtualMirror.Core;

namespace VirtualMirror.Vision {
    /// <summary>
    /// The socket and the thread under every vision receiver: it binds one UDP port, reads datagrams on a background thread into a single buffer,
    /// counts them, and hands each to <see cref="OnDatagram"/>. What a datagram means (a JSON message, or one chunk of a camera picture) is the
    /// subclass's business; <see cref="UdpVisionReceiver{TMessage}"/> keeps the newest small message, <see cref="CutoutReceiver"/> assembles frames.
    ///
    /// The sidecar is a separate process on purpose (ADR-016): a native inference crash can never take the player down. This is only a socket and
    /// a loop; no model, no camera, no native plugin runs in the player.
    ///
    /// <see cref="OnDatagram"/> runs on the receive thread and receives the SHARED buffer: it must read what it needs before it returns and keep no
    /// reference to the array.
    /// </summary>
    public abstract class UdpDatagramReceiver : IDisposable {
        public const int MaxDatagramBytes = 65536;
        private const int ReceiveTimeoutMilliseconds = 250;
        private const int JoinMilliseconds = 1000;

        private readonly string name;
        private readonly int requestedPort;
        private readonly IPAddress bindAddress;
        private readonly int socketBufferBytes;
        private readonly ILogService logService;
        private readonly byte[] buffer = new byte[MaxDatagramBytes];

        private Socket socket;
        private Thread thread;
        private volatile bool running;
        private int boundPort;
        private long received;
        private long oversize;

        /// <param name="socketBufferBytes">The operating system's receive queue for this socket. A one-datagram message needs little; a camera frame
        /// arrives as a burst of datagrams that must all fit while the thread catches up, so a frame receiver asks for megabytes.</param>
        protected UdpDatagramReceiver(string name, int port, bool loopbackOnly, int socketBufferBytes, ILogService logService) {
            this.name = name;
            requestedPort = port;
            bindAddress = loopbackOnly ? IPAddress.Loopback : IPAddress.Any;
            this.socketBufferBytes = socketBufferBytes;
            this.logService = logService;
        }

        public string Name {
            get {
                return name;
            }
        }

        /// <summary>The port actually bound (differs from the requested one when 0 was asked for). Zero before <see cref="Start"/>.</summary>
        public int Port {
            get {
                return boundPort;
            }
        }

        public bool IsRunning {
            get {
                return running;
            }
        }

        /// <summary>Datagrams received, understood or not.</summary>
        public long Received {
            get {
                return Interlocked.Read(ref received);
            }
        }

        /// <summary>Datagrams larger than the receive buffer, which the socket truncates and rejects.</summary>
        public long Oversize {
            get {
                return Interlocked.Read(ref oversize);
            }
        }

        /// <summary>Called on the receive thread for every datagram, with the shared buffer and the number of bytes in it.</summary>
        protected abstract void OnDatagram(byte[] data, int length);

        /// <summary>Binds the socket and starts the receive thread. False (and logged) when the port cannot be bound, e.g. it is already in use.</summary>
        public bool Start() {
            if (running) {
                return true;
            }
            try {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.ReceiveBufferSize = socketBufferBytes;
                socket.ReceiveTimeout = ReceiveTimeoutMilliseconds;
                socket.Bind(new IPEndPoint(bindAddress, requestedPort));
                boundPort = ((IPEndPoint)socket.LocalEndPoint).Port;
            } catch (Exception exception) {
                CloseSocket();
                Log(LogLevel.Error, name + " could not bind UDP " + bindAddress + ":" + requestedPort + " (is another receiver on that port?): " + exception.Message);
                return false;
            }
            running = true;
            thread = new Thread(ReceiveLoop);
            thread.IsBackground = true;
            thread.Name = name;
            thread.Start();
            Log(LogLevel.Info, name + " listening on " + bindAddress + ":" + boundPort);
            return true;
        }

        public void Stop() {
            running = false;
            CloseSocket();
            Thread worker = thread;
            thread = null;
            if (worker != null && worker != Thread.CurrentThread) {
                worker.Join(JoinMilliseconds);
            }
        }

        public void Dispose() {
            Stop();
        }

        protected void Log(LogLevel level, string message) {
            if (logService != null) {
                logService.Log(level, message);
            }
        }

        private void ReceiveLoop() {
            while (running) {
                int length;
                try {
                    length = socket.Receive(buffer);
                } catch (SocketException exception) {
                    if (!running) {
                        break;
                    }
                    if (exception.SocketErrorCode == SocketError.MessageSize) {
                        Interlocked.Increment(ref oversize);
                    }
                    continue;
                } catch (ObjectDisposedException) {
                    break;
                } catch (NullReferenceException) {
                    break;
                }
                if (length > 0) {
                    Interlocked.Increment(ref received);
                    OnDatagram(buffer, length);
                }
            }
        }

        private void CloseSocket() {
            Socket current = socket;
            socket = null;
            if (current != null) {
                try {
                    current.Close();
                } catch (Exception) {
                    // closing a socket that is already gone is the goal, not a failure
                }
            }
        }
    }
}

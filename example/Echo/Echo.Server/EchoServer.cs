﻿using NetGear.Core;
using NetGear.Core.Connection;
using NetGear.Core.Listener;
using System;
using System.Net;
using System.Net.Sockets;

namespace Echo.Server
{
    public sealed class EchoListener : BaseListener
    {
        bool _debug;

        public EchoListener(int maxConnectionCount, int bufferSize, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _debug = debug;
        }

        protected override BaseConnection CreateConnection(SocketAsyncEventArgs e)
        {
            return new EchoConnection(_connectedCount, e.AcceptSocket, this, _debug);
        }
    }

    public sealed class EchoConnection : StreamedSocketConnection
    {
        bool _disposed;
        EchoListener _listener;

        public EchoConnection(int id, Socket socket, EchoListener listener, bool debug)
            : base(id, socket, debug)
        {
            _listener = listener;            

            _readEventArgs = _listener.SocketAsyncReadEventArgsPool.Get() as PooledSocketAsyncEventArgs;
            _sendEventArgs = _listener.SocketAsyncSendEventArgsPool.Get() as PooledSocketAsyncEventArgs;

            _readAwait = new SocketAwaitable(_readEventArgs, Scheduler, debug);
            _sendAwait = new SocketAwaitable(_sendEventArgs, Scheduler, debug);
        }

        public override async void Start()
        {
            while (true)
            {
                try
                {
                    var str = await ReadString();
                    await Write(str);
                }
                catch (SocketException ex)
                {
                    Abort("远程连接被关闭");
                    break;
                }
                catch
                {
                    Close();
                    break;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _readAwait.Dispose();
                _sendAwait.Dispose();
                _readEventArgs.UserToken = null;
                _sendEventArgs.UserToken = null;
                _readEventArgs.Dispose();
                _sendEventArgs.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
            base.Dispose();
        }
    }

    public class EchoServer
    {
        bool _debug;
        int _bufferSize = 512;
        int _maxConnectionCount = 500;
        IPEndPoint _endPoint;
        BaseListener _listener;

        public EchoServer(string address, int port, bool debug = false)
        {
            _debug = debug;
            _listener = new EchoListener(_maxConnectionCount, _bufferSize, debug);
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);
        }

        public void Start()
        {
            ServerStarting();
            _listener.Start(_endPoint);
            ServerStarted();
            _listener.OnConnectionCreated += (sender, info) => { Console.WriteLine("新建立连接：" + info); };
            _listener.OnConnectionAborted += (sender, info) => { Console.WriteLine("连接被终止：" + info); };
            _listener.OnConnectionClosed += (sender, info) => { Console.WriteLine("连接关闭：" + info); };
        }

        public void Stop()
        {
            ServerStopping();
            _listener.Stop();
            ServerStopped();
        }

        private void ServerStarting()
        {
            Console.WriteLine("server开启中...");
        }

        private void ServerStarted()
        {
            Console.WriteLine("server启动成功");
        }

        private void ServerStopping()
        {
            Console.WriteLine("server关闭中...");
        }

        private void ServerStopped()
        {
            Console.WriteLine("server已关闭");
        }
    }
}

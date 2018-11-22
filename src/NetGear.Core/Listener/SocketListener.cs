using NetGear.Core.Connection;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NetGear.Core.Listener
{
    public sealed class SocketListener : BaseListener
    {
        bool _debug;
        bool _disposed;
        Thread _sendMessageWorker;
        BlockingCollection<Package> _sendingQueue;

        #region 事件
        public event EventHandler<Package> OnMessageReceived;
        public event EventHandler<Package> OnMessageSending;
        public event EventHandler<Package> OnMessageSent;
        #endregion

        public SocketListener(int maxConnectionCount, int bufferSize, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _debug = debug;
            _disposed = false;
            _sendingQueue = new BlockingCollection<Package>();
            _sendMessageWorker = new Thread(PorcessMessageQueue);
        }

        ~SocketListener()
        {
            Dispose(false);
        }

        public override void Start(IPEndPoint localEndPoint)
        {
            base.Start(localEndPoint);
            _sendMessageWorker.Start();
        }

        public override void Stop()
        {
            base.Stop();
            // 处理队列中剩余的消息
            Package package;
            while (_sendingQueue.TryTake(out package))
            {
                if (package != null)
                {
                    OnMessageSending?.Invoke(this, package);
                    OnInnerSending(package);
                    OnMessageSent?.Invoke(this, package);
                }
            }
        }

        protected override BaseConnection CreateConnection(SocketAsyncEventArgs e)
        {
            return new SocketConnection(_connectedCount, e.AcceptSocket, this, _debug);
        }

        public void Send(SocketConnection connection, string message)
        {
            var length = 0;
            var bytes = connection.GetMessageBytes(message, out length);
            var package = new Package
            {
                Connection = connection,
                MessageData = bytes,
                DataLength = length,
                RentFromPool = true
            };
            _sendingQueue.Add(package);
        }

        public void Send(SocketConnection connection, byte[] messageData, int length, bool rentFromPool = true)
        {
            var package = new Package
            {
                Connection = connection,
                MessageData = messageData,
                DataLength = length,
                RentFromPool = rentFromPool,
                NeedHead = true
            };
            _sendingQueue.Add(package);
        }

        private void PorcessMessageQueue()
        {
            while (true)
            {
                if (_shutdownEvent.Wait(0)) // 仅检查标志，立即返回
                {
                    // 关闭事件触发，退出loop
                    return;
                }

                var package = _sendingQueue.Take();
                if (package != null)
                {
                    OnMessageSending?.Invoke(this, package);
                    OnInnerSending(package);
                    OnMessageSent?.Invoke(this, package);
                }
            }
        }

        private void OnInnerSending(Package package)
        {
            package.Connection.InnerSend(package);
        }

        public void MessageReceived(SocketConnection connection, byte[] messageData, int length)
        {
            OnMessageReceived?.Invoke(this, new Package { Connection = connection, MessageData = messageData, DataLength = length, RentFromPool = true });
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
                _sendingQueue.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
            base.Dispose();
        }
    }
}

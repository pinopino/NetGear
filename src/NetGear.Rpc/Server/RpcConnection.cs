using NetGear.Core;
using NetGear.Core.Connection;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetGear.Rpc.Server
{
    public sealed class RpcConnection : StreamedConnection
    {
        RpcServer _server;
        RpcListener _listener;

        public RpcConnection(int id, RpcServer server, Socket socket, RpcListener listener, int bufferSize, bool debug)
            : base(id, socket, bufferSize, debug)
        {
            _listener = listener;
            _server = server;
        }

        public override async void Start()
        {
            while (true)
            {
                try
                {
                    await ProcessInvocation();
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted || ex.SocketErrorCode == SocketError.InvalidArgument)
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

        protected override void InitSAEA()
        {
            _readEventArgs = _listener.SocketAsyncReadEventArgsPool.Get();
            _sendEventArgs = _listener.SocketAsyncSendEventArgsPool.Get();
        }

        protected override void ReleaseSAEA()
        {
            _readEventArgs.UserToken = null;
            _sendEventArgs.UserToken = null;
            _listener.SocketAsyncReadEventArgsPool.Put((PooledSocketAsyncEventArgs)_readEventArgs);
            _listener.SocketAsyncSendEventArgsPool.Put((PooledSocketAsyncEventArgs)_sendEventArgs);
        }

        private async Task ProcessInvocation()
        {
            // 读取调用信息
            var obj = await ReadObject<InvokeInfo>();

            // 准备调用方法
            ServiceInfo invokedInstance;
            if ( _server.Services.TryGetValue(obj.ServiceHash, out invokedInstance))
            {
                int index = obj.MethodIndex;
                object[] parameters = new object[obj.Parameters.Count];
                for (int i = 0; i < parameters.Length; i++)
                {
                    parameters[i] = obj.Parameters[i].UntypedValue;
                }

                //invoke the method
                object returnValue;
                var returnMessageType = MessageType.ReturnValues;
                try
                {
                    returnValue = invokedInstance.Methods[index].Invoke(invokedInstance.Instance, parameters);
                }
                catch (Exception ex)
                {
                    //an exception was caught. Rethrow it client side
                    returnValue = ex;
                    returnMessageType = MessageType.ThrowException;
                }

                var returnObj = new InvokeReturn
                {
                    ReturnType = (int)returnMessageType,
                    ReturnValue = InvokeParam.CreateDynamic(returnValue == null ? new NULL() : returnValue)
                };
                //send the result back to the client
                // (2) write the return parameters
                await Write(returnObj);
            }
            else
                await Write(new InvokeReturn { ReturnType = (int)MessageType.UnknownMethod });
        }
    }
}

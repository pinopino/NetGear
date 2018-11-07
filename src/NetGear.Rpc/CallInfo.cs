using System.Collections.Generic;
using System.Reflection;

namespace NetGear.Rpc
{
    public class ServiceInfo
    {
        public object Instance;
        public Dictionary<int, MethodInfo> Methods;
    }

    public class InvokeInfo
    {
        public ulong ServiceHash;
        public int MethodIndex;
        public object[] Parameters;
    }

    public class InvokeReturn
    {
        public int ReturnType;
        public object[] ReturnParameters;
    }

    public enum MessageType
    {
        MethodInvocation = 1,
        ReturnValues = 2,
        UnknownMethod = 3,
        ThrowException = 4
    }
}

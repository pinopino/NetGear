using System;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using NetGear.Rpc.Client;

namespace NetGear.Example.Rpc
{
	public class DataContractProxy : IDataContract
	{
		ulong _serviceHash;
		StreamedRpcClient _client;

		public DataContractProxy(IPEndPoint endPoint)
		{
			_client = new StreamedRpcClient(typeof(IDataContract), endPoint);
			_serviceHash = CalculateHash(typeof(IDataContract).FullName);
		}

		public Int64 AddMoney(Int32 input1, Int64 input2)
		{
			var ret = _client.InvokeMethod(_serviceHash, 1, input1, input2);
			return (Int64)ret;
		}

		private ulong CalculateHash(string str)
		{
			var hashedValue = 3074457345618258791ul;
			for (var i = 0; i < str.Length; i++)
			{
				hashedValue += str[i];
				hashedValue *= 3074457345618258799ul;
			}
			return hashedValue;
		}
	}
}
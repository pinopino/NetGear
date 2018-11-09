using System;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using NetGear.Rpc.Client;

namespace NetGear.Example.Rpc
{
	public class TestContractProxy : ITestContract
	{
		ulong _serviceHash;
		StreamedRpcClient _client;

		public TestContractProxy(IPEndPoint endPoint)
		{
			_client = new StreamedRpcClient(typeof(ITestContract), endPoint);
			_serviceHash = CalculateHash(typeof(ITestContract).FullName);
        }

        public ComplexResponse Get(Guid id, String label, Double weight, Int64 quantity)
		{
			var ret = _client.InvokeMethod(_serviceHash, 1, id, label, weight, quantity);
			return (ComplexResponse)ret;
		}

		public Decimal GetDecimal(Decimal input)
		{
			var ret = _client.InvokeMethod(_serviceHash, 2, input);
			return (Decimal)ret;
		}

		public Guid GetId(String source, Double weight, Int32 quantity, DateTime dt)
		{
			var ret = _client.InvokeMethod(_serviceHash, 3, source, weight, quantity, dt);
			return (Guid)ret;
		}

		public List<String> GetItems(Guid id)
		{
			var ret = _client.InvokeMethod(_serviceHash, 4, id);
			return (List<String>)ret;
		}

		public Boolean OutDecimal(Decimal val)
		{
			var ret = _client.InvokeMethod(_serviceHash, 5, val);
			return (Boolean)ret;
		}

		public Int64 TestLong(Int64 id1, List<Int64> id2)
		{
			var ret = _client.InvokeMethod(_serviceHash, 6, id1, id2);
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
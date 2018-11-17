/*
 *  2018-11-12 22:43:19
 *  本文件由生成工具自动生成，请勿随意修改内容除非你很清楚自己在做什么！
 */

using System;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using NetGear.Rpc.Client;
using NetGear.Rpc;

namespace NetGear.Example.Rpc
{
	public class TestContractProxy : BaseProxy, ITestContract
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

		public Boolean OutDecimal(Decimal val)
		{
			var ret = _client.InvokeMethod(_serviceHash, 4, val);
			return (Boolean)ret;
		}

		public Int64 TestLong(Int64 id1, List<Int64> id2)
		{
			var ret = _client.InvokeMethod(_serviceHash, 5, id1, id2);
			return (Int64)ret;
		}

		public void TestVoid()
		{
			var ret = _client.InvokeMethod(_serviceHash, 6);
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
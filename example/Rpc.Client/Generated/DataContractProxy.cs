/*
 *  2018-11-10 11:52:44
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
	public class DataContractProxy : IDataContract
	{
		ulong _serviceHash;
		StreamedRpcClient _client;

		static DataContractProxy()
		{
		}

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
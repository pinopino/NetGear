/*
 *  2018-11-12 22:43:19
 *  本文件由生成工具自动生成，请勿随意修改内容除非你很清楚自己在做什么！
 */

using NetGear.Rpc;
using ProtoBuf;
using System;
using System.Collections.Generic;

namespace NetGear.Example.Rpc
{
    public class BaseProxy
    {
        static BaseProxy()
        {
            int index = 10;
            ProtoBuf.Meta.RuntimeTypeModel.Default[typeof(InvokeParam)]
                .AddSubType(index++, typeof(InvokeParam<Boolean>))
                .AddSubType(index++, typeof(InvokeParam<Char>))
                .AddSubType(index++, typeof(InvokeParam<SByte>))
                .AddSubType(index++, typeof(InvokeParam<Byte>))
                .AddSubType(index++, typeof(InvokeParam<Int16>))
                .AddSubType(index++, typeof(InvokeParam<UInt16>))
                .AddSubType(index++, typeof(InvokeParam<Int32>))
                .AddSubType(index++, typeof(InvokeParam<UInt32>))
                .AddSubType(index++, typeof(InvokeParam<Int64>))
                .AddSubType(index++, typeof(InvokeParam<UInt64>))
                .AddSubType(index++, typeof(InvokeParam<Single>))
                .AddSubType(index++, typeof(InvokeParam<Double>))
                .AddSubType(index++, typeof(InvokeParam<Decimal>))
                .AddSubType(index++, typeof(InvokeParam<DateTime>))
                .AddSubType(index++, typeof(InvokeParam<String>))
                .AddSubType(index++, typeof(InvokeParam<Guid>))

                .AddSubType(index++, typeof(InvokeParam<List<Boolean>>))
                .AddSubType(index++, typeof(InvokeParam<List<Char>>))
                .AddSubType(index++, typeof(InvokeParam<List<SByte>>))
                .AddSubType(index++, typeof(InvokeParam<List<Byte>>))
                .AddSubType(index++, typeof(InvokeParam<List<Int16>>))
                .AddSubType(index++, typeof(InvokeParam<List<UInt16>>))
                .AddSubType(index++, typeof(InvokeParam<List<Int32>>))
                .AddSubType(index++, typeof(InvokeParam<List<UInt32>>))
                .AddSubType(index++, typeof(InvokeParam<List<Int64>>))
                .AddSubType(index++, typeof(InvokeParam<List<UInt64>>))
                .AddSubType(index++, typeof(InvokeParam<List<Single>>))
                .AddSubType(index++, typeof(InvokeParam<List<Double>>))
                .AddSubType(index++, typeof(InvokeParam<List<Decimal>>))
                .AddSubType(index++, typeof(InvokeParam<List<DateTime>>))
                .AddSubType(index++, typeof(InvokeParam<List<String>>))
                .AddSubType(index++, typeof(InvokeParam<List<Guid>>))

                .AddSubType(index++, typeof(InvokeParam<Boolean[]>))
                .AddSubType(index++, typeof(InvokeParam<Char[]>))
                .AddSubType(index++, typeof(InvokeParam<SByte[]>))
                .AddSubType(index++, typeof(InvokeParam<Byte[]>))
                .AddSubType(index++, typeof(InvokeParam<Int16[]>))
                .AddSubType(index++, typeof(InvokeParam<UInt16[]>))
                .AddSubType(index++, typeof(InvokeParam<Int32[]>))
                .AddSubType(index++, typeof(InvokeParam<UInt32[]>))
                .AddSubType(index++, typeof(InvokeParam<Int64[]>))
                .AddSubType(index++, typeof(InvokeParam<UInt64[]>))
                .AddSubType(index++, typeof(InvokeParam<Single[]>))
                .AddSubType(index++, typeof(InvokeParam<Double[]>))
                .AddSubType(index++, typeof(InvokeParam<Decimal[]>))
                .AddSubType(index++, typeof(InvokeParam<DateTime[]>))
                .AddSubType(index++, typeof(InvokeParam<String[]>))
                .AddSubType(index++, typeof(InvokeParam<Guid[]>))
                
                .AddSubType(index++, typeof(InvokeParam<NULL>))
				.AddSubType(100, typeof(InvokeParam<ComplexResponse>));
        }
    }
}

# NetGear
基于[System.IO.Pipelines](https://devblogs.microsoft.com/dotnet/system-io-pipelines-high-performance-io-in-net/)的简单易用、高性能网络组件

### 前置说明
基本上是从以下两个项目拼拼凑凑而来：
- [Pipelines.Sockets.Unofficial](https://github.com/mgravell/Pipelines.Sockets.Unofficial)
- [KestrelHttpServer](https://github.com/aspnet/KestrelHttpServer)

感谢作者的辛勤劳动！

拼凑的过程中重新设计了核心对象模型，添加了一些必要的抽象关系，删除了一些自己觉得无用的功能模块（比如`KestrelHttpServer`毕竟是一个完整的http服务器，`NetGear`则在更底层的地方所以很多应用层的概念、抽象并没有挪过来）。如你所想，所有这些的目的都是为了给设计目标服务的。

### 设计目标
我期望这套网络组件能够真正的像齿轮（`gears`）一样方便的拆分、组合使用，因此在保证正确性的前提下尽量设计了清晰的对象模型，抽象关系等；

同时，亦不可太过标新立异，服务端编程的那套基本样子还是保持了下来。别无它意，就是让有服务端开发经验的程序员能够快速理解、掌握。

基本的项目结构如下：
#### NetGear.Core
核心的抽象都在该项目中，两个重要的类型：
- `ITransport`
  将底层不同的传输协议做了抽象，因此你可以适配`tcp/udp`、`pipe`，又或者是`libuv`
  
- `SocketConnection`
  选用`tcp`做传输层时对应的产物。因为继承自`IDuplexPipe`，因此除了抽象表示新建立的socket连接之外，它还有一个重要作用就是桥接底层的socket和上层的管道消费者。
  > *你可能会好奇为何一个具体的SoketConnection类型会在Core名称空间下？请暂时理解为默认提供一个开箱即用的类型好了。*
  
#### NetGear.Pipeline
该项目的主要目的就是抽象靠近业务这一侧的上层管道消费者。比较核心的类型有：
- `DuplexPipeServer`
  服务端server应该有的功能，包括启动、停止等；提供了不少方便的虚函数供子类型在特定事件发生时重写响应逻辑使用；持有另外，一个重要的能力就是持有了上述双工管道的另一侧，并驱动下层的pipe读写！
- `DuplexPipeClient`
  基本同server类型。
  
#### NetGear.Libuv
该项目用于使用libuv实现了上述`ITransport`接口。目的有二：
- 了解、学习与非托管交互的写法
- 实在是很喜欢`libuv`的精巧细致（当然，实际写作下来比较悲剧的是托管、非托管这样子交互实在是够呛，有点两头都不讨好的感觉 :(）
  > *我没有记错的话从 .net core 3.0开始，托管socket已经可以非常高效的运行在linux系统上了*

所以libuv相关实现没有进Core也就不奇怪了。是的，独立成一个程序集，你想要用的时候再用吧。

### 基本使用
#### 服务端
首先继承`DuplexPipeServer`并重载`OnReceiveForReplyAsync`方法以提供我们自定义的收到消息后逻辑：
```csharp
public class Server : DuplexPipeServer
{
    protected override ValueTask<IMemoryOwner<byte>> OnReceiveForReplyAsync(IMemoryOwner<byte> message)
    {
        var memory = message.Memory;
        memory.Span.Reverse();
        return new ValueTask<IMemoryOwner<byte>>(message);
    }
}
```

接着就是使用上面准备好的类型：
```csharp
static async Task Main(string[] args)
{
    var endPoint = new IPEndPoint(IPAddress.Loopback, 5000);

    // 开启server
    using (var server = new Server())
    {
        await server.StartAsync(new ListenOptions(endPoint));

        string line;
        while ((line = await Console.In.ReadLineAsync()) != null)
        {
            if (line == "q")
                break;

            int clientCount, len;
            using (var leased = line.EncodeWithOwnership())
            {
                len = leased.Memory.Length;
                clientCount = await server.BroadcastAsync(leased.Memory);
            }
            await Console.Out.WriteLineAsync(
                $"Broadcast {len} bytes to {clientCount} clients");
        }
    }
}
```
至此，一个简单的服务端就算搭建完毕。

#### 客户端
也很简单：
```csharp
static async Task Main(string[] args)
{
    var endPoint = new IPEndPoint(IPAddress.Loopback, 5000);

    // 开启client
    using (var client = await DuplexPipeClient.ConnectAsync(new ListenOptions(endPoint)))
    {
        // subscribe to broadcasts
        client.Broadcast += async msg =>
        {
            if (!msg.Memory.IsEmpty)
                await WriteLineAsync("*", msg);
        };

        string line;
        while ((line = await Console.In.ReadLineAsync()) != null)
        {
            if (line == "q")
                break;

            using (var leased = line.EncodeWithOwnership())
            {
                var response = await client.SendReceiveAsync(leased.Memory);
                await WriteLineAsync("<", response);
            }
        }
    }
}

static async Task WriteLineAsync(string prefix, IMemoryOwner<byte> message)
{
    await Console.Out.WriteLineAsync($"{prefix} {Encoding.UTF8.GetString(message.Memory.Span)}");
}
```

### 路线图
当前版本，按我目前的评判最多只能算作是`0.1.0`，还有很长的路要走啊！
- **添加单元测试**  
  可以考虑从参考项目中直接照搬单元测试，可能有一定的适配工作要做
- **线程模型**  
  还需要再整理下，提供一些更明确、易用的接口（当前因为有libuv的存在显得比较混乱）
- **example**  
  添加一些更复杂、更具有实际意义的example，后期可以考虑成型的example独立出去
  - http server
  - game server
  - rpc server
- **日志模块**  
- **配置模块**  
- **添加性能测试**  
  当中涉及到一些重点的性能计数（参考项目中也有类似的逻辑），需要逐步完善
- **协议解析模块**  
  目前封包格式我是写死掉的（个人觉得并无大碍，没必要为了灵活而灵活）
  
其它可能还有一些暂时没有想到的，以后会逐步添加到这里。
> ps：上面所列各项按优先级排列

### License
项目是基于[MIT license](LICENSE)的，所以请放心的使用。

### Contributing
项目目前处于早期阶段，结构简单、逻辑易懂是品尝试用的最佳时机:)。如有问题请提交`issue`，或者不吝赐教提交`pull request`，谢谢。



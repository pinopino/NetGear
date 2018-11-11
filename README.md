# NetGear
简单易用的网络组件

### 对象模型
项目包含了一些常见的网络基础组件，组件间包含简单的继承关系：
```
connection:
                   +-----------------+
                   |                 |
                   |  BaseConnection |
                   |                 |
                   +-+------+------+-+
                     |      |      |
                     |      |      |
      +--------------+---+  |  +---+----------------------+
      |                  |  |  |                          |
      | SocketConnection |  |  | StreamedSocketConnection |
      |                  |  |  |                          |
      +------------------+  |  +-------------+------------+
                            |                |
+------------------------+  |                |
|                        |  |                |
| SocketClientConnection +--+  +-------------+------------------+
|                        |     |                                |
+------------------------+     | StreamedSocketClientConnection |
                               |                                |
                               +--------------------------------+
```

```
listener:
           +----------------+
           |                |
           |  BaseListener  |
           |                |
           +---+--------+---+
               |        |
               |        |
               |        |
               |        |
+--------------+-+    +-+-------------+
|                |    |               |
| SocketListener |    |  RpcListener  |
|                |    |               |
+----------------+    +---------------+

```
上述类型大致分了两种，一种是普通的socket读写，另一种则是streamed读写。

两种方式各有优劣。普通的socket读写方式来驱动程序，业务协议的体现主要集中在每次recv完成后的回调处理中，然而回调能读取多少byte都不是程序能够控制的（那是属于tcp协议的东西），类似这种tcp粘包问题的处理会蔓延在整个对象模型中下层的各个地方，很丑陋，很难维护；

streamed读写则没有这种困扰（好吧其实还是有，就在那最底层的`stream.write`处），并且由于tcp本身就是一种流式协议，所以使用stream读写似乎显得更为自然。业务协议的体现现在也简单得不得了：
```csharp
    var len = await stream.write(4);
    var body = await stream.write(len);
```

这就读取到一条消息了！但是，这里总共进行了两次IO，这意味着什么？意味着更多的上下文切换，更多的资源占用和更低下的性能。

### 线程模型
.net世界的[IOCP](https://www.ibm.com/developerworks/cn/java/j-lo-iocp/index.html)天生具有[Reactor](http://ifeve.com/netty-reactor-4/)的特质，基于`APM`、`TAP`又或者是[SocketAsyncEventArgs](https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socketasynceventargs?view=netframework-4.7.2)的方式在内部微软都封装好了`IOCP`的使用。于是，参照经典的reactor线程模型，listener就是main reactor，负责侦听端口accept新进入的socket；而每个新建立的connection则都与sub reactor绑定好了。内部`IOCP`作为事件通知方驱动了整个程序向前运行。唯一需要担心的是，当连接数量过多的时候线程池的性能问题（事件的回调都会在线程池上完成）。线程过多可不是一件好事情，上下文切换带来的开销，以及对系统资源的争抢都会降低系统整体性能。

目前使用了一个叫做`IOCompletionPortTaskScheduler`的类型来限制可用线程数量，所有的处理回调都会在这个scheduler上执行；这里需要注意的一点是，另外一个类型，[SocketAwaitable](https://blogs.msdn.microsoft.com/pfxteam/2011/12/15/awaiting-socket-operations/)。它存在的全部意义就在于能够在享受`await/async`编译器支持的同时还可以对socket异步操作进行“高效”等待。高效二字的体现就是并没有task对象的产生，这在巨量异步操作的情况下能够非常有效的降低gc的开销。而现在，`IOCompletionPortTaskScheduler`每一次操作都会生成一个新的task对象，这二者是仇家，是互相拆家的仇家。

### GC控制
对于streamed类型来说，流的读写是直接基于`SocketAsyncEventArgs`的buffer来做的，这也是一个有好有坏的设计。好处在于，如果saea对象的缓冲区足够大，那么`stream.read`的过程中不会有任何内存拷贝（当然如果缓冲区偏小，那么多次read操作必定需要一块额外的buffer来存放多次操作的结果）；坏处呢，也很明显，saea被设计来一次异步操作完成之后是马上可以复用的，按照这种设计，在业务说可以之前，saea是会被一直占用的！

另外关于byte的使用，目前项目中仅仅只是简单的使用了一个名为[ArrayPool](https://adamsitnik.com/Array-Pool/)的类型，让大量byte操作会用到的缓冲区全部取自这里；比较关键的一点是这些被`rent`出来的byte[]要在使用完毕后小心的`return`回去。

### 优化方向
关于服务端编程，个人认为三个方面最为重要：
* ***线程模型***
* ***GC控制***
* ***API设计（对象模型）***
  
上面的描述从这三个方面对这个项目进行了简单的描述；当中不少的问题即是将来的优化、重构方向：
* IO合并
* 需要一个不会产生task对象的scheduler（可能导致直接不走TAP模式）

### 如何使用
使用方式将会非常简单：
1. 定义你自己的协议
   ```csharp
   public interface IDataContract
   {
        long AddMoney(int input1, int input2);
   }
   ```

2. 使用自带的RpcGenerator生成器生成相应的操作类
    * *AppConfig指定好输入协议路径*
    * *AppConfig指定好输出类文件路径*
    * *运行Generator*
  
3. 你将会得到`BaseProxy.cs` `DataContractProxy.cs`两个类文件，将它们拷贝至Client项目中
    ```csharp
    var remote = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5000);
    var proxy = new DataContractProxy(remote);
    var ret = proxy.AddMoney(1, 2);
    Console.WriteLine("返回值：" + ret);
    ```

4. 我们需要在服务端实现之前定义好的协议，并注册它
    ```csharp
    public class DataContractImpl : IDataContract
    {
        public long AddMoney(int input1, int input2)
        {
            return input1 + input2;
        }
    }

    var server = new RpcServer("127.0.0.1", 5000);
    var simpleContract = new DataContractImpl();
    server.AddService<IDataContract>(simpleContract);
    server.Start();
    ```
5. we are done here, cool!
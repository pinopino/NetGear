using System;
using System.Runtime.InteropServices;

namespace NetGear.Libuv
{
    public class UvRequest : UvMemory
    {
        protected UvRequest(ILibuvTrace logger)
            : base(logger, GCHandleType.Normal)
        { }

        public virtual void Init(UvThread thread)
        {
            // 说明：request都关联到某一个具体的uv线程上
#if DEBUG
            // Store weak handles to all UvRequest objects so we can do leak detection
            // while running tests
            thread.Requests.Add(new WeakReference(this));
#endif
        }

        protected override bool ReleaseHandle()
        {
            DestroyMemory(handle);
            handle = IntPtr.Zero;
            return true;
        }
    }
}

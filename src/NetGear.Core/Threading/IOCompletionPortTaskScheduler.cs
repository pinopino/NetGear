//--------------------------------------------------------------------------
// 
//  Copyright (c) Microsoft Corporation.  All rights reserved. 
// 
//  File: IOCompletionPortTaskScheduler.cs
//
//--------------------------------------------------------------------------

using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetGear.Core.Threading
{
    // 说明：原版本来自于微软，这里作了修改去除了task，试图消除巨量task带来gc压力
    // todo: Kestrel中控制线程的方式跟这个在思路上是很类似的，比较感兴趣二者的性能对比
    /// <summary>Provides a TaskScheduler that uses an I/O completion port for concurrency control.</summary>
    public sealed class IOCompletionPortTaskScheduler : IScheduler, IDisposable
    {
        /// <summary>The queue of tasks to be scheduled.</summary>
        private readonly ConcurrentQueue<Work> m_tasks;
        /// <summary>The I/O completion port to use for concurrency control.</summary>
        private readonly IOCompletionPort m_iocp;
        /// <summary>Whether the current thread is a scheduler thread.</summary>
        private ThreadLocal<bool> m_schedulerThread;
        /// <summary>Event used to wait for all threads to shutdown.</summary>
        private CountdownEvent m_remainingThreadsToShutdown;

        /// <summary>Initializes the IOCompletionPortTaskScheduler.</summary>
        /// <param name="maxConcurrencyLevel">The maximum number of threads in the scheduler to be executing concurrently.</param>
        /// <param name="numAvailableThreads">The number of threads to have available in the scheduler for executing tasks.</param>
        public IOCompletionPortTaskScheduler(int maxConcurrencyLevel, int numAvailableThreads)
        {
            // Validate arguments
            if (maxConcurrencyLevel < 1) throw new ArgumentNullException("maxConcurrencyLevel");
            if (numAvailableThreads < 1) throw new ArgumentNullException("numAvailableThreads");

            m_tasks = new ConcurrentQueue<Work>();
            m_iocp = new IOCompletionPort(maxConcurrencyLevel);
            m_schedulerThread = new ThreadLocal<bool>();
            m_remainingThreadsToShutdown = new CountdownEvent(numAvailableThreads);

            // Create and start the threads
            for (int i = 0; i < numAvailableThreads; i++)
            {
                new Thread(() =>
                {
                    try
                    {
                        // Note that this is a scheduler thread.  Used for inlining checks.
                        // todo: 去掉task之后，inline的情况可能需要手动判断手动处理了
                        m_schedulerThread.Value = true;

                        // Continually wait on the I/O completion port until 
                        // there's a work item, then process it.
                        while (m_iocp.WaitOne())
                        {
                            Work next;
                            if (m_tasks.TryDequeue(out next))
                                next.Callback(next.State);
                        }
                    }
                    finally { m_remainingThreadsToShutdown.Signal(); }
                })
                { IsBackground = true }.Start();
            }
        }

        public void QueueTask(Action<object> action, object state)
        {
            // Store the task and let the I/O completion port know that more work has arrived.
            m_tasks.Enqueue(new Work { Callback = action, State = state });
            m_iocp.NotifyOne();
        }

        /// <summary>Dispose of the scheduler.</summary>
        public void Dispose()
        {
            // Close the I/O completion port.  This will cause any threads blocked
            // waiting for items to wake up.
            m_iocp.Dispose();

            // Wait for all threads to shutdown.  This could cause deadlock
            // if the current thread is calling Dispose or is part of such a cycle.
            m_remainingThreadsToShutdown.Wait();
            m_remainingThreadsToShutdown.Dispose();

            // Clean up remaining state
            m_schedulerThread.Dispose();
        }

        /// <summary>Provides a simple managed wrapper for an I/O completion port.</summary>
        private sealed class IOCompletionPort : IDisposable
        {
            /// <summary>Infinite timeout value to use for GetQueuedCompletedStatus.</summary>
            private UInt32 INFINITE_TIMEOUT = unchecked((UInt32)Timeout.Infinite);
            /// <summary>An invalid file handle value.</summary>
            private IntPtr INVALID_FILE_HANDLE = unchecked((IntPtr)(-1));
            /// <summary>An invalid I/O completion port handle value.</summary>
            private IntPtr INVALID_IOCP_HANDLE = IntPtr.Zero;

            /// <summary>The I/O completion porth handle.</summary>
            private SafeFileHandle m_handle;

            /// <summary>Initializes the I/O completion port.</summary>
            /// <param name="maxConcurrencyLevel">The maximum concurrency level allowed by the I/O completion port.</param>
            public IOCompletionPort(Int32 maxConcurrencyLevel)
            {
                // Validate the argument and create the port.
                if (maxConcurrencyLevel < 1) throw new ArgumentOutOfRangeException("maxConcurrencyLevel");
                m_handle = CreateIoCompletionPort(INVALID_FILE_HANDLE, INVALID_IOCP_HANDLE, UIntPtr.Zero, (UInt32)maxConcurrencyLevel);
            }

            /// <summary>Clean up.</summary>
            public void Dispose() { m_handle.Dispose(); }

            /// <summary>Notify that I/O completion port that new work is available.</summary>
            public void NotifyOne()
            {
                if (!PostQueuedCompletionStatus(m_handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception();
            }

            /// <summary>Waits for an item on the I/O completion port.</summary>
            /// <returns>true if an item was available; false if the completion port closed before an item could be retrieved.</returns>
            public bool WaitOne()
            {
                // Wait for an item to be posted.
                // DangerousGetHandle is used so that the safe handle can be closed even while blocked in the call to GetQueuedCompletionStatus.
                UInt32 lpNumberOfBytes;
                IntPtr lpCompletionKey, lpOverlapped;
                if (!GetQueuedCompletionStatus(m_handle.DangerousGetHandle(), out lpNumberOfBytes, out lpCompletionKey, out lpOverlapped, INFINITE_TIMEOUT))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    if (errorCode == 735 /*ERROR_ABANDONED_WAIT_0*/ || errorCode == 6 /*INVALID_HANDLE*/)
                        return false;
                    else
                        throw new Win32Exception(errorCode);
                }
                return true;
            }

            /// <summary>
            /// Creates an input/output (I/O) completion port and associates it with a specified file handle, 
            /// or creates an I/O completion port that is not yet associated with a file handle, allowing association at a later time.
            /// </summary>
            /// <param name="fileHandle">An open file handle or INVALID_HANDLE_VALUE.</param>
            /// <param name="existingCompletionPort">A handle to an existing I/O completion port or NULL.</param>
            /// <param name="completionKey">The per-handle user-defined completion key that is included in every I/O completion packet for the specified file handle.</param>
            /// <param name="numberOfConcurrentThreads">The maximum number of threads that the operating system can allow to concurrently process I/O completion packets for the I/O completion port.</param>
            /// <returns>If the function succeeds, the return value is the handle to an I/O completion port.  If the function fails, the return value is NULL.</returns>
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern SafeFileHandle CreateIoCompletionPort(
                  IntPtr fileHandle, IntPtr existingCompletionPort, UIntPtr completionKey, UInt32 numberOfConcurrentThreads);

            /// <summary>Attempts to dequeue an I/O completion packet from the specified I/O completion port.</summary>
            /// <param name="completionPort">A handle to the completion port.</param>
            /// <param name="lpNumberOfBytes">A pointer to a variable that receives the number of bytes transferred during an I/O operation that has completed.</param>
            /// <param name="lpCompletionKey">A pointer to a variable that receives the completion key value associated with the file handle whose I/O operation has completed.</param>
            /// <param name="lpOverlapped">A pointer to a variable that receives the address of the OVERLAPPED structure that was specified when the completed I/O operation was started.</param>
            /// <param name="dwMilliseconds">The number of milliseconds that the caller is willing to wait for a completion packet to appear at the completion port. </param>
            /// <returns>Returns nonzero (TRUE) if successful or zero (FALSE) otherwise.</returns>
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern Boolean GetQueuedCompletionStatus(
                IntPtr completionPort, out UInt32 lpNumberOfBytes, out IntPtr lpCompletionKey, out IntPtr lpOverlapped, UInt32 dwMilliseconds);

            /// <summary>Posts an I/O completion packet to an I/O completion port.</summary>
            /// <param name="completionPort">A handle to the completion port.</param>
            /// <param name="dwNumberOfBytesTransferred">The value to be returned through the lpNumberOfBytesTransferred parameter of the GetQueuedCompletionStatus function.</param>
            /// <param name="dwCompletionKey">The value to be returned through the lpCompletionKey parameter of the GetQueuedCompletionStatus function.</param>
            /// <param name="lpOverlapped">The value to be returned through the lpOverlapped parameter of the GetQueuedCompletionStatus function.</param>
            /// <returns>If the function succeeds, the return value is nonzero. If the function fails, the return value is zero.</returns>
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern Boolean PostQueuedCompletionStatus(
                SafeFileHandle completionPort, IntPtr dwNumberOfBytesTransferred, IntPtr dwCompletionKey, IntPtr lpOverlapped);
        }
    }
}

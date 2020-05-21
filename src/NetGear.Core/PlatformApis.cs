// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace NetGear.Core
{
    public static class PlatformApis
    {
        static PlatformApis()
        {
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            IsDarwin = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        }

        public static bool IsWindows { get; }

        public static bool IsLinux { get; }

        public static bool IsDarwin { get; }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
[assembly: AssemblyVersion("1.0.0.0")]
namespace PlaySessions.ExternalFixture
{
    // Compiled as a real DLL with no dependency on Unity or Play Sessions.
    public static class VendorCache
    {
        public static readonly List<int> Values = new List<int>();
        public static event Action Changed;
        public static int Subscribers => Changed == null ? 0 : Changed.GetInvocationList().Length;
        public static void Add(int value) { Values.Add(value); Changed?.Invoke(); }
        public static void Reset() { Values.Clear(); Changed = null; }
    }
}

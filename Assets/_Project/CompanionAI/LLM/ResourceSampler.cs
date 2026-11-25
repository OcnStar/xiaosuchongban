using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Xiaosu.CompanionAI.LLM
{
    public sealed class MachineResourceSnapshot
    {
        public string OsDescription { get; set; }
        public string ProcessArchitecture { get; set; }
        public string FrameworkDescription { get; set; }
        public int ProcessorCount { get; set; }
        public long ManagedHeapBytes { get; set; }
        public long WorkingSetBytes { get; set; }
        public int GcGen0Count { get; set; }
        public int GcGen1Count { get; set; }
        public int GcGen2Count { get; set; }
    }

    public static class ResourceSampler
    {
        public static MachineResourceSnapshot Capture()
        {
            long workingSet = 0;
            try
            {
                using (var proc = Process.GetCurrentProcess())
                {
                    workingSet = proc.WorkingSet64;
                }
            }
            catch
            {
                workingSet = 0;
            }

            return new MachineResourceSnapshot
            {
                OsDescription = RuntimeInformation.OSDescription.Trim(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                FrameworkDescription = RuntimeInformation.FrameworkDescription.Trim(),
                ProcessorCount = Environment.ProcessorCount,
                ManagedHeapBytes = GC.GetTotalMemory(false),
                WorkingSetBytes = workingSet,
                GcGen0Count = GC.CollectionCount(0),
                GcGen1Count = GC.CollectionCount(1),
                GcGen2Count = GC.CollectionCount(2)
            };
        }
    }
}

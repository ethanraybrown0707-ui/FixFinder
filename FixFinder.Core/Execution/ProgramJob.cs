using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FixFinder.Core.Execution;

/// <summary>
/// A Windows job holding one program FixFinder started - to run it, build it or serve it - which Windows ends, with whatever
/// that program started, when the job is closed. FixFinder closes it when it is done with the program; and when FixFinder ends,
/// however it ends, Windows closes every handle FixFinder held, so the program ends too. A run that times out, or that the
/// person stops, FixFinder ends itself; this is for FixFinder being closed from outside, or stopping with an error, in the
/// middle of a run, which left a program that never finishes running on its own for hours.
/// </summary>
/// <remarks>
/// One job for each program, not one for them all: Windows puts a program already in a job of its own - Python from the
/// Microsoft Store is - only in a new, empty job, which it places inside that one; one job already holding another program
/// it refused, with access denied. Nothing is done outside Windows, nor when Windows will not make the job or put the program
/// in it, when the program is left as it always was.
/// </remarks>
internal sealed class ProgramJob : IDisposable
{
    /// <summary>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: the job's processes are ended when the last handle to it is closed.</summary>
    private const uint KillOnJobClose = 0x2000;

    /// <summary>JobObjectExtendedLimitInformation, the kind of limits SetInformationJobObject is given.</summary>
    private const int ExtendedLimitInformation = 9;

    private IntPtr _handle;

    private ProgramJob(IntPtr handle) => _handle = handle;

    /// <summary>A new job holding the program, just started - or null when Windows would not make one, or put the program in it.</summary>
    public static ProgramJob? Hold(Process process)
    {
        if (!OperatingSystem.IsWindows()) return null;

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) return null;

        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
        try
        {
            if (SetInformationJobObject(handle, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) &&
                AssignProcessToJobObject(handle, process.Handle))
            {
                return new ProgramJob(handle);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The program had already ended, or its handle could not be had: there is nothing left to end.
        }

        CloseHandle(handle);
        return null;
    }

    /// <summary>Whether the program is in this job.</summary>
    public bool Holds(Process process) => _handle != IntPtr.Zero && IsProcessInJob(process.Handle, _handle, out var inJob) && inJob;

    /// <summary>Closes the job, which ends whatever is still in it.</summary>
    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;

        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    // JOBOBJECT_BASIC_LIMIT_INFORMATION, IO_COUNTERS and JOBOBJECT_EXTENDED_LIMIT_INFORMATION, as Windows lays them out.
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputOutputCounts
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public InputOutputCounts InputOutput;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimits information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, [MarshalAs(UnmanagedType.Bool)] out bool inJob);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

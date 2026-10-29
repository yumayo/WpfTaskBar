using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace WpfTaskBar;

internal sealed record SystemMetricsSample(
    DateTimeOffset Timestamp,
    double? CpuPercent,
    double? MemoryPercent,
    double? DiskPercent,
    double? SwapPercent,
    ulong? MemoryUsedBytes,
    ulong? MemoryTotalBytes);

internal readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User)
{
    public double? UsageSince(CpuTimes previous)
    {
        if (Idle < previous.Idle || Kernel < previous.Kernel || User < previous.User)
        {
            return null;
        }

        // GetSystemTimes の Kernel には Idle が含まれる。
        var total = (double)(Kernel - previous.Kernel) + (User - previous.User);
        var idle = Idle - previous.Idle;
        return total > 0 && idle <= total
            ? Math.Clamp((total - idle) / total * 100, 0, 100)
            : null;
    }
}

/// <summary>PC 全体の使用率を取得する。UI スレッドからは呼び出さない。</summary>
internal sealed class WindowsSystemMetricsReader : IDisposable
{
    private readonly PdhPercentageCounter _disk = new(@"\PhysicalDisk(_Total)\% Idle Time", invert: true);
    private readonly PdhPercentageCounter _swap = new(@"\Paging File(_Total)\% Usage");
    private CpuTimes? _previousCpu;

    public SystemMetricsSample Read()
    {
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var current = new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
            cpu = _previousCpu.HasValue ? current.UsageSince(_previousCpu.Value) : null;
            _previousCpu = current;
        }
        else
        {
            _previousCpu = null;
        }

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var hasMemory = GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0 &&
                        memory.AvailablePhysical <= memory.TotalPhysical;
        ulong? used = hasMemory ? memory.TotalPhysical - memory.AvailablePhysical : null;

        return new SystemMetricsSample(
            DateTimeOffset.UtcNow,
            cpu,
            hasMemory ? (double)used!.Value / memory.TotalPhysical * 100 : null,
            _disk.Read(),
            _swap.Read(),
            used,
            hasMemory ? memory.TotalPhysical : null);
    }

    public void Dispose()
    {
        _disk.Dispose();
        _swap.Dispose();
    }

    private static ulong ToUInt64(FILETIME time) =>
        ((ulong)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed class PdhPercentageCounter(string path, bool invert = false) : IDisposable
    {
        private SafePdhQueryHandle? _query;
        private IntPtr _counter;
        private long _retryAfter;
        private uint? _lastFailure;

        public double? Read()
        {
            if (_query == null)
            {
                if (Environment.TickCount64 < _retryAfter)
                {
                    return null;
                }

                var status = PdhOpenQuery(null, UIntPtr.Zero, out var query);
                _query = query;
                if (status == 0)
                {
                    // OS の表示言語に依存しないカウンター名を使用する。
                    status = PdhAddEnglishCounter(query, path, UIntPtr.Zero, out _counter);
                }

                if (status == 0)
                {
                    status = PdhCollectQueryData(query);
                }

                if (status != 0)
                {
                    RetryLater(status);
                }

                // 稼働率には 2 回のサンプルが必要。初回を 0% として表示しない。
                return null;
            }

            var collectStatus = PdhCollectQueryData(_query);
            if (collectStatus != 0)
            {
                RetryLater(collectStatus);
                return null;
            }

            var formatStatus = PdhGetFormattedCounterValue(_counter, 0x00000200, out _, out var value);
            if (formatStatus != 0 || value.Status > 1 || !double.IsFinite(value.Value))
            {
                // カウンター再構築やページファイル設定変更後にも再接続する。
                RetryLater(formatStatus != 0 ? formatStatus : value.Status);
                return null;
            }

            if (_lastFailure.HasValue)
            {
                Logger.Info($"System monitor counter recovered: {path}");
                _lastFailure = null;
            }

            return Math.Clamp(invert ? 100 - value.Value : value.Value, 0, 100);
        }

        private void RetryLater(uint status)
        {
            Dispose();
            _retryAfter = Environment.TickCount64 + 30_000;
            // ページファイル未構成など、継続中の同じエラーを毎回記録しない。
            if (_lastFailure != status)
            {
                Logger.Warning($"System monitor counter unavailable: {path}, status=0x{status:X8}; retrying in 30 seconds.");
                _lastFailure = status;
            }
        }

        public void Dispose()
        {
            _query?.Dispose();
            _query = null;
            _counter = IntPtr.Zero;
        }
    }

    private sealed class SafePdhQueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePdhQueryHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    // PDH_FMT_COUNTERVALUE は DWORD と 8 バイト境界の union からなる。
    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFormattedValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, UIntPtr userData, out SafePdhQueryHandle query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(SafePdhQueryHandle query, string path, UIntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(SafePdhQueryHandle query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFormattedValue value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}

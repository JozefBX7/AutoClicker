// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace AutoClicker;

internal static class OpenRgbServerDiagnostics
{
    private const int OpenRgbSdkPort = 6742;
    private const int AddressFamilyInterNetwork = 2;
    private const uint ErrorInsufficientBuffer = 122;

    internal static string? GetConflictMessage()
    {
        try { return GetConflictMessage(GetListeningProcessIds(OpenRgbSdkPort)); }
        catch (Exception exception)
        {
            AppLog.Error("Could not inspect OpenRGB SDK server ownership", exception);
            return null;
        }
    }

    internal static string? GetConflictMessage(IEnumerable<int> listeningProcessIds)
    {
        var owners = listeningProcessIds.Distinct().Order().ToArray();
        if (owners.Length < 2) return null;

        return $"Multiple OpenRGB SDK servers are listening on port {OpenRgbSdkPort} (processes {string.Join(" and ", owners)}). "
            + "Stop either the OpenRGB Windows service or the duplicate desktop server, then restart or rescan the remaining OpenRGB instance.";
    }

    private static int[] GetListeningProcessIds(int port)
    {
        if (!OperatingSystem.IsWindows()) return [];

        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, order: false, AddressFamilyInterNetwork, TcpTableClass.OwnerPidListener, 0);
        if (result != ErrorInsufficientBuffer) throw new Win32Exception((int)result);

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = GetExtendedTcpTable(buffer, ref size, order: false, AddressFamilyInterNetwork, TcpTableClass.OwnerPidListener, 0);
            if (result != 0) throw new Win32Exception((int)result);

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            var rowAddress = IntPtr.Add(buffer, sizeof(int));
            var owners = new List<int>();
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(rowAddress);
                var localPort = unchecked((ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort));
                if (row.State == TcpState.Listen && localPort == port)
                    owners.Add(unchecked((int)row.OwningProcessId));
                rowAddress = IntPtr.Add(rowAddress, rowSize);
            }
            return owners.ToArray();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        bool order,
        int addressFamily,
        TcpTableClass tableClass,
        uint reserved);

    private enum TcpTableClass
    {
        OwnerPidListener = 3
    }

    private enum TcpState : uint
    {
        Listen = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        internal TcpState State;
        internal uint LocalAddress;
        internal uint LocalPort;
        internal uint RemoteAddress;
        internal uint RemotePort;
        internal uint OwningProcessId;
    }
}

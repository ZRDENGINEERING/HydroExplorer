using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;


public static class FileLockDetector
{
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, uint dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint dwSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint dwSessionHandle, uint nFiles, string[] rgsFileNames,
        uint nApplications, IntPtr rgApplications, uint nServices, IntPtr rgServices);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
        ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);

    private const int RmBlErrorOverflow = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    public static List<Process> GetProcessesLockingFile(string filePath)
    {
        var processes = new List<Process>();
        string sessionKey = Guid.NewGuid().ToString();

        if (RmStartSession(out uint sessionHandle, 0, sessionKey) != 0)
            return processes;

        try
        {
            string[] resources = { filePath };
            if (RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, IntPtr.Zero, 0, IntPtr.Zero) != 0)
                return processes;

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint rebootReasons = 0;

            // First call fetches the required size
            int res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, ref rebootReasons);
            if (res == RmBlErrorOverflow)
            {
                var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;

                // Second call retrieves the actual process list
                if (RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref rebootReasons) == 0)
                {
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        try
                        {
                            processes.Add(Process.GetProcessById(processInfo[i].Process.dwProcessId));
                        }
                        catch (ArgumentException)
                        {
                            // Process might have exited closed in the meantime
                        }
                    }
                }
            }
        }
        finally
        {
            RmEndSession(sessionHandle);
        }

        return processes;
    }
}

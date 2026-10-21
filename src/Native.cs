using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace CoreAllocator
{
    internal sealed class NativeOperationException : Win32Exception
    {
        internal readonly string Operation;
        internal NativeOperationException(int code, string operation) : base(code) { Operation = operation; }
    }

    internal sealed class ProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ProcessHandle() : base(true) { }
        protected override bool ReleaseHandle() { return Native.CloseHandle(handle); }
    }

    internal static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetLogicalProcessorInformationEx(int relation, IntPtr buffer, ref uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ProcessHandle OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(ProcessHandle handle, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessAffinityMask(ProcessHandle handle, out UIntPtr process, out UIntPtr system);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessAffinityMask(ProcessHandle handle, UIntPtr mask);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(ProcessHandle handle, uint milliseconds);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(ProcessHandle process, uint access, out IntPtr token);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
        [DllImport("ntdll.dll")]
        private static extern int NtQueryObject(ProcessHandle handle, int informationClass, IntPtr information, uint length, out uint returned);

        internal static bool IsAdministrator
        {
            get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        }

        internal static ProcessHandle Open(int pid, bool write)
        {
            ProcessHandle handle = OpenProcess(0x1000U | 0x100000U | (write ? 0x0200U : 0U), false, pid);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new NativeOperationException(error, write ? "打开进程并申请修改权限" : "以只读方式打开进程");
            }
            return handle;
        }

        internal static long Created(ProcessHandle handle)
        {
            long created, exited, kernel, user;
            if (!GetProcessTimes(handle, out created, out exited, out kernel, out user)) throw Error("读取进程启动时间");
            return created;
        }

        internal static bool IsExited(ProcessHandle handle)
        {
            uint result = WaitForSingleObject(handle, 0);
            if (result == 0xffffffff) throw Error("检查进程是否退出");
            return result == 0;
        }

        internal static ulong Affinity(ProcessHandle handle, out ulong system)
        {
            UIntPtr processMask, systemMask;
            if (!GetProcessAffinityMask(handle, out processMask, out systemMask)) throw Error("读取亲和性");
            system = systemMask.ToUInt64();
            if (processMask == UIntPtr.Zero || systemMask == UIntPtr.Zero)
                throw new InvalidOperationException("Windows 返回空亲和性；可能涉及多个处理器组，本版不修改此进程。");
            return processMask.ToUInt64();
        }

        internal static void SetAffinity(ProcessHandle handle, ulong mask)
        {
            EnsureCanSetAffinity(handle);
            if (!SetProcessAffinityMask(handle, new UIntPtr(mask))) throw Error("写入亲和性");
        }

        // Diagnostic only: an OpenProcess success does not guarantee every requested right survived filtering.
        // This optional native query never enables privileges or changes the target's security descriptor.
        internal static uint? GrantedAccess(ProcessHandle handle)
        {
            IntPtr buffer = Marshal.AllocHGlobal(56);
            try
            {
                uint returned;
                int status = NtQueryObject(handle, 0, buffer, 56, out returned);
                return status == 0 ? (uint?)unchecked((uint)Marshal.ReadInt32(buffer, 4)) : null;
            }
            catch (EntryPointNotFoundException) { return null; }
            catch (DllNotFoundException) { return null; }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        internal static bool HasAffinityWriteAccess(uint granted) { return (granted & 0x0200) != 0; }

        internal static void EnsureCanSetAffinity(ProcessHandle handle)
        {
            uint? granted = GrantedAccess(handle);
            if (granted.HasValue && !HasAffinityWriteAccess(granted.Value))
                throw new InvalidOperationException("权限检查：系统返回的句柄缺少修改亲和性权限（PROCESS_SET_INFORMATION）；请求的写权限被过滤，未执行写入。" +
                    " 实际权限 0x" + granted.Value.ToString("X8") + "，工具" + (IsAdministrator ? "已是管理员。" : "当前为普通权限。") +
                    " 这类限制不能仅靠重复点击解决。");
        }

        internal static string Owner(ProcessHandle handle)
        {
            IntPtr token;
            if (!OpenProcessToken(handle, 0x0008, out token)) throw Error("核对进程所有者");
            try { using (WindowsIdentity identity = new WindowsIdentity(token)) { return identity.User.Value; } }
            finally { CloseHandle(token); }
        }

        internal static void ValidateTarget(ProcessHandle handle, int pid, long created)
        {
            if (pid <= 4 || pid == Process.GetCurrentProcess().Id) throw new InvalidOperationException("不修改系统进程或工具自身。");
            if (IsExited(handle) || Created(handle) != created) throw new InvalidOperationException("进程已退出或 PID 已被其他进程复用，请刷新列表。");
            uint session;
            if (!ProcessIdToSessionId((uint)pid, out session)) throw LastError();
            if (session != (uint)Process.GetCurrentProcess().SessionId || Owner(handle) != WindowsIdentity.GetCurrent().User.Value)
                throw new InvalidOperationException("第一版只操作当前登录用户、当前会话的进程。");
        }

        internal static Win32Exception LastError() { return new Win32Exception(Marshal.GetLastWin32Error()); }
        private static NativeOperationException Error(string operation) { return new NativeOperationException(Marshal.GetLastWin32Error(), operation); }

        internal static string Explain(Exception exception)
        {
            Win32Exception error = exception as Win32Exception;
            if (error != null)
            {
                string hint = error.NativeErrorCode == 5 ? (IsAdministrator ?
                    "访问被拒绝：工具已是管理员，目标访问策略仍不允许这项操作。可能涉及进程保护或安全软件；不能据此确定具体来源。" :
                    "访问被拒绝：工具当前是普通权限，可用桌面的管理员快捷方式重新打开；目标保护仍可能限制操作。") :
                    error.NativeErrorCode == 87 ? "参数无效：进程可能已退出、核心不可用，或目标受 Job/处理器组限制。" : error.Message;
                NativeOperationException operationError = error as NativeOperationException;
                return (operationError == null ? "" : operationError.Operation + "：") + hint + "（Win32 " + error.NativeErrorCode + "）";
            }
            return exception.Message;
        }
    }
}

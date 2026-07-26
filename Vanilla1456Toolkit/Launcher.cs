using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Launcher
{
	private const uint ProcessCreateThread = 0x0002;
	private const uint ProcessQueryInformation = 0x0400;
	private const uint ProcessVmOperation = 0x0008;
	private const uint ProcessVmWrite = 0x0020;
	private const uint ProcessVmRead = 0x0010;
	private const uint MemCommit = 0x1000;
	private const uint MemReserve = 0x2000;
	private const uint MemRelease = 0x8000;
	private const uint PageReadWrite = 0x04;
	private const uint WaitObject0 = 0;
	private const uint WaitTimeout = 0x00000102;
	private const string NativeModuleName = "Terraria1456Toolkit.Bootstrap.dll";

	private sealed class Options
	{
		public int ProcessId;
		public bool HasProcessId;
		public bool Silent;
		public string LogPath;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint allocationType, uint protect);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, uint size, uint freeType);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, uint size, out UIntPtr written);

	[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr GetModuleHandle(string name);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, uint stackSize, IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(IntPtr handle);

	[STAThread]
	private static int Main(string[] args)
	{
		Options options = ParseOptions(args);
		try {
			string directory = AppDomain.CurrentDomain.BaseDirectory;
			if (string.IsNullOrWhiteSpace(options.LogPath))
				options.LogPath = Path.Combine(directory, "vanilla_injector.log");

			Log(options, "launcher-start");
			string bootstrap = Path.Combine(directory, NativeModuleName);
			string managed = Path.Combine(directory, "Terraria1456Toolkit.Managed.dll");
			if (!File.Exists(bootstrap) || !File.Exists(managed))
				return Fail(options, 20, "修改器组件不完整，请把三个文件放在同一目录。", null);

			Process process = FindTerraria(options);
			if (process == null)
				return Fail(options, 10, "没有找到正在运行的原版 Terraria.exe。", null);

			Log(options, "target pid=" + process.Id);
			FileVersionInfo version = process.MainModule.FileVersionInfo;
			if (version.FileVersion != "1.4.5.6") {
				string text = "检测到 Terraria 版本 " + version.FileVersion + "，本后端按 1.4.5.6 构建。";
				if (options.Silent)
					return Fail(options, 21, text, null);

				DialogResult answer = MessageBox.Show(
					text + "\n是否仍然尝试加载？",
					"版本提示",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Question);
				if (answer != DialogResult.Yes)
					return 21;
			}

			using (Mutex mutex = new Mutex(false, @"Local\HoamTerrariaToolkit.Vanilla.Inject." + process.Id)) {
				bool acquired = false;
				try {
					try {
						acquired = mutex.WaitOne(0, false);
					}
					catch (AbandonedMutexException) {
						acquired = true;
					}
					if (!acquired)
						return Fail(options, 12, "另一个修改器实例正在加载到这个 Terraria 进程。", null);

					if (IsModuleLoaded(process, NativeModuleName))
						return Fail(
							options,
							11,
							"修改器已经注入这个 Terraria 进程。若界面没有出现，请查看日志并重启游戏后再试；不会进行二次注入。",
							null);

					string statusPath = Path.Combine(directory, "bootstrap_status.txt");
					string backendErrorPath = Path.Combine(directory, "vanilla_backend_error.txt");
					DeleteRequired(statusPath);
					DeleteRequired(backendErrorPath);

					Inject(process, bootstrap);
					string status = WaitForStatus(process, statusPath, backendErrorPath, 15000);
					if (!string.Equals(status, "00000000", StringComparison.OrdinalIgnoreCase)) {
						string detail = ReadTextSafely(backendErrorPath);
						return Fail(
							options,
							31,
							"原版后端启动失败，状态：" + status,
							detail);
					}

					Log(options, "inject-success status=" + status);
					return 0;
				}
				finally {
					if (acquired) {
						try { mutex.ReleaseMutex(); }
						catch { }
					}
				}
			}
		}
		catch (Exception ex) {
			return Fail(options, 30, ex.Message, ex.ToString());
		}
	}

	private static Options ParseOptions(string[] args)
	{
		Options options = new Options();
		for (int i = 0; args != null && i < args.Length; i++) {
			string value = args[i];
			if (string.Equals(value, "--silent", StringComparison.OrdinalIgnoreCase)) {
				options.Silent = true;
			}
			else if (string.Equals(value, "--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
				int pid;
				if (int.TryParse(args[++i], out pid) && pid > 0) {
					options.ProcessId = pid;
					options.HasProcessId = true;
				}
			}
			else if (string.Equals(value, "--log", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
				options.LogPath = Path.GetFullPath(args[++i]);
			}
		}
		return options;
	}

	private static Process FindTerraria(Options options)
	{
		if (options.HasProcessId) {
			try {
				Process specified = Process.GetProcessById(options.ProcessId);
				if (!specified.HasExited &&
					string.Equals(specified.ProcessName, "Terraria", StringComparison.OrdinalIgnoreCase))
					return specified;
			}
			catch {
			}
			return null;
		}

		return Process.GetProcessesByName("Terraria")
			.Where(delegate(Process process) {
				try { return !process.HasExited && process.MainModule != null; }
				catch { return false; }
			})
			.FirstOrDefault();
	}

	private static bool IsModuleLoaded(Process target, string moduleName)
	{
		target.Refresh();
		if (target.HasExited)
			throw new InvalidOperationException("Terraria 已经退出。");

		foreach (ProcessModule module in target.Modules) {
			try {
				if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(Path.GetFileName(module.FileName), moduleName, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			catch {
			}
		}
		return false;
	}

	private static void Inject(Process target, string dllPath)
	{
		uint access = ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation | ProcessVmWrite | ProcessVmRead;
		IntPtr process = OpenProcess(access, false, target.Id);
		if (process == IntPtr.Zero)
			throw new InvalidOperationException("无法打开 Terraria 进程，错误码：" + Marshal.GetLastWin32Error());

		IntPtr remotePath = IntPtr.Zero;
		bool safeToFreeRemotePath = true;
		try {
			byte[] pathBytes = Encoding.Unicode.GetBytes(Path.GetFullPath(dllPath) + "\0");
			remotePath = VirtualAllocEx(process, IntPtr.Zero, (uint)pathBytes.Length, MemCommit | MemReserve, PageReadWrite);
			if (remotePath == IntPtr.Zero)
				throw new InvalidOperationException("无法分配远程内存，错误码：" + Marshal.GetLastWin32Error());

			UIntPtr written;
			if (!WriteProcessMemory(process, remotePath, pathBytes, (uint)pathBytes.Length, out written) ||
				written.ToUInt64() != (ulong)pathBytes.Length)
				throw new InvalidOperationException("无法写入远程内存，错误码：" + Marshal.GetLastWin32Error());

			IntPtr remoteLoadLibrary = ResolveRemoteLoadLibrary(target);
			uint threadId;
			IntPtr thread = CreateRemoteThread(process, IntPtr.Zero, 0, remoteLoadLibrary, remotePath, 0, out threadId);
			if (thread == IntPtr.Zero)
				throw new InvalidOperationException("无法建立加载线程，错误码：" + Marshal.GetLastWin32Error());

			try {
				uint wait = WaitForSingleObject(thread, 10000);
				if (wait == WaitTimeout) {
					safeToFreeRemotePath = false;
					throw new TimeoutException("加载修改器超时；为保护游戏，不会再次注入。");
				}
				if (wait != WaitObject0) {
					safeToFreeRemotePath = false;
					throw new InvalidOperationException("等待加载线程失败，错误码：" + Marshal.GetLastWin32Error());
				}

				uint module;
				if (!GetExitCodeThread(thread, out module) || module == 0)
					throw new InvalidOperationException("Terraria 拒绝加载修改器组件。");
			}
			finally {
				CloseHandle(thread);
			}
		}
		finally {
			if (remotePath != IntPtr.Zero && safeToFreeRemotePath)
				VirtualFreeEx(process, remotePath, 0, MemRelease);
			CloseHandle(process);
		}
	}

	private static IntPtr ResolveRemoteLoadLibrary(Process target)
	{
		IntPtr localKernel32 = GetModuleHandle("kernel32.dll");
		IntPtr localLoadLibrary = localKernel32 == IntPtr.Zero
			? IntPtr.Zero
			: GetProcAddress(localKernel32, "LoadLibraryW");
		if (localKernel32 == IntPtr.Zero || localLoadLibrary == IntPtr.Zero)
			throw new InvalidOperationException("无法解析本地 LoadLibraryW。");

		long functionAddress = localLoadLibrary.ToInt64();
		ProcessModule localOwner = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
			.FirstOrDefault(delegate(ProcessModule module) {
				try {
					long start = module.BaseAddress.ToInt64();
					long end = start + module.ModuleMemorySize;
					return functionAddress >= start && functionAddress < end;
				}
				catch { return false; }
			});
		if (localOwner == null)
			throw new InvalidOperationException("无法确定 LoadLibraryW 所属的系统模块。");

		ProcessModule remoteOwner = target.Modules.Cast<ProcessModule>()
			.FirstOrDefault(delegate(ProcessModule module) {
				try { return string.Equals(module.ModuleName, localOwner.ModuleName, StringComparison.OrdinalIgnoreCase); }
				catch { return false; }
			});
		if (remoteOwner == null)
			throw new InvalidOperationException("目标进程中没有找到 " + localOwner.ModuleName + "。");

		long relative = functionAddress - localOwner.BaseAddress.ToInt64();
		return new IntPtr(remoteOwner.BaseAddress.ToInt64() + relative);
	}

	private static string WaitForStatus(Process target, string statusPath, string backendErrorPath, int timeoutMilliseconds)
	{
		Stopwatch timer = Stopwatch.StartNew();
		while (timer.ElapsedMilliseconds < timeoutMilliseconds) {
			if (File.Exists(statusPath)) {
				string status = ReadTextSafely(statusPath).Trim();
				if (status.Length > 0)
					return status;
			}
			if (File.Exists(backendErrorPath)) {
				string error = ReadTextSafely(backendErrorPath);
				if (error.Length > 0)
					throw new InvalidOperationException("原版后端初始化失败：" + FirstLine(error));
			}
			target.Refresh();
			if (target.HasExited)
				throw new InvalidOperationException("Terraria 在后端初始化期间退出。");
			Thread.Sleep(50);
		}
		throw new TimeoutException("原版组件已加载，但后端在 15 秒内没有报告状态。请查看日志并重启游戏后再试。");
	}

	private static int Fail(Options options, int code, string message, string detail)
	{
		Log(options, "failure code=" + code + " message=" + message +
			(string.IsNullOrWhiteSpace(detail) ? string.Empty : Environment.NewLine + detail));
		if (!options.Silent) {
			MessageBox.Show(
				message,
				"加载失败",
				MessageBoxButtons.OK,
				code == 11 || code == 12 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
		}
		return code;
	}

	private static void Log(Options options, string message)
	{
		try {
			string path = options.LogPath;
			if (string.IsNullOrWhiteSpace(path))
				path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vanilla_injector.log");
			string parent = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(parent))
				Directory.CreateDirectory(parent);
			File.AppendAllText(
				path,
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
				" [pid " + Process.GetCurrentProcess().Id + "] " + message + Environment.NewLine,
				Encoding.UTF8);
		}
		catch {
		}
	}

	private static void DeleteRequired(string path)
	{
		try {
			if (File.Exists(path))
				File.Delete(path);
		}
		catch (Exception ex) {
			throw new IOException("无法清理旧状态文件：" + path, ex);
		}
		if (File.Exists(path))
			throw new IOException("旧状态文件仍然存在，已取消注入以避免误报成功：" + path);
	}

	private static string ReadTextSafely(string path)
	{
		try {
			return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
		}
		catch {
			return string.Empty;
		}
	}

	private static string FirstLine(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;
		int newline = value.IndexOfAny(new[] { '\r', '\n' });
		return newline < 0 ? value : value.Substring(0, newline);
	}
}

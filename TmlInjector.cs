using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class TmlInjector
{
	private const uint FullAccess = 0x0002 | 0x0400 | 0x0008 | 0x0020 | 0x0010;
	private const uint MemCommitReserve = 0x1000 | 0x2000;
	private const uint MemRelease = 0x8000;
	private const uint PageReadWrite = 0x04;
	private const uint WaitObject0 = 0;
	private const uint WaitTimeout = 0x00000102;
	private const string NativeModuleName = "TerrariaTmlToolkit.Native.dll";

	private sealed class Options
	{
		public int ProcessId;
		public bool HasProcessId;
		public bool Silent;
		public string LogPath;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
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
	private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, uint stackSize, IntPtr start, IntPtr parameter, uint flags, out uint threadId);
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
				options.LogPath = Path.Combine(directory, "tml_injector.log");

			Log(options, "launcher-start");
			string native = Path.Combine(directory, NativeModuleName);
			string bootstrap = Path.Combine(directory, "TerrariaTmlToolkit.Bootstrap.dll");
			string ui = Path.Combine(directory, "TerrariaTmlToolkit.UI.dll");
			string config = Path.Combine(directory, "TerrariaTmlToolkit.runtimeconfig.json");
			if (!File.Exists(native) || !File.Exists(bootstrap) || !File.Exists(ui) || !File.Exists(config))
				return Fail(options, 20, "TML 修改器组件不完整。", null);

			Process target = FindTml(options);
			if (target == null)
				return Fail(options, 10, "没有找到指定的 tModLoader 进程。", null);

			Log(options, "target pid=" + target.Id);
			using (Mutex mutex = new Mutex(false, @"Local\HoamTerrariaToolkit.Tml.Inject." + target.Id)) {
				bool acquired = false;
				try {
					try {
						acquired = mutex.WaitOne(0, false);
					}
					catch (AbandonedMutexException) {
						acquired = true;
					}
					if (!acquired)
						return Fail(options, 12, "另一个修改器实例正在加载到这个 tModLoader 进程。", null);

					if (IsModuleLoaded(target, NativeModuleName))
						return Fail(
							options,
							11,
							"TML 修改器已经注入这个进程。若界面没有出现，请查看日志并重启 tModLoader；不会进行二次注入。",
							null);

					string statusPath = Path.Combine(directory, "tml_bootstrap_status.txt");
					string backendErrorPath = Path.Combine(directory, "tml_backend_error.txt");
					string uiErrorPath = Path.Combine(directory, "tml_ui_error.txt");
					DeleteRequired(statusPath);
					DeleteRequired(backendErrorPath);
					DeleteRequired(uiErrorPath);

					Inject(target, native);
					string status = WaitForStatus(target, statusPath, backendErrorPath, uiErrorPath, 20000);
					if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) {
						string detail = ReadTextSafely(backendErrorPath);
						if (string.IsNullOrWhiteSpace(detail))
							detail = ReadTextSafely(uiErrorPath);
						return Fail(options, 31, "TML 后端启动失败，状态：" + status, detail);
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

	private static Process FindTml(Options options)
	{
		if (options.HasProcessId) {
			try {
				Process specified = Process.GetProcessById(options.ProcessId);
				if (IsTmlProcess(specified) &&
					!IsLikelyTmlServer(specified))
					return specified;
			}
			catch {
			}
			return null;
		}

		return Process.GetProcesses()
			.Where(delegate(Process process) {
				try {
					return string.Equals(process.ProcessName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
						process.ProcessName.IndexOf(
							"tModLoader", StringComparison.OrdinalIgnoreCase) >= 0;
				}
				catch { return false; }
			})
			.Where(delegate(Process process) {
				try { return IsTmlProcess(process); }
				catch { return false; }
			})
			.Where(delegate(Process process) {
				return !IsLikelyTmlServer(process);
			})
			.OrderByDescending(ScoreTmlClient)
			.ThenByDescending(SafeStartTimeTicks)
			.FirstOrDefault();
	}

	private static bool IsTmlProcess(Process process)
	{
		if (process == null)
			return false;
		try { if (process.HasExited) return false; }
		catch { return false; }
		try {
			foreach (ProcessModule module in process.Modules) {
				try {
					string name = Path.GetFileName(module.FileName);
					if (string.Equals(name, "tModLoader.dll",
						StringComparison.OrdinalIgnoreCase) ||
						string.Equals(name, "tModLoader.exe",
						StringComparison.OrdinalIgnoreCase))
						return true;
				}
				catch { }
			}
		}
		catch { }
		return SafeWindowTitle(process).IndexOf(
			"tModLoader", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsLikelyTmlServer(Process process)
	{
		string name;
		try { name = process.ProcessName; }
		catch { name = string.Empty; }
		string title = SafeWindowTitle(process);
		if (name.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0 ||
			title.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0 ||
			title.IndexOf("dedicated", StringComparison.OrdinalIgnoreCase) >= 0)
			return true;
		try {
			foreach (ProcessModule module in process.Modules) {
				string moduleName;
				try { moduleName = Path.GetFileName(module.FileName); }
				catch { continue; }
				if (moduleName.IndexOf(
					"tModLoaderServer", StringComparison.OrdinalIgnoreCase) >= 0)
					return true;
			}
		}
		catch { }
		return false;
	}

	private static int ScoreTmlClient(Process process)
	{
		int score = 0;
		try {
			if (process.MainWindowHandle != IntPtr.Zero)
				score += 100000;
		}
		catch { }
		string title = SafeWindowTitle(process);
		if (title.Length > 0)
			score += 10000;
		if (title.IndexOf("tModLoader", StringComparison.OrdinalIgnoreCase) >= 0)
			score += 5000;
		if (title.IndexOf("Terraria", StringComparison.OrdinalIgnoreCase) >= 0)
			score += 2500;
		return score;
	}

	private static long SafeStartTimeTicks(Process process)
	{
		try { return process.StartTime.Ticks; }
		catch { return 0L; }
	}

	private static string SafeWindowTitle(Process process)
	{
		try { return process.MainWindowTitle ?? string.Empty; }
		catch { return string.Empty; }
	}

	private static bool IsModuleLoaded(Process target, string moduleName)
	{
		target.Refresh();
		if (target.HasExited)
			throw new InvalidOperationException("tModLoader 已经退出。");

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
		IntPtr process = OpenProcess(FullAccess, false, target.Id);
		if (process == IntPtr.Zero)
			throw new InvalidOperationException("无法打开 TML 进程，错误码：" + Marshal.GetLastWin32Error());

		IntPtr remotePath = IntPtr.Zero;
		bool safeToFreeRemotePath = true;
		try {
			byte[] bytes = Encoding.Unicode.GetBytes(Path.GetFullPath(dllPath) + "\0");
			remotePath = VirtualAllocEx(process, IntPtr.Zero, (uint)bytes.Length, MemCommitReserve, PageReadWrite);
			if (remotePath == IntPtr.Zero)
				throw new InvalidOperationException("无法分配远程内存，错误码：" + Marshal.GetLastWin32Error());

			UIntPtr written;
			if (!WriteProcessMemory(process, remotePath, bytes, (uint)bytes.Length, out written) ||
				written.ToUInt64() != (ulong)bytes.Length)
				throw new InvalidOperationException("无法写入 TML 进程，错误码：" + Marshal.GetLastWin32Error());

			IntPtr remoteLoadLibrary = ResolveRemoteLoadLibrary(target);
			uint threadId;
			IntPtr thread = CreateRemoteThread(process, IntPtr.Zero, 0, remoteLoadLibrary, remotePath, 0, out threadId);
			if (thread == IntPtr.Zero)
				throw new InvalidOperationException("无法建立 TML 加载线程，错误码：" + Marshal.GetLastWin32Error());

			try {
				uint wait = WaitForSingleObject(thread, 10000);
				if (wait == WaitTimeout) {
					safeToFreeRemotePath = false;
					throw new TimeoutException("TML 加载超时；为保护游戏，不会再次注入。");
				}
				if (wait != WaitObject0) {
					safeToFreeRemotePath = false;
					throw new InvalidOperationException("等待 TML 加载线程失败，错误码：" + Marshal.GetLastWin32Error());
				}

				uint module;
				if (!GetExitCodeThread(thread, out module) || module == 0)
					throw new InvalidOperationException("TML 拒绝加载修改器组件。");
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

	private static string WaitForStatus(
		Process target,
		string statusPath,
		string backendErrorPath,
		string uiErrorPath,
		int timeoutMilliseconds)
	{
		Stopwatch timer = Stopwatch.StartNew();
		while (timer.ElapsedMilliseconds < timeoutMilliseconds) {
			if (File.Exists(statusPath)) {
				string status = ReadStatus(statusPath).Trim().Trim('\0');
				if (status.Length > 0)
					return status;
			}
			if (File.Exists(backendErrorPath)) {
				string error = ReadTextSafely(backendErrorPath);
				if (error.Length > 0)
					throw new InvalidOperationException("TML 后端初始化失败：" + Shorten(error, 1600));
			}
			if (File.Exists(uiErrorPath)) {
				string error = ReadTextSafely(uiErrorPath);
				if (error.Length > 0)
					throw new InvalidOperationException("TML 界面初始化失败：" + Shorten(error, 1600));
			}
			target.Refresh();
			if (target.HasExited)
				throw new InvalidOperationException("tModLoader 在后端初始化期间退出。");
			Thread.Sleep(50);
		}
		throw new TimeoutException("TML 组件已加载，但后端在 20 秒内没有报告状态。请查看日志并重启 tModLoader 后再试。");
	}

	private static string ReadStatus(string path)
	{
		try {
			byte[] bytes = File.ReadAllBytes(path);
			if (bytes.Length >= 2 && bytes[1] == 0)
				return Encoding.Unicode.GetString(bytes);
			return Encoding.UTF8.GetString(bytes);
		}
		catch {
			return string.Empty;
		}
	}

	private static int Fail(Options options, int code, string message, string detail)
	{
		Log(options, "failure code=" + code + " message=" + message +
			(string.IsNullOrWhiteSpace(detail) ? string.Empty : Environment.NewLine + detail));
		if (!options.Silent) {
			MessageBox.Show(
				message,
				"TML 加载失败",
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
				path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tml_injector.log");
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

	private static string Shorten(string value, int maximum)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;
		value = value.Trim();
		return value.Length <= maximum ? value : value.Substring(0, maximum);
	}
}

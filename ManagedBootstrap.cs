using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace TerrariaTmlToolkit;

public static class EntryPoint
{
	private static readonly object Gate = new object();
	private static bool _resolverInstalled;
	private static string _baseDirectory = string.Empty;
	private static string _desktopDirectory = string.Empty;

	[UnmanagedCallersOnly]
	public static int Start(IntPtr args, int size)
	{
		lock (Gate) {
			try {
				_baseDirectory =
					Path.GetDirectoryName(typeof(EntryPoint).Assembly.Location) ??
					string.Empty;
				if (!_resolverInstalled) {
					_desktopDirectory = FindDesktopRuntime();
					AssemblyLoadContext.Default.Resolving += ResolveAssembly;
					_resolverInstalled = true;
				}

				Assembly ui = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(delegate(Assembly assembly) {
						return string.Equals(assembly.GetName().Name,
							"TerrariaTmlToolkit.UI",
							StringComparison.OrdinalIgnoreCase);
					});
				if (ui == null) {
					ui = AssemblyLoadContext.Default.LoadFromAssemblyPath(
						Path.Combine(_baseDirectory,
							"TerrariaTmlToolkit.UI.dll"));
				}

				Type entryType = ui.GetType(
					"TerrariaTmlToolkit.UiEntry", throwOnError: true);
				MethodInfo startMethod = entryType.GetMethod(
					"Start", BindingFlags.Public | BindingFlags.Static);
				if (startMethod == null)
					throw new MissingMethodException(entryType.FullName, "Start");

				object value = startMethod.Invoke(null, null);
				int result = value is int ? (int)value : 0;
				if (result == 0)
					AppDomain.CurrentDomain.SetData(
						"TerrariaTmlToolkit.Loaded", true);
				return result;
			}
			catch (Exception ex) {
				Exception actual =
					ex is TargetInvocationException && ex.InnerException != null
						? ex.InnerException
						: ex;
				WriteError(actual);
				return -1;
			}
		}
	}

	private static Assembly ResolveAssembly(
		AssemblyLoadContext context, AssemblyName name)
	{
		string local = Path.Combine(_baseDirectory, name.Name + ".dll");
		if (File.Exists(local))
			return context.LoadFromAssemblyPath(local);
		string desktop = Path.Combine(_desktopDirectory, name.Name + ".dll");
		if (File.Exists(desktop))
			return context.LoadFromAssemblyPath(desktop);
		return null;
	}

	private static void WriteError(Exception exception)
	{
		try {
			File.AppendAllText(
				Path.Combine(_baseDirectory, "tml_backend_error.txt"),
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
				Environment.NewLine + exception + Environment.NewLine +
				Environment.NewLine);
		}
		catch {
		}
	}

	private static string FindDesktopRuntime()
	{
		string root = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			"dotnet", "shared", "Microsoft.WindowsDesktop.App");
		return Directory.GetDirectories(root)
			.Select(path => new {
				Path = path,
				Version = ParseVersion(Path.GetFileName(path))
			})
			.Where(x => x.Version.Major == 8)
			.OrderByDescending(x => x.Version)
			.First().Path;
	}

	private static Version ParseVersion(string value)
	{
		Version result;
		return Version.TryParse(value, out result)
			? result
			: new Version();
	}
}

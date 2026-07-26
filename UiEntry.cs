using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace TerrariaTmlToolkit;

public static class UiEntry
{
	private static readonly object Gate = new object();
	private static Thread _uiThread;
	private static TmlTrainerForm _form;
	private static ManualResetEventSlim _startupSignal;
	private static Exception _startupError;
	private static bool _applicationConfigured;

	/// <summary>
	/// Starts the UI and waits until its Form constructor has completed. This
	/// keeps constructor/layout failures from escaping a background thread and
	/// terminating the whole tModLoader process.
	/// </summary>
	public static int Start()
	{
		ManualResetEventSlim signal;
		TmlTrainerForm existing;

		lock (Gate) {
			existing = _form;
			if (existing != null && !existing.IsDisposed) {
				ShowExisting(existing);
				return 0;
			}

			if (_uiThread == null || !_uiThread.IsAlive) {
				_startupError = null;
				_startupSignal = new ManualResetEventSlim(false);
				signal = _startupSignal;
				_uiThread = new Thread(new ThreadStart(
					delegate { RunUiThread(signal); }));
				_uiThread.Name = "Terraria TML Toolkit UI";
				_uiThread.IsBackground = true;
				_uiThread.SetApartmentState(ApartmentState.STA);
				_uiThread.Start();
			}
			else {
				signal = _startupSignal;
			}
		}

		if (signal == null || !signal.Wait(TimeSpan.FromSeconds(15))) {
			LogError(new TimeoutException("TML trainer UI startup timed out."));
			return -2;
		}

		lock (Gate) {
			if (_form != null && !_form.IsDisposed)
				return 0;
			if (_startupError != null)
				LogError(_startupError);
		}
		return -1;
	}

	private static void RunUiThread(ManualResetEventSlim startupSignal)
	{
		TmlTrainerForm created = null;
		bool shown = false;
		try {
			if (!_applicationConfigured) {
				Application.SetUnhandledExceptionMode(
					UnhandledExceptionMode.CatchException);
				Application.ThreadException += delegate(object sender,
					ThreadExceptionEventArgs args) {
					LogError(args.Exception);
				};
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				_applicationConfigured = true;
			}

			created = new TmlTrainerForm();
			created.Shown += delegate {
				lock (Gate) {
					shown = true;
					_form = created;
				}
				startupSignal.Set();
			};
			Application.Run(created);
			if (!shown) {
				throw new InvalidOperationException(
					"TML trainer message loop ended before the form was shown.");
			}
		}
		catch (Exception ex) {
			lock (Gate) {
				_startupError = ex;
			}
			LogError(ex);
		}
		finally {
			startupSignal.Set();
			lock (Gate) {
				if (created == null || ReferenceEquals(_form, created))
					_form = null;
				_uiThread = null;
			}
		}
	}

	private static void ShowExisting(TmlTrainerForm form)
	{
		try {
			form.BeginInvoke(new Action(delegate {
				if (form.IsDisposed)
					return;
				form.Show();
				form.WindowState = FormWindowState.Normal;
				form.Activate();
			}));
		}
		catch (Exception ex) {
			LogError(ex);
		}
	}

	private static void LogError(Exception exception)
	{
		try {
			string directory = Path.GetDirectoryName(
				typeof(UiEntry).Assembly.Location);
			string path = Path.Combine(directory ?? string.Empty,
				"tml_ui_error.txt");
			File.AppendAllText(path,
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
				Environment.NewLine + exception + Environment.NewLine +
				Environment.NewLine);
		}
		catch {
		}
	}
}

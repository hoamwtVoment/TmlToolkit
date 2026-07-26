using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Terraria1456Toolkit
{
	public static class EntryPoint
	{
		private static readonly object SyncRoot = new object();
		private static TrainerForm _form;
		private static bool _starting;
		private static bool _winFormsConfigured;
		private static ManualResetEvent _startupSignal;
		private static int _startupResult;

		public static int Start(string argument)
		{
			TrainerForm existing;
			ManualResetEvent startupSignal;
			bool launchThread = false;
			lock (SyncRoot) {
				existing = _form;
				if (existing != null && !existing.IsDisposed) {
					RestoreExistingWindow(existing);
					return 0;
				}
				if (_starting) {
					startupSignal = _startupSignal;
				}
				else {
					_starting = true;
					_startupResult = 3;
					_startupSignal = new ManualResetEvent(false);
					startupSignal = _startupSignal;
					launchThread = true;
				}
			}

			if (launchThread) {
				Thread thread = new Thread(new ThreadStart(
					delegate { RunUi(startupSignal); }));
				thread.Name = "Terraria 1.4.5.6 Toolkit UI";
				thread.IsBackground = true;
				thread.SetApartmentState(ApartmentState.STA);
				try {
					thread.Start();
				}
				catch (Exception ex) {
					lock (SyncRoot) {
						_starting = false;
						_startupResult = 2;
					}
					startupSignal.Set();
					WriteError(ex);
					return 2;
				}
			}

			// ExecuteInDefaultAppDomain must not report success merely because a
			// worker thread was created.  Wait until the form has actually raised
			// Shown, or its constructor/message loop has failed.
			if (!startupSignal.WaitOne(30000))
				return 3;
			lock (SyncRoot)
				return _startupResult;
		}

		private static void RunUi(ManualResetEvent startupSignal)
		{
			TrainerForm form = null;
			try
			{
				if (!_winFormsConfigured) {
					Application.EnableVisualStyles();
					try {
						Application.SetCompatibleTextRenderingDefault(false);
					}
					catch (InvalidOperationException) {
						// Another in-process component may already have created a
						// WinForms handle.  The toolkit can use that established
						// text-rendering mode without failing its whole startup.
					}
					_winFormsConfigured = true;
				}
				form = new TrainerForm();
				form.Shown += delegate { CompleteStartup(startupSignal, form, 0); };
				Application.Run(form);
			}
			catch (Exception ex) {
				CompleteStartup(startupSignal, null, 4);
				WriteError(ex);
			}
			finally {
				if (form != null) {
					try {
						if (!form.IsDisposed)
							form.Dispose();
					}
					catch {
					}
				}
				CompleteStartup(startupSignal, null, 4);
				lock (SyncRoot) {
					if (ReferenceEquals(_form, form))
						_form = null;
				}
			}
		}

		private static void CompleteStartup(
			ManualResetEvent expectedSignal,
			TrainerForm form,
			int result)
		{
			ManualResetEvent signal = null;
			lock (SyncRoot) {
				if (!_starting || !ReferenceEquals(_startupSignal, expectedSignal))
					return;
				_form = form;
				_startupResult = result;
				_starting = false;
				signal = _startupSignal;
			}
			if (signal != null)
				signal.Set();
		}

		private static void RestoreExistingWindow(TrainerForm form)
		{
			try {
				if (!form.IsHandleCreated)
					return;
				form.BeginInvoke((MethodInvoker)delegate {
					if (!form.IsDisposed)
						form.RestoreFromExternalLaunch();
				});
			}
			catch {
				// The form may be closing between the state check and BeginInvoke.
			}
		}

		private static void WriteError(Exception ex)
		{
			try {
				string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				File.WriteAllText(Path.Combine(directory, "vanilla_backend_error.txt"), ex.ToString());
			}
			catch {
			}
		}
	}
}

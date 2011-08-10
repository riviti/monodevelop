using System;
using System.IO;
using System.Threading;

namespace MonoDevelop.Monitoring {

	abstract class CrashMonitor : ICrashMonitor {
		
		public static ICrashMonitor Create (int pid)
		{
			// FIXME: Add in proper detection for mac/windows/linux as appropriate
			return new MacCrashMonitor (pid);
		}
		
		public event EventHandler ApplicationExited;
		public event EventHandler<CrashEventArgs> CrashDetected;
		
		public int Pid {
			get; private set;
		}
		
		FileSystemWatcher Watcher {
			get; set;
		}
		
		public CrashMonitor (int pid, string path)
			: this (pid, path, "")
		{
			
		}
		
		public CrashMonitor (int pid, string path, string filter)
		{
			Pid = pid;
			Watcher = new FileSystemWatcher (path, filter);
			Watcher.Created += (o, e) => {
				OnCrashDetected (new CrashEventArgs (e.FullPath));
			};
			
			// Wait for the parent MD process to exit. This could be a crash
			// or a graceful exit.
			ThreadPool.QueueUserWorkItem (o => {
				// Do a loop rather than calling WaitForExit or hooking into
				// the Exited event as those do not work on MacOS on mono 2.10.3
				var info = System.Diagnostics.Process.GetProcessById (Pid);
				while (!info.HasExited) {
					Thread.Sleep (1000);
					info.Refresh ();
				}
				OnApplicationExited ();
			});
		}
		
		protected virtual void OnApplicationExited ()
		{
			if (ApplicationExited != null)
				ApplicationExited (this, EventArgs.Empty);
		}
		
		protected virtual void OnCrashDetected (CrashEventArgs e)
		{
			if (CrashDetected != null)
				CrashDetected (this, e);
		}


		public void Start () {
			Watcher.EnableRaisingEvents = true;
		}

		public void Stop () {
			Watcher.EnableRaisingEvents = false;
		}
	}
}

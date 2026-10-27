namespace CameraCaptureHangRepro;

public partial class MainPage : ContentPage
{
	const int Cycles = 100;
	static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(2);

	public MainPage()
	{
		InitializeComponent();
		Loaded += (_, _) =>
		{
			if (Environment.GetCommandLineArgs().Contains("--auto"))
				OnRunClicked(null, EventArgs.Empty);
		};
	}

	void Log(string message)
	{
		var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
		System.Diagnostics.Debug.WriteLine(line);
		try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "CameraCaptureHangRepro.log"), line + Environment.NewLine); } catch { }
		MainThread.BeginInvokeOnMainThread(() => LogBox.Text += line + Environment.NewLine);
	}

	async void OnRunClicked(object? sender, EventArgs e)
	{
		RunBtn.IsEnabled = false;
		try { await RunAsync(); }
		catch (Exception ex) { Log($"Unexpected error: {ex}"); }
		RunBtn.IsEnabled = true;
	}

	public async Task RunAsync()
	{
		if (await Permissions.RequestAsync<Permissions.Camera>() is not PermissionStatus.Granted)
		{
			Log("No camera permission");
			return;
		}

		var cameras = await cameraView.GetAvailableCameras(CancellationToken.None);
		if (cameras.Count == 0)
		{
			Log("No camera found");
			return;
		}
		cameraView.SelectedCamera = cameras[0];

		var random = new Random(1);
		var consecutiveTimeouts = 0;

		for (var i = 1; i <= Cycles; i++)
		{
			await cameraView.StartCameraPreview(CancellationToken.None);

			// Stop the preview a few ms after the capture starts, as happens when a page is closed during capture.
			var captureTask = cameraView.CaptureImage(CancellationToken.None);
			await Task.Delay(random.Next(0, 15));
			if (!Environment.GetCommandLineArgs().Contains("--control")) cameraView.StopCameraPreview();

			var completed = await Task.WhenAny(captureTask, Task.Delay(CaptureTimeout));
			if (completed == captureTask)
			{
				consecutiveTimeouts = 0;
				Log($"cycle {i}: capture completed ({(captureTask.IsFaulted ? "faulted" : "ok")})");
			}
			else
			{
				consecutiveTimeouts++;
				Log($"cycle {i}: CaptureImage did not complete within {CaptureTimeout.TotalSeconds}s (consecutive={consecutiveTimeouts})");
				if (consecutiveTimeouts >= 3)
				{
					Log("HANG REPRODUCED: later captures on the same CameraView are stuck behind the unreleased semaphore.");
					return;
				}
			}

			await Task.Delay(200);
		}

		Log("Finished without a hang. Run again.");
	}
}



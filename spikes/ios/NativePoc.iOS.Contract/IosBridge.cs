namespace NativePoc;

public interface IIosServices
{
	void Copy(string label, string text);

	void Haptic();

	void Share(string text);

	void Set(string key, string value);

	string Get(string key);

	void Theme(string mode);

	void Play(string key);

	string Http(string method, string url, string? body);

	string Location();

	void RememberRow(string screen, object row);

	void WriteBack(string screen);
}

public static class IosBridge
{
	public static readonly string[] Modules =
	[
		"clipboard.Copy",
		"haptics.Confirm",
		"share.Text",
		"storage.Set",
		"storage.Get",
		"theme.Mode",
		"animate.Play",
		"http.GetJson",
		"http.PostJson",
		"location.Read",
		"jni.Call",
		"telemetry.FieldDeliveries",
		"telemetry.SwitchDeliveries",
	];

	public static void CopyText(IIosServices services, string label, string text)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.Copy(label, text);
	}

	public static void PushRow(IIosServices services, string screen, object row, Action<string> push)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(push);
		services.RememberRow(screen, row);
		push(screen);
	}

	public static void WriteBack(IIosServices services, string screen)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.WriteBack(screen);
	}

	public static void Continue(Task<string> task, Action<string> resume)
	{
		ArgumentNullException.ThrowIfNull(task);
		ArgumentNullException.ThrowIfNull(resume);
		if (task.IsCompleted)
		{
			resume(IPlatform.AwaitText(task));
			return;
		}

		task.ContinueWith(done => resume(IPlatform.AwaitText(done)));
	}

	public static void Module(IIosServices services, string module, string method, string[] args)
	{
		switch (module, method, args.Length)
		{
			case ("jni", _, _):
				JniRegistry.Call(method, args);
				return;
			case ("telemetry", "FieldDeliveries", 0):
				ModuleTelemetry.FieldDeliveries++;
				return;
			case ("telemetry", "SwitchDeliveries", 0):
				ModuleTelemetry.SwitchDeliveries++;
				return;
		}

		ArgumentNullException.ThrowIfNull(services);
		switch (module, method)
		{
			case ("clipboard", "Copy"):
				services.Copy(args.Length > 1 ? args[0] : "", args.Length == 0 ? "" : args[^1]);
				return;
			case ("haptics", "Confirm"):
				services.Haptic();
				return;
			case ("share", "Text"):
				services.Share(args.Length == 0 ? "" : args[0]);
				return;
			case ("storage", "Set"):
				services.Set(args.Length == 0 ? "" : args[0], args.Length > 1 ? args[1] : "");
				return;
			case ("theme", "Mode"):
				services.Theme(args.Length == 0 ? "" : args[0]);
				return;
			case ("animate", "Play"):
				services.Play(args.Length == 0 ? "" : args[0]);
				return;
			default:
				throw new InvalidOperationException("Unknown platform module " + module + "." + method + ".");
		}
	}

	public static Task<string> ModuleAsync(IIosServices services, string module, string method, string[] args)
	{
		if (module == "jni")
			return Task.FromResult(JniRegistry.Call(method, args));
		ArgumentNullException.ThrowIfNull(services);
		switch (module, method)
		{
			case ("storage", "Get"):
				return Task.FromResult(services.Get(args.Length == 0 ? "" : args[0]));
			case ("http", "GetJson"):
				return Task.Run(() => services.Http("GetJson", args.Length == 0 ? "" : args[0], null));
			case ("http", "PostJson"):
				return Task.Run(() => services.Http("PostJson", args.Length == 0 ? "" : args[0], args.Length > 1 ? args[1] : ""));
			case ("location", "Read"):
				return Task.Run(services.Location);
			default:
				return Task.FromResult("http-error: unknown " + module + "." + method);
		}
	}
}

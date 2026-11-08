using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class ChangeDebouncerTests
{
	[Fact]
	public void Events_inside_the_window_flush_once()
	{
		var debouncer = new ChangeDebouncer(100);
		debouncer.Push("src/NativePoc.Screen/Screen.cs", 0);
		debouncer.Push("src/NativePoc.Screen/Screen.cs", 40);
		debouncer.Push("src/NativePoc.Screen/Helper.cs", 80);

		Assert.Null(debouncer.Poll(179));
		var batch = debouncer.Poll(180);
		Assert.Equal(
			["src/NativePoc.Screen/Screen.cs", "src/NativePoc.Screen/Helper.cs"],
			batch);
		Assert.Null(debouncer.Poll(500));
	}

	[Fact]
	public void A_later_event_starts_a_new_window()
	{
		var debouncer = new ChangeDebouncer(50);
		debouncer.Push("a.cs", 0);
		var first = debouncer.Poll(50);
		debouncer.Push("b.cs", 60);

		Assert.Equal(["a.cs"], first);
		Assert.Null(debouncer.Poll(109));
		Assert.Equal(["b.cs"], debouncer.Poll(110));
	}

	[Fact]
	public void Rapid_filesystem_writes_collapse_to_one_batch()
	{
		var directory = Directory.CreateTempSubdirectory("cnative-watch");
		try
		{
			var debouncer = new ChangeDebouncer(200);
			using var watcher = new FileSystemWatcher(directory.FullName)
			{
				Filter = "*.cs",
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
			};
			watcher.Changed += (_, args) => debouncer.Push(args.Name ?? "", Environment.TickCount64);
			watcher.Created += (_, args) => debouncer.Push(args.Name ?? "", Environment.TickCount64);
			watcher.EnableRaisingEvents = true;
			var path = Path.Combine(directory.FullName, "Screen.cs");
			File.WriteAllText(path, "a");
			Thread.Sleep(30);
			File.WriteAllText(path, "ab");
			Thread.Sleep(30);
			File.WriteAllText(path, "abc");
			var deadline = Environment.TickCount64 + 3000;
			IReadOnlyList<string>? batch = null;
			while (Environment.TickCount64 < deadline)
			{
				batch = debouncer.Poll(Environment.TickCount64);
				if (batch is not null)
					break;
				Thread.Sleep(20);
			}

			Assert.NotNull(batch);
			Assert.Equal(["Screen.cs"], batch);
		}
		finally
		{
			directory.Delete(true);
		}
	}
}

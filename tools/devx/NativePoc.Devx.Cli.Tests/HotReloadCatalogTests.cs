using NativePoc.Devx;

namespace NativePoc.Devx.Tests;

public sealed class HotReloadCatalogTests
{
	[Fact]
	public void The_hot_reload_table_matches_the_subset_rules()
	{
		var path = Path.Combine(Repo(), "docs", "hot-reload.md");
		var expected = HotReloadCatalog.Markdown().Replace("\r\n", "\n", StringComparison.Ordinal);
		var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
		Assert.Equal(expected, actual);
		foreach (var rule in HotReloadCatalog.Rules)
		{
			if (rule.Source is null)
				continue;
			var lowered = SubsetLowerer.TryLower(rule.Source, rule.Companion is null ? null : [rule.Companion]);
			if (rule.Can)
			{
				var detail = lowered.Diagnostics.Count == 0 ? "" : lowered.Diagnostics[0].Message;
				Assert.True(lowered.Ok, rule.Change + " " + detail);
			}
			else
				Assert.False(lowered.Ok, rule.Change);
		}
	}

	static string Repo()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "NativePoc.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}

		throw new InvalidOperationException("The repo root was not found.");
	}
}

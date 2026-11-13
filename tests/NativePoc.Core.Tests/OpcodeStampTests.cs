using System.Reflection;

namespace NativePoc.Tests;

public sealed class OpcodeStampTests
{
	[Fact]
	public void Every_opcode_constant_is_a_row_in_the_table()
	{
		var fields = typeof(RecordProgram)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.Name.StartsWith("Op", StringComparison.Ordinal) && field.FieldType == typeof(byte))
			.ToArray();
		Assert.Equal(OpcodeTable.Rows.Length, fields.Length);
		foreach (var field in fields)
		{
			var code = (byte)field.GetRawConstantValue()!;
			var row = Assert.Single(OpcodeTable.Rows, item => item.Name == field.Name);
			Assert.Equal(code, row.Code);
			Assert.Equal(field.Name, row.Name);
		}
	}

	[Fact]
	public void Adding_an_opcode_without_regenerating_the_catalog_fails_the_check()
	{
		var source = File.ReadAllText(Path.Combine(Repo(), "src", "NativePoc.Android", "NavCatalog.cs"));
		Assert.Null(CatalogVersion.Problem(source));
		var extra = new OpcodeTable.Row[OpcodeTable.Rows.Length + 1];
		OpcodeTable.Rows.CopyTo(extra, 0);
		extra[^1] = new OpcodeTable.Row("OpExtra", 48, 0);
		var stamp = OpcodeTable.Stamp(extra);
		Assert.NotEqual(OpcodeTable.Version, stamp);
		var problem = CatalogVersion.Problem(source, stamp);
		Assert.NotNull(problem);
		Assert.StartsWith("Counter opcode ", problem, StringComparison.Ordinal);
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

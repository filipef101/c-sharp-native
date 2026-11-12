namespace NativePoc.Tests;

public sealed class RecordWarmTests
{
	[Fact]
	public void Warm_evaluates_a_column_without_a_screen_id()
	{
		var element = RecordProgram.Warm(new WarmPlatform());
		var column = Assert.IsType<Column>(element);
		Assert.Single(column.Children);
		Assert.IsType<Text>(column.Children[0]);
	}

	sealed class WarmPlatform : IPlatform
	{
		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}
	}
}

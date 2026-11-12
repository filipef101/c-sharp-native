namespace NativePoc.Tests;

public sealed class ColumnTests
{
	[Fact]
	public void A_column_takes_its_children_as_arguments()
	{
		var column = new Column(new Text("a"), new Text("b"), new Text("c"));
		Assert.Equal(3, column.Children.Count);
		Assert.Equal("b", ((Text)column.Children[1]).Value);
		var listed = new Column(new List<Element> { new Text("one") });
		Assert.Equal("one", ((Text)Assert.Single(listed.Children)).Value);
	}
}

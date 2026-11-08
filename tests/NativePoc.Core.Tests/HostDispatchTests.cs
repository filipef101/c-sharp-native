namespace NativePoc.Tests;

public sealed class HostDispatchTests
{
	sealed class Field
	{
	}

	class PlainButton
	{
	}

	sealed class Toggle : PlainButton
	{
	}

	[Fact]
	public void A_switch_subtype_of_button_dispatches_as_a_switch()
	{
		Assert.Equal(
			HostInput.Switch,
			HostDispatch.Choose(typeof(Toggle), typeof(PlainButton), typeof(Field), typeof(Toggle)));
		Assert.Equal(
			HostInput.Button,
			HostDispatch.Choose(typeof(PlainButton), typeof(PlainButton), typeof(Field), typeof(Toggle)));
		Assert.Equal(
			HostInput.TextField,
			HostDispatch.Choose(typeof(Field), typeof(PlainButton), typeof(Field), typeof(Toggle)));
		Assert.Equal(
			HostInput.None,
			HostDispatch.Choose(typeof(string), typeof(PlainButton), typeof(Field), typeof(Toggle)));
	}
}

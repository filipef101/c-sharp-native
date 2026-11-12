using NativePoc;

namespace NativePoc.Tests;

public sealed class ActionLogTests
{
	[Fact]
	public void Parallel_notes_keep_their_own_stop()
	{
		var ready = new CountdownEvent(2);
		string? leftStop = "unset";
		string? rightStop = "unset";
		var leftSteps = -1;
		var rightSteps = -1;
		var left = new Thread(() =>
		{
			ActionLog.Note(1, 11, RecordProgram.CallDepthStop);
			ready.Signal();
			if (!ready.Wait(TimeSpan.FromSeconds(5)))
				return;
			leftStop = ActionLog.LastStop;
			leftSteps = ActionLog.LastSteps;
		});
		var right = new Thread(() =>
		{
			ActionLog.Note(2, 22, null);
			ready.Signal();
			if (!ready.Wait(TimeSpan.FromSeconds(5)))
				return;
			rightStop = ActionLog.LastStop;
			rightSteps = ActionLog.LastSteps;
		});
		left.Start();
		right.Start();
		Assert.True(left.Join(TimeSpan.FromSeconds(5)));
		Assert.True(right.Join(TimeSpan.FromSeconds(5)));
		Assert.Equal(RecordProgram.CallDepthStop, leftStop);
		Assert.Equal(11, leftSteps);
		Assert.Null(rightStop);
		Assert.Equal(22, rightSteps);
	}
}

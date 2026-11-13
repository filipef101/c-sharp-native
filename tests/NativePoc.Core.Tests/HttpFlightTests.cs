using System.Net;
using System.Net.Http;

namespace NativePoc.Tests;

public sealed class HttpFlightTests
{
	[Fact]
	public void Two_screens_cancel_independently()
	{
		var stories = HttpFlight.Open("stories");
		var visit = HttpFlight.Open("visit");
		HttpFlight.Attach("stories", stories);
		HttpFlight.Attach("visit", visit);
		Assert.True(HttpFlight.Live("stories"));
		Assert.True(HttpFlight.Live("visit"));

		Assert.True(HttpFlight.Cancel("stories"));
		Assert.True(HttpFlight.WasCancelled("stories", stories));
		Assert.False(HttpFlight.WasCancelled("visit", visit));
		Assert.False(HttpFlight.Live("stories"));
		Assert.True(HttpFlight.Live("visit"));
		Assert.False(HttpFlight.Cancel("stories"));

		var again = HttpFlight.Open("stories");
		HttpFlight.Attach("stories", again);
		Assert.True(HttpFlight.Live("stories"));
		Assert.True(HttpFlight.WasCancelled("stories", stories));
		Assert.False(HttpFlight.WasCancelled("stories", again));
		Assert.True(HttpFlight.Live("visit"));

		Assert.True(HttpFlight.Cancel("visit"));
		Assert.True(HttpFlight.WasCancelled("visit", visit));
		Assert.False(HttpFlight.Live("visit"));
		Assert.True(HttpFlight.Live("stories"));
	}

	[Fact]
	public void An_image_join_dies_with_its_screen()
	{
		var stories = HttpFlight.Open("stories-image");
		var image = HttpFlight.Join("stories-image");
		Assert.Equal(stories, image);
		var visit = HttpFlight.Join("visit-image");
		Assert.False(HttpFlight.WasCancelled("stories-image", image));
		Assert.False(HttpFlight.WasCancelled("visit-image", visit));

		Assert.True(HttpFlight.Cancel("stories-image"));
		Assert.True(HttpFlight.WasCancelled("stories-image", image));
		Assert.False(HttpFlight.WasCancelled("visit-image", visit));
		var late = HttpFlight.Join("stories-image");
		Assert.Equal(image, late);
		Assert.True(HttpFlight.WasCancelled("stories-image", late));

		var again = HttpFlight.Open("stories-image");
		Assert.False(HttpFlight.WasCancelled("stories-image", again));
		Assert.True(HttpFlight.WasCancelled("stories-image", image));
		Assert.False(HttpFlight.WasCancelled("visit-image", visit));
	}

	[Fact]
	public void Cancel_disposes_the_managed_image_read()
	{
		var id = "managed-abort";
		var generation = HttpFlight.Join(id);
		var ran = 0;
		Assert.True(HttpFlight.BindAbort(id, generation, () => ran++));
		Assert.True(HttpFlight.Cancel(id));
		Assert.Equal(1, ran);
		Assert.True(HttpFlight.WasCancelled(id, generation));
		Assert.False(HttpFlight.BindAbort(id, generation, () => ran++));
		Assert.Equal(1, ran);
	}

	[Fact]
	public void Cancel_aborts_a_blocked_managed_read()
	{
		var id = "managed-socket-" + Guid.NewGuid().ToString("N");
		var generation = HttpFlight.Join(id);
		var gate = new BlockedRead();
		var cancel = new CancellationTokenSource();
		var response = new NotedResponse();
		Assert.True(HttpFlight.BindAbort(id, generation, () => ResponseAbort.Stop(cancel, gate, response)));
		var read = Task.Run(() => gate.Read(new byte[8], 0, 8));
		Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(1)));
		Assert.True(HttpFlight.Cancel(id));
		var error = Assert.Throws<AggregateException>(() => read.Wait(TimeSpan.FromSeconds(1)));
		Assert.IsType<IOException>(error.InnerException);
		Assert.True(gate.WasDisposed);
		Assert.True(cancel.IsCancellationRequested);
		Assert.True(response.Disposed);
	}

	sealed class NotedResponse : HttpResponseMessage
	{
		public NotedResponse()
			: base(HttpStatusCode.OK)
		{
		}

		public bool Disposed { get; private set; }

		protected override void Dispose(bool disposing)
		{
			Disposed = true;
			base.Dispose(disposing);
		}
	}

	sealed class BlockedRead : Stream
	{
		readonly ManualResetEventSlim _entered = new(false);
		readonly ManualResetEventSlim _release = new(false);
		int _disposed;

		public ManualResetEventSlim Entered => _entered;

		public bool WasDisposed => Volatile.Read(ref _disposed) == 1;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			_entered.Set();
			_release.Wait();
			if (Volatile.Read(ref _disposed) == 1)
				throw new IOException("The read was aborted.");
			return 0;
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			Volatile.Write(ref _disposed, 1);
			_release.Set();
			base.Dispose(disposing);
		}
	}
}

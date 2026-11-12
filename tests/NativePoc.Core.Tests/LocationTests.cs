using NativePoc;

namespace NativePoc.Tests;

public sealed class LocationTests
{
	[Fact]
	public void Classify_covers_granted_denied_and_permanently_denied()
	{
		Assert.Equal(LocationAccess.Granted, LocationAccessRules.Classify(granted: true, shouldShowRationale: false, asked: false));
		Assert.Equal(LocationAccess.Granted, LocationAccessRules.Classify(granted: true, shouldShowRationale: true, asked: true));
		Assert.Equal(LocationAccess.Denied, LocationAccessRules.Classify(granted: false, shouldShowRationale: false, asked: false));
		Assert.Equal(LocationAccess.Denied, LocationAccessRules.Classify(granted: false, shouldShowRationale: true, asked: true));
		Assert.Equal(LocationAccess.PermanentlyDenied, LocationAccessRules.Classify(granted: false, shouldShowRationale: false, asked: true));
	}

	[Fact]
	public void A_fix_round_trips_through_the_wire_record()
	{
		var fix = new LocationFix(LocationAccess.Granted, 37.8, -122.4, 12.5f);
		var text = Location.Format(fix);
		var parsed = Location.Parse(text);
		Assert.Equal(LocationAccess.Granted, parsed.Access);
		Assert.Equal(37.8, parsed.Latitude);
		Assert.Equal(-122.4, parsed.Longitude);
		Assert.Equal(12.5f, parsed.AccuracyMeters);
		Assert.Equal(LocationAccess.Denied, Location.Parse(Location.Format(new LocationFix(LocationAccess.Denied, 1, 2, 3))).Access);
		Assert.Equal(LocationAccess.PermanentlyDenied, Location.Parse("permanently-denied").Access);
		Assert.Throws<InvalidOperationException>(() => Location.Parse("nope"));
	}

	[Fact]
	public async Task Read_asks_the_platform_module()
	{
		var platform = new Recording();
		var wire = await Location.Read(platform);
		Assert.Equal(("location", "Read"), platform.Call);
		Assert.Equal("granted 37.8 -122.4 4", wire);
		var fix = Location.Parse(wire);
		Assert.Equal(37.8, fix.Latitude);
		Assert.Equal(-122.4, fix.Longitude);
	}

	sealed class Recording : IPlatform
	{
		public (string Module, string Method) Call { get; private set; }

		public void CopyNonce(string nonce)
		{
		}

		public void RequestRender()
		{
		}

		public Task<string> ModuleAsync(string module, string method, string[] args)
		{
			Call = (module, method);
			Assert.Empty(args);
			return Task.FromResult("granted 37.8 -122.4 4");
		}
	}
}

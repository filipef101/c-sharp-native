using System.Text.Json;
using System.Text.Json.Serialization;
using NativePoc.Feed;

namespace NativePoc;

public static class FeedModule
{
	public static void Apply(IPlatform platform, FeedState state, string json)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(state);
		FeedJson.Fill(state.Items, json);
		platform.Module("feed", "Apply", [json]);
	}
}

static class FeedJson
{
	public static void Fill(List<FeedItem> items, string json)
	{
		items.Clear();
		if (!TryRead(json, out var rows))
			return;
		foreach (var row in rows)
			items.Add(new FeedItem { Id = row.Id ?? "", Title = row.Title ?? "" });
	}

	public static void FillBag(string json)
	{
		var bag = EffectContext.Bag;
		if (bag is null)
			return;
		bag.ClearList("Items");
		if (!TryRead(json, out var rows))
			return;
		foreach (var row in rows)
			bag.AddRow("Items", [row.Id ?? "", row.Title ?? ""]);
	}

	static bool TryRead(string json, out List<FeedRow> rows)
	{
		rows = [];
		if (json.Length == 0 || json.StartsWith("http-error:", StringComparison.Ordinal))
			return false;
		var document = JsonSerializer.Deserialize(json, FeedJsonContext.Default.FeedDocument);
		if (document?.Items is null)
			return false;
		rows = document.Items;
		return true;
	}
}

public sealed class FeedDocument
{
	public List<FeedRow>? Items { get; set; }
}

public sealed class FeedRow
{
	public string? Id { get; set; }

	public string? Title { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FeedDocument))]
[JsonSerializable(typeof(FeedRow))]
sealed partial class FeedJsonContext : JsonSerializerContext
{
}

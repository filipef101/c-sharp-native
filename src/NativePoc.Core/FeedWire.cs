using System.Text.Json.Serialization;

namespace NativePoc;

internal static class FeedWire
{
	public static bool TryRead(string json, out List<FeedPost> rows)
	{
		rows = [];
		if (json.Length == 0 || json.StartsWith("http-error:", StringComparison.Ordinal))
			return false;
		try
		{
			var document = System.Text.Json.JsonSerializer.Deserialize(json, FeedJsonContext.Default.FeedDocument);
			if (document is null)
				return false;
			if (document.Items is { Count: > 0 })
				rows.AddRange(document.Items);
			return true;
		}
		catch (System.Text.Json.JsonException)
		{
			return false;
		}
	}
}

internal sealed class FeedDocument
{
	public List<FeedPost> Items { get; set; } = [];
}

internal sealed class FeedPost
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";
}

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FeedDocument))]
internal partial class FeedJsonContext : JsonSerializerContext
{
}

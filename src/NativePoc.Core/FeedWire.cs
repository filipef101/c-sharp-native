using System.Text.Json;
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
			var document = JsonSerializer.Deserialize(json, FeedJsonContext.Default.FeedDocument);
			if (document is null)
				return false;
			if (document.Items is { Count: > 0 })
				rows.AddRange(document.Items);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}

[JsonConverter(typeof(FeedDocumentConverter))]
internal sealed class FeedDocument
{
	public List<FeedPost> Items { get; set; } = [];
}

internal sealed class FeedPost
{
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(FeedDocument))]
internal partial class FeedJsonContext : JsonSerializerContext
{
}

internal sealed class FeedDocumentConverter : JsonConverter<FeedDocument>
{
	public override FeedDocument? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
			return null;
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException();
		var document = new FeedDocument();
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
				return document;
			if (reader.TokenType != JsonTokenType.PropertyName)
				throw new JsonException();
			if (reader.ValueTextEquals("items"u8))
			{
				if (!reader.Read())
					throw new JsonException();
				document.Items = ReadItems(ref reader);
			}
			else
			{
				reader.Skip();
			}
		}

		throw new JsonException();
	}

	public override void Write(Utf8JsonWriter writer, FeedDocument value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WritePropertyName("items");
		writer.WriteStartArray();
		foreach (var post in value.Items)
		{
			writer.WriteStartObject();
			writer.WriteString("id", post.Id);
			writer.WriteString("title", post.Title);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
	}

	static List<FeedPost> ReadItems(ref Utf8JsonReader reader)
	{
		if (reader.TokenType == JsonTokenType.Null)
			return [];
		if (reader.TokenType != JsonTokenType.StartArray)
			throw new JsonException();
		var rows = new List<FeedPost>();
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndArray)
				return rows;
			rows.Add(ReadPost(ref reader));
		}

		throw new JsonException();
	}

	static FeedPost ReadPost(ref Utf8JsonReader reader)
	{
		if (reader.TokenType != JsonTokenType.StartObject)
			throw new JsonException();
		var post = new FeedPost();
		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
				return post;
			if (reader.TokenType != JsonTokenType.PropertyName)
				throw new JsonException();
			if (reader.ValueTextEquals("id"u8))
			{
				post.Id = reader.Read() ? reader.GetString() ?? "" : throw new JsonException();
			}
			else if (reader.ValueTextEquals("title"u8))
			{
				post.Title = reader.Read() ? reader.GetString() ?? "" : throw new JsonException();
			}
			else
			{
				reader.Skip();
			}
		}

		throw new JsonException();
	}
}

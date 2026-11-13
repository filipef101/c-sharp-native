using System.Globalization;
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

	public static void Generate(IPlatform platform, FeedState state)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(state);
		if (state.Generated.Count > 0)
			return;
		for (var n = 0; n < 1000; n++)
		{
			var id = n.ToString("0000", CultureInfo.InvariantCulture);
			state.Generated.Add(new FeedItem { Id = id, Title = "Row " + id });
		}
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
			items.Add(new FeedItem { Id = row.Id, Title = row.Title });
	}

	public static void FillBag(string json)
	{
		var bag = EffectContext.Bag;
		if (bag is null)
		{
			var id = ScreenNav.Shared.TopId();
			if (id.Length == 0)
				return;
			bag = ScreenNav.Shared.Bag(id);
		}

		bag.ClearList("Items");
		if (!TryRead(json, out var rows))
			return;
		foreach (var row in rows)
			bag.AddRow("Items", [row.Id, row.Title]);
		Android.Util.Log.Info(MainActivity.Tag, "json-bag screen=" + ScreenNav.Shared.TopId() + " items=" + rows.Count.ToString(CultureInfo.InvariantCulture));
	}

	static bool TryRead(string json, out List<Row> rows)
	{
		rows = [];
		if (json.Length == 0 || json.StartsWith("http-error:", StringComparison.Ordinal))
			return false;
		if (FeedWire.TryRead(json, out var typed))
		{
			foreach (var row in typed)
				rows.Add(new Row(row.Id, row.Title));
			Android.Util.Log.Info(MainActivity.Tag, "json-source items=" + rows.Count.ToString(CultureInfo.InvariantCulture));
			return true;
		}

		var scan = new Scan(json);
		return scan.TryDocument(rows);
	}

	readonly struct Row(string id, string title)
	{
		public string Id { get; } = id;

		public string Title { get; } = title;
	}

	struct Scan
	{
		readonly string _text;
		int _at;

		public Scan(string text)
		{
			_text = text;
			_at = 0;
		}

		public bool TryDocument(List<Row> rows)
		{
			if (!Eat('{'))
				return false;
			if (Eat('}'))
				return true;
			do
			{
				if (!TryString(out var key) || !Eat(':'))
					return false;
				if (key == "items")
				{
					if (!TryItems(rows))
						return false;
				}
				else if (!SkipValue())
					return false;
			}
			while (Eat(','));

			return Eat('}');
		}

		bool TryItems(List<Row> rows)
		{
			if (!Eat('['))
				return false;
			if (Eat(']'))
				return true;
			do
			{
				if (!TryRow(out var row))
					return false;
				rows.Add(row);
			}
			while (Eat(','));

			return Eat(']');
		}

		bool TryRow(out Row row)
		{
			row = default;
			if (!Eat('{'))
				return false;
			var id = "";
			var title = "";
			if (!Eat('}'))
			{
				do
				{
					if (!TryString(out var key) || !Eat(':'))
						return false;
					if (key is "id" or "title")
					{
						if (!TryString(out var value))
							return false;
						if (key == "id")
							id = value;
						else
							title = value;
					}
					else if (!SkipValue())
						return false;
				}
				while (Eat(','));

				if (!Eat('}'))
					return false;
			}

			row = new Row(id, title);
			return true;
		}

		bool SkipValue()
		{
			Skip();
			if (_at >= _text.Length)
				return false;
			var c = _text[_at];
			if (c == '"')
				return TryString(out _);
			if (c == '{')
				return SkipObject();
			if (c == '[')
				return SkipArray();
			if (Starts("true"))
			{
				_at += 4;
				return true;
			}

			if (Starts("false"))
			{
				_at += 5;
				return true;
			}

			if (Starts("null"))
			{
				_at += 4;
				return true;
			}

			if (c is '-' or '+' or '.' or (>= '0' and <= '9'))
				return SkipNumber();
			return false;
		}

		bool SkipObject()
		{
			if (!Eat('{'))
				return false;
			if (Eat('}'))
				return true;
			do
			{
				if (!TryString(out _) || !Eat(':') || !SkipValue())
					return false;
			}
			while (Eat(','));

			return Eat('}');
		}

		bool SkipArray()
		{
			if (!Eat('['))
				return false;
			if (Eat(']'))
				return true;
			do
			{
				if (!SkipValue())
					return false;
			}
			while (Eat(','));

			return Eat(']');
		}

		bool SkipNumber()
		{
			Skip();
			var start = _at;
			while (_at < _text.Length)
			{
				var c = _text[_at];
				if (c is >= '0' and <= '9' or '-' or '+' or '.' or 'e' or 'E')
					_at++;
				else
					break;
			}

			return _at > start;
		}

		bool TryString(out string value)
		{
			value = "";
			Skip();
			if (_at >= _text.Length || _text[_at] != '"')
				return false;
			_at++;
			var buffer = new System.Text.StringBuilder();
			while (_at < _text.Length)
			{
				var c = _text[_at++];
				if (c == '"')
				{
					value = buffer.ToString();
					return true;
				}

				if (c != '\\')
				{
					buffer.Append(c);
					continue;
				}

				if (_at >= _text.Length)
					return false;
				var escaped = _text[_at++];
				switch (escaped)
				{
					case '"' or '\\' or '/':
						buffer.Append(escaped);
						break;
					case 'b':
						buffer.Append('\b');
						break;
					case 'f':
						buffer.Append('\f');
						break;
					case 'n':
						buffer.Append('\n');
						break;
					case 'r':
						buffer.Append('\r');
						break;
					case 't':
						buffer.Append('\t');
						break;
					case 'u':
						if (_at + 4 > _text.Length)
							return false;
						var hex = _text.Substring(_at, 4);
						_at += 4;
						if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var code))
							return false;
						buffer.Append((char)code);
						break;
					default:
						return false;
				}
			}

			return false;
		}

		bool Eat(char want)
		{
			Skip();
			if (_at >= _text.Length || _text[_at] != want)
				return false;
			_at++;
			return true;
		}

		void Skip()
		{
			while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
				_at++;
		}

		bool Starts(string word)
		{
			Skip();
			return _at + word.Length <= _text.Length && string.CompareOrdinal(_text, _at, word, 0, word.Length) == 0;
		}
	}
}

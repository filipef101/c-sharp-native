using System.Globalization;

namespace NativePoc;

public static class L
{
	public static string Get(string key) => StringTable.Get(key);

	public static string Format(string template, params string[] args) => StringTable.Format(template, args);

	public static string Plural(string count, string one, string other) => StringTable.Plural(count, one, other);
}

public static class StringTable
{
	static readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
	static string _language = "en";
	static bool _pseudo;
	static bool _rtl;

	public static bool RightToLeft => _rtl;

	public static string Language => _language;

	public static string AssetSuffix => _language == "pt" ? ".pt" : "";

	public static void Use(string locale)
	{
		var tag = (locale ?? "").Replace('_', '-');
		var language = tag.Split('-', 2)[0];
		_pseudo = tag.Equals("ar-XB", StringComparison.OrdinalIgnoreCase)
			|| tag.Equals("en-XA", StringComparison.OrdinalIgnoreCase);
		_rtl = tag.Equals("rtl", StringComparison.OrdinalIgnoreCase)
			|| tag.Contains("XB", StringComparison.OrdinalIgnoreCase)
			|| language.Equals("ar", StringComparison.OrdinalIgnoreCase);
		_language = language.Equals("pt", StringComparison.OrdinalIgnoreCase) ? "pt" : "en";
		_map.Clear();
		if (_language == "pt")
			FillPortuguese();
	}

	public static string Get(string key)
	{
		var text = key is not null && _map.TryGetValue(key, out var found) ? found : key ?? "";
		return _pseudo ? "\u200F[" + text + "]" : text;
	}

	public static string Format(string template, params string[] args)
	{
		var text = template ?? "";
		if (args is null)
			return text;
		var count = args.Length > 10 ? 10 : args.Length;
		for (var index = 0; index < count; index++)
		{
			var token = "{" + index.ToString(CultureInfo.InvariantCulture) + "}";
			text = text.Replace(token, args[index] ?? "", StringComparison.Ordinal);
		}

		return text;
	}

	public static string Plural(string countText, string one, string other)
	{
		var count = 0;
		if (countText is not null)
			int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
		var chosen = count == 1 ? one ?? "" : other ?? "";
		return Format(chosen, count.ToString(CultureInfo.InvariantCulture));
	}

	static void FillPortuguese()
	{
		Add("Trips", "Viagens");
		Add("Night", "Noite");
		Add("Day", "Dia");
		Add("Dark", "Escuro");
		Add("Back", "Voltar");
		Add("Trip", "Viagem");
		Add("Note", "Nota");
		Add("Packing", "Bagagem");
		Add("Share", "Partilhar");
		Add("Share note", "Partilhar nota");
		Add("Add to kit", "Acrescentar ao kit");
		Add("Item name", "Nome do artigo");
		Add("Save", "Guardar");
		Add("Save item", "Guardar artigo");
		Add("Pull to refresh", "Puxar para atualizar");
		Add("Hold a row", "Manter premida uma linha");
		Add("Packed", "Arrumado");
		Add("Open", "Por arrumar");
		Add("Add item", "Acrescentar artigo");
		Add("1 trip", "1 viagem");
		Add("{0} trips", "{0} viagens");
		Add("loading", "a carregar");
		Add("Clear and cold. Pack the stove.", "Céu limpo, 12 °C. Arruma o fogão.");
		Add("List updated", "Lista atualizada");
		Add("Coast road", "Estrada da costa");
		Add("Ridge night", "Noite no cume");
		Add("Lake loop", "Volta ao lago");
		Add("Tent fly", "Sobrecapa");
		Add("Stove", "Fogão");
		Add("Page", "Página");
	}

	static void Add(string key, string value) => _map[key] = value;
}

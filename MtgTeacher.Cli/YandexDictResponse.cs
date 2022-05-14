using System.Text.Json.Serialization;

namespace MtgTeacher.Cli;

// Root myDeserializedClass = JsonSerializer.Deserialize<Root>(myJsonResponse);
public record Def(
	[property: JsonPropertyName("text")] string Text,
	[property: JsonPropertyName("pos")] string Pos,
	[property: JsonPropertyName("ts")] string Ts,
	[property: JsonPropertyName("tr")] IReadOnlyList<Tr> Tr
);

public record Ex(
	[property: JsonPropertyName("text")] string Text,
	[property: JsonPropertyName("tr")] IReadOnlyList<Tr> Tr
);

public record Head(
);

public record Mean(
	[property: JsonPropertyName("text")] string Text
);

public record YandexDictResponse(
	[property: JsonPropertyName("head")] Head Head,
	[property: JsonPropertyName("def")] IReadOnlyList<Def> Def,
	[property: JsonPropertyName("code")] uint? Code,
	[property: JsonPropertyName("message")] string? Message
);

public record Syn(
	[property: JsonPropertyName("text")] string Text,
	[property: JsonPropertyName("pos")] string Pos,
	[property: JsonPropertyName("fr")] int Fr
);

public record Tr(
	[property: JsonPropertyName("text")] string Text,
	[property: JsonPropertyName("pos")] string Pos,
	[property: JsonPropertyName("fr")] int Fr,
	[property: JsonPropertyName("syn")] IReadOnlyList<Syn>? Syn,
	[property: JsonPropertyName("mean")] IReadOnlyList<Mean> Mean,
	[property: JsonPropertyName("ex")] IReadOnlyList<Ex>? Ex
);
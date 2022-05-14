using System.Text.Json.Serialization;

namespace MtgTeacher.Cli;

public record YandexTranslateResponse(
	[property: JsonPropertyName("translations")]
	IReadOnlyList<Translation> Translations,
	[property: JsonPropertyName("code")] uint? Code,
	[property: JsonPropertyName("message")] string? Message 

);

public record Translation(
	[property: JsonPropertyName("text")] string Text,
	[property: JsonPropertyName("detectedLanguageCode")]
	string DetectedLanguageCode
);
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MtgTeacher.Cli;

public class MtgListParser
{
	private readonly ILogger<MtgListParser> _logger;
	private const string SideboardString = "sideboard";
	private const string DeckString = "deck";
	private readonly Regex _parseRegex = new(@"^\d+\s([\w\s]+)");


	public MtgListParser(ILogger<MtgListParser> logger)
	{
		_logger = logger;
	}

	public List<string> Parse(string filename)
	{
		var result = new List<string>();
		var lines = File.ReadAllLines(filename);

		foreach (var line in lines)
		{
			var parseResult = ParseLine(line);
			if (parseResult != null)
			{
				result.Add(parseResult);
			}
		}

		return result;
	}

	private string? ParseLine(string inputLine)
	{
		var line = inputLine.ToLowerInvariant();

		if (line is DeckString or SideboardString || string.IsNullOrEmpty(line))
		{
			return null;
		}

		var matchResult = _parseRegex.Match(line);

		var cardName = matchResult.Groups[1].Value.Trim();

		return cardName;
	}
}
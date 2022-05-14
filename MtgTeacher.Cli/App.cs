using System.Data;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnkiSharp;
using LazyCache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScryfallApi.Client;
using ScryfallApi.Client.Models;
using YandexTranslateCSharpSdk;

namespace MtgTeacher.Cli;

public class App : ConsoleAppBase
{
	private readonly ILogger<App> _logger;
	private readonly ScryfallApiClient _scryfallApiClient;
	private readonly IAppCache _cache;
	private readonly MtgListParser _mtgListParser;
	private readonly AppConfig _appConfig;

	private readonly Dictionary<string, string> _replaceDict = new()
	{
		{ "enters", "enter" },
		{ "attacks", "attack" },
		{ "leaves", "to leave" }
	};

	private readonly AsyncRateLimitedSemaphore _yandexTimeSemaphore;
	private readonly HttpClient _scryFallHttpClient;
	private readonly HttpClient _httpClientTranslate;
	private readonly HttpClient _httpClientDict;


	public App(ILogger<App> logger, ScryfallApiClient scryfallApiClient,
		IOptions<AppConfig> appConfig,
		IAppCache cache,
		MtgListParser mtgListParser
	)
	{
		_logger = logger;
		_scryfallApiClient = scryfallApiClient;
		_cache = cache;
		_mtgListParser = mtgListParser;
		_appConfig = appConfig.Value;
		_yandexTimeSemaphore = new AsyncRateLimitedSemaphore(20, TimeSpan.FromSeconds(1));

		_scryFallHttpClient = new HttpClient();
		_scryFallHttpClient.BaseAddress = new Uri("https://api.scryfall.com/");

		_httpClientTranslate = new HttpClient();
		_httpClientTranslate.BaseAddress = new Uri("https://translate.api.cloud.yandex.net");
		_httpClientTranslate.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
			_appConfig.YandexIamToken);
		_httpClientTranslate.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

		_httpClientDict = new HttpClient();
		_httpClientDict.BaseAddress = new Uri("https://dictionary.yandex.net");
	}

	record CardData(Card enCard, Card ruCard, List<DictResult> DictResults);

	[RootCommand]
	public async Task Generate([Option(null, "Card list file")] string cardListFile)
	{
		var cardsData = new List<CardData>();

		var cardsListData = _mtgListParser.Parse(cardListFile);

		var cancellationTokenSource = new CancellationTokenSource();


		var tasks = cardsListData.Select(cardName => Task.Run(async () =>
			{
				_logger.LogInformation("Receiving card info {card}", cardName);
				var (enCard, ruCard) = await Scryfall(cardName, cancellationTokenSource, cancellationTokenSource.Token);
				var enDictData = await Dict(enCard, cancellationTokenSource, cancellationTokenSource.Token);
				cardsData.Add(new CardData(enCard, ruCard, enDictData));
			}, cancellationTokenSource.Token))
			.ToList();

		await Task.WhenAll(tasks);

		CreateAnkiOutput(cardsData);
	}

	private void CreateAnkiOutput(List<CardData> cardsData)
	{
		var output = new Anki("MTG Cards");
		output.SetFields("English", "Russian", "Examples");
		output.SetFormat("{0}\\n<hr id=answer>\\n {1}\\n<hr> {2}");


		foreach (var cardData in cardsData)
		{
			foreach (var dictResult in cardData.DictResults)
			{
				var translateResponseTranslations = dictResult.TranslateResponse?.Translations;
				if (translateResponseTranslations == null)
				{
					_logger.LogInformation("No translation for {word}", dictResult.Word);
					continue;
				}

				var mainTranslation = string.Join(", ", translateResponseTranslations.Select(el => el.Text));

				var secondaryTranslation = "";
				foreach (var def in dictResult.DictResponse.Def.Take(1))
				{
					foreach (var tr in def.Tr.Take(1))
					{
						secondaryTranslation += $@"</br>({tr.Pos}) {tr.Text}<br/>{def.Ts}<br/>";


						if (tr.Ex is { Count: > 0 })
						{
							secondaryTranslation +=
								$@"<small>Пример: {string.Join(",", tr.Ex.Take(2).Select(el => $"{el.Text} -> {string.Join(",", el.Tr.Select(el => el.Text))}"))}</small><br/>";
						}

						if (tr.Syn is { Count: > 0 })
						{
							secondaryTranslation +=
								$@"<small>{string.Join(",", (tr.Syn).Take(2).Select(el => $"({el.Pos}) {el.Text}"))}</small>";
						}
					}
				}

				output.AddItem(dictResult.Word, mainTranslation,
					string.IsNullOrEmpty(secondaryTranslation) ? "---" : secondaryTranslation);
			}
		}

		output.CreateApkgFile(".");
	}

	private async Task<(Card enCard, Card ruCard)> Scryfall(string cardName,
		CancellationTokenSource cancellationTokenSource,
		CancellationToken cancellationToken)
	{
		var cacheKey = $"scryfall_{cardName}";

		(Card enCard, Card ruCard) result = await _cache.GetOrAddAsync(cacheKey, async () =>
		{
			try
			{
				var cardData = await _scryfallApiClient.Cards.Search(cardName, 1, new SearchOptions()
				{
					IncludeMultilingual = true,
					Sort = SearchOptions.CardSort.Cmc
				});

				var card = cardData.Data.First();

				using var ruRequest = new HttpRequestMessage(HttpMethod.Get,
					$"cards/{card.Set}/{card.CollectorNumber}/ru");
				var ruResult = await _scryFallHttpClient.SendAsync(ruRequest);
				var ruCardJson = await ruResult.Content.ReadAsStringAsync();
				var ruCard = JsonSerializer.Deserialize<Card>(ruCardJson)!;


				using var enRequest = new HttpRequestMessage(HttpMethod.Get,
					$"cards/{card.Set}/{card.CollectorNumber}/en");
				var enResult = await _scryFallHttpClient.SendAsync(enRequest);
				var enCardJson = await enResult.Content.ReadAsStringAsync();
				var enCard = JsonSerializer.Deserialize<Card>(enCardJson)!;

				result = (enCard, ruCard);

				return result;
			}
			catch (Exception)
			{
				cancellationTokenSource.Cancel();
				throw;
			}
		});

		return result;
	}


	public record DictResult(string Word, YandexTranslateResponse TranslateResponse, YandexDictResponse DictResponse);


	private async Task<List<DictResult>> Dict(Card scryfallCard, CancellationTokenSource cancellationTokenSource,
		CancellationToken cancellationToken)
	{
		var text = string.Join(" ", new List<string>()
		{
			scryfallCard.Name, scryfallCard.OracleText, scryfallCard.TypeLine
		});

		// var punctuation = text.Where(char.IsPunctuation).Distinct().ToArray();
		var words = text.Split()
			.Select(x => x.Trim('{', '}').Trim())
			.Where(el => !string.IsNullOrWhiteSpace(el) && !string.IsNullOrEmpty(el))
			.Where(el => !int.TryParse(el, out _)).ToList();
		words = words.Where(el => el.All(el => { return char.IsLetter(el) || char.IsSeparator(el) || el == '-'; }))
			.ToList();
		// .Where(el => Regex.IsMatch(el, @"^[a-zA-Z\-,]+$"))
		words = words.Where(el => el.Length != 1)
			.Select(el => _replaceDict.ContainsKey(el) ? _replaceDict[el] : el)
			.Select(el => el.ToLowerInvariant())
			.Distinct().ToList();

		// Отдельно добавляем полное название карты
		words.Add(scryfallCard.Name);


		var result = new List<DictResult>();
		foreach (var word in words)
		{
			try
			{
				var cacheKey = $"word_{word.ToLowerInvariant()}";

				var resultVal = await _cache.GetOrAddAsync(cacheKey, async () =>
				{
					var uri =
						$"/api/v1/dicservice.json/lookup?key={_appConfig.YandexDictKey}&lang=en-ru&text={word}&ui=ru";
					_logger.LogDebug(uri);
					using var request = new HttpRequestMessage(HttpMethod.Get, uri);
					await _yandexTimeSemaphore.WaitAsync();
					var yandexResult = await _httpClientDict.SendAsync(request);
					var dataJson = await yandexResult.Content.ReadAsStringAsync();
					var yandexDictResponse = JsonSerializer.Deserialize<YandexDictResponse>(dataJson)!;

					if (yandexDictResponse.Code != null)
					{
						throw new ApplicationException($"Yandex dict error: {dataJson}");
					}

					var httpTranslateRequest = new HttpRequestMessage(HttpMethod.Post, "translate/v2/translate");
					var serialize = JsonSerializer.Serialize(new
					{
						folderId = _appConfig.YandexFolder,
						texts = new[] { word },
						targetLanguageCode = "ru",
						sourceLanguageCode = "en"
					});
					// Console.WriteLine(serialize);
					httpTranslateRequest.Content = new StringContent(serialize, Encoding.UTF8, "application/json");


					await _yandexTimeSemaphore.WaitAsync();
					var translateResult = await _httpClientTranslate.SendAsync(httpTranslateRequest);
					var translateResultString = await translateResult.Content.ReadAsStringAsync();
					var translateResponse =
						JsonSerializer.Deserialize<YandexTranslateResponse>(
							translateResultString)!;

					if (translateResponse.Code != null)
					{
						throw new ApplicationException($"Yandex translate error: {translateResultString}");
					}

					return new DictResult(word, translateResponse, yandexDictResponse);
				});

				result.Add(resultVal);
			}
			catch (Exception)
			{
				cancellationTokenSource.Cancel();
				throw;
			}
		}

		return result;
	}
}
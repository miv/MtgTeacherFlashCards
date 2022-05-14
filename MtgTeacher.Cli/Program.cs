// You can use full feature of Generic Host(same as ASP.NET Core).

using ConsoleAppFramework;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MtgTeacher.Cli;
using ScryfallApi.Client;

var builder = ConsoleApp.CreateBuilder(args);
builder.ConfigureServices((ctx, services) =>
{
	services.AddOptions();
	// Register appconfig.json to IOption<MyConfig>
	var configurationSection = ctx.Configuration.GetSection("AppConfig");
	services.Configure<AppConfig>(configurationSection);

	services.AddSingleton<MtgListParser>();
	services.AddLazyCache();

	services.AddScryfallApiClient();
});


var app = builder.Build();

app.AddCommands<App>();

// some argument from DI.
// app.AddRootCommand((ConsoleAppContext ctx, IOptions<AppConfig> config, string name) => { });
// app.AddRootCommand(App);

app.Run();
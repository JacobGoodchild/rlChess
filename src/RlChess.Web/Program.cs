using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RlChess.Web;
using Microsoft.Extensions.DependencyInjection;
using RlChess.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<Prefs>();
builder.Services.AddScoped<GameHistory>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<Analysis>();
builder.Services.AddScoped<Openings>();
builder.Services.AddScoped<BotRatings>();
builder.Services.AddSingleton<StockfishPool>();

var host = builder.Build();
// Stockfish opponents run in background workers in the browser.
var pool = host.Services.GetRequiredService<StockfishPool>();
RlChess.Players.PlayerRegistry.StockfishFactory = strength => new WebStockfishPlayer(pool, strength);
await host.RunAsync();

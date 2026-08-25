// Program.cs — startpunkten för hela Blazor-appen
// Allt som behöver registreras i appen görs här

using MinGramUI.Components;

var builder = WebApplication.CreateBuilder(args);

// Registrera Blazor — det är det som gör att .razor-filerna fungerar
// InteractiveServer betyder att C#-koden i komponenterna körs på servern,
// men UI:t uppdateras i realtid via WebSocket (SignalR)
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

// Registrera HttpClient med bas-URL från appsettings.json
// På det här sättet hårdkodar vi aldrig URL:er i koden
builder.Services.AddHttpClient("MinGramApi", client =>
{
    var apiUrl = builder.Configuration["ApiUrl"]
        ?? throw new InvalidOperationException("ApiUrl saknas i appsettings.json");
    client.BaseAddress = new Uri(apiUrl);
});

// v35 — CORS-notering:
// När den här Blazor-appen och ditt MinGram-API körs på olika domäner i Azure
// (t.ex. frontend på https://mingram-ui.azurewebsites.net och API på https://mingram-api.azurewebsites.net)
// måste API:t tillåta anrop från frontend-URL:en — annars blockerar webbläsaren svaren.
// Det kallas CORS (Cross-Origin Resource Sharing) och konfigureras på API:ts App Service i Azure.
// Vi går igenom det den här veckan — håll koll på var din frontend-URL hamnar!

var app = builder.Build();

app.UseStaticFiles();   // Tillåter filer i wwwroot/ (CSS, bilder)
app.UseAntiforgery();   // Skydd mot CSRF-attacker vid formulär

// Koppla Blazor-komponenterna till HTTP-pipelinen
// App är rotkomponenten som definierar routing och layout
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

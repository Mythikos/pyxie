using Pyxie.Data.DependencyInjection;
using Pyxie.Runtime.DependencyInjection;
using Pyxie.Web.Components;
using Pyxie.Web.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddPyxieDatabase(builder.Configuration);
builder.Services.AddPyxieData();
builder.Services.AddPyxieRuntime(builder.Configuration);
builder.Services.AddPyxieBlazorise(builder.Configuration);
PyxieMcpHosting.Register(builder.Services);

WebApplication app = builder.Build();

await app.OpenPyxieDatabasesAsync();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UsePyxieRouting();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapPyxieRepositoryArchive();
app.MapPyxieSettingsExport();
app.MapPyxieMedia();
PyxieMcpHosting.Map(app);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

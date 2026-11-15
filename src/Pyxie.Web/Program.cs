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

WebApplication app = builder.Build();

await app.OpenPyxieDatabasesAsync();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapPyxieRepositoryArchive();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();

using TemperatureController.Models;
using TemperatureController.Services;
using TemperatureController.Tuya;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ProcessStateManager>();
builder.Services.AddSingleton<HardwareService>();
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services.Configure<TuyaOptions>(builder.Configuration.GetSection("Tuya"));
builder.Services.AddHttpClient<ITuyaService, TuyaService>();
builder.Services.AddHostedService<ProcessMonitorService>();

builder.Services.AddScoped<IConfigFileService, ConfigFileService>();
builder.Services.AddScoped<ICalibrationService, CalibrationService>();

builder.Services.Configure<WeatherOptions>(builder.Configuration.GetSection("Weather"));
builder.Services.AddHttpClient<IWeatherService, OpenMeteoWeatherService>(httpClient =>
{
    httpClient.BaseAddress = new Uri("https://api.open-meteo.com");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

var hasHttpsEndpointConfigured = !string.IsNullOrWhiteSpace(app.Configuration["Kestrel:Endpoints:Https:Url"]);
if (hasHttpsEndpointConfigured)
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<DashboardHub>("/dashboardHub");
app.MapControllers();

app.Run();

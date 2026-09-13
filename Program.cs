using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TransportesGutierrez.Api.Configurations;
using TransportesGutierrez.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<ModuleAccessFilter>();
builder.Services.AddControllers(options => options.Filters.AddService<ModuleAccessFilter>());
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDataProtection()
    .SetApplicationName("TransportesGutierrez.Api");

builder.Services.AddOptions<SupabaseOptions>()
    .Bind(builder.Configuration.GetSection(SupabaseOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => Uri.TryCreate(o.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
        "Supabase:Url debe ser una URL HTTPS absoluta.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Key),
        "Configure Supabase:Key mediante la variable de entorno Supabase__Key.")
    .ValidateOnStart();

builder.Services.AddScoped<XmlGeneratorService>();
builder.Services.AddScoped<SupabaseService>();
builder.Services.AddSingleton<AppSessionService>();
builder.Services.AddHttpClient<EmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.brevo.com/");
    client.Timeout = TimeSpan.FromSeconds(45);
});
if (builder.Configuration.GetValue("PdfRetention:Enabled", !builder.Environment.IsDevelopment()))
{
    builder.Services.AddHostedService<PdfRetentionService>();
}

builder.Services.AddHttpClient<RndcClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient("Supabase", (services, client) =>
{
    var options = services.GetRequiredService<IOptions<SupabaseOptions>>().Value;
    client.BaseAddress = new Uri(options.Url.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

System.Text.Encoding.RegisterProvider(
    System.Text.CodePagesEncodingProvider.Instance);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseExceptionHandler("/error");
app.MapGet("/health", () => Results.Ok(new { status = "ok", revision = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") }));
app.MapControllers();
app.Run();

public partial class Program;

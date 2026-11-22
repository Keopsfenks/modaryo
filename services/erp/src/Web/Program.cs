using ERP.Infrastructure.Data;
using Modaryo.Sessions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

// Proxy (Next.js / gateway) arkasında gerçek istemci IP'si.
app.UseForwardedHeaders();

app.UseExceptionHandler(options => { });

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    await app.InitialiseDatabaseAsync();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    // Geliştirmede servis Next.js proxy'si arkasında http üzerinden çalışır.
    app.UseHttpsRedirection();
}

app.UseFileServer();

// GET dışındaki her istekte "X-Modaryo-Csrf: 1" zorunlu (spec 6.8). CORS bilerek kapalı.
// Oturumu kimlik servisi açar; ERP yalnızca Valkey'den okur (Modaryo.Sessions).
app.UseModaryoCsrf();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();

app.Map("/", () => Results.Redirect("/scalar"));

app.MapDefaultEndpoints();
app.MapEndpoints(typeof(Program).Assembly);

app.Run();

public partial class Program;

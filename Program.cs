using AdGroupUserCompare.Models;
using AdGroupUserCompare.Options;
using AdGroupUserCompare.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LdapOptions>(builder.Configuration.GetSection("Ad"));
builder.Services.PostConfigure<LdapOptions>(options =>
{
    ApplyEnvironmentFallback(options, option => option.Server, value => options.Server = value, "AD_LDAP_SERVER");
    ApplyEnvironmentFallback(options, option => option.SearchBase, value => options.SearchBase = value, "AD_SEARCH_BASE");
    ApplyEnvironmentFallback(options, option => option.BindDn, value => options.BindDn = value, "AD_BIND_DN");
    ApplyEnvironmentFallback(options, option => option.BindPassword, value => options.BindPassword = value, "AD_BIND_PASSWORD");

    if (bool.TryParse(Environment.GetEnvironmentVariable("AD_USE_SSL"), out var useSsl))
    {
        options.UseSsl = useSsl;
    }

    if (int.TryParse(Environment.GetEnvironmentVariable("AD_LDAP_PORT"), out var port))
    {
        options.Port = port;
    }
});

builder.Services.AddSingleton<ResultComparisonService>();
builder.Services.AddScoped<IAdGroupLookupService, LdapAdGroupLookupService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/config", (IOptions<LdapOptions> options) =>
{
    var value = options.Value;
    return new AppConfigResponse(
        value.Server,
        value.SearchBase,
        value.UseSsl,
        !string.IsNullOrWhiteSpace(value.BindDn));
});

app.MapPost("/api/search", async (
    [FromBody] AdSearchRequest request,
    IAdGroupLookupService lookup,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await lookup.SearchAsync(request, cancellationToken));
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.DirectoryServices.Protocols.DirectoryException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("Search").LogError(ex, "LDAP search failed.");
        return Results.Json(new { error = "LDAP-Suche fehlgeschlagen. Bitte Server, SearchBase und Bind-Daten pruefen." }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/users", ([FromBody] IReadOnlyList<AdUserResult> results, ResultComparisonService comparison) =>
{
    return Results.Ok(comparison.BuildUserOptions(results));
});

app.MapPost("/api/compare", ([FromBody] CompareUsersRequest request, ResultComparisonService comparison) =>
{
    try
    {
        return Results.Ok(comparison.Compare(request));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();

static void ApplyEnvironmentFallback(
    LdapOptions options,
    Func<LdapOptions, string> getValue,
    Action<string> setValue,
    string environmentName)
{
    if (!string.IsNullOrWhiteSpace(getValue(options)))
    {
        return;
    }

    var value = Environment.GetEnvironmentVariable(environmentName);
    if (!string.IsNullOrWhiteSpace(value))
    {
        setValue(value);
    }
}

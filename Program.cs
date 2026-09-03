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
    ApplyEnvironmentFallback(options, option => option.DefaultGroupPattern, value => options.DefaultGroupPattern = value, "AD_GROUP_PATTERN");
    ApplyEnvironmentFallback(options, option => option.BindDn, value => options.BindDn = value, "AD_BIND_DN");
    ApplyEnvironmentFallback(options, option => option.BindPassword, value => options.BindPassword = value, "AD_BIND_PASSWORD");
    ApplyEnvironmentFallback(options, option => option.SettingsPath, value => options.SettingsPath = value, "AD_SETTINGS_PATH");

    if (bool.TryParse(Environment.GetEnvironmentVariable("AD_USE_SSL"), out var useSsl))
    {
        options.UseSsl = useSsl;
    }

    if (bool.TryParse(Environment.GetEnvironmentVariable("AD_USE_START_TLS"), out var useStartTls))
    {
        options.UseStartTls = useStartTls;
    }

    if (bool.TryParse(Environment.GetEnvironmentVariable("AD_VERIFY_CERTIFICATE"), out var verifyCertificate))
    {
        options.VerifyCertificate = verifyCertificate;
    }

    if (bool.TryParse(Environment.GetEnvironmentVariable("AD_USE_PAGING"), out var usePaging))
    {
        options.UsePaging = usePaging;
    }

    if (int.TryParse(Environment.GetEnvironmentVariable("AD_LDAP_PORT"), out var port))
    {
        options.Port = port;
    }
});

builder.Services.AddSingleton<ResultComparisonService>();
builder.Services.AddSingleton<LdapSettingsStore>();
builder.Services.AddScoped<IAdGroupLookupService, LdapAdGroupLookupService>();
builder.Services.AddScoped<ILdapDiagnosticService, LdapDiagnosticService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/config", (LdapSettingsStore settings) =>
{
    return settings.BuildResponse();
});

app.MapPost("/api/config", async (
    [FromBody] LdapSettingsRequest request,
    LdapSettingsStore settings,
    CancellationToken cancellationToken) =>
{
    if (request.UseSsl && request.UseStartTls)
    {
        return Results.BadRequest(new { error = "SSL und StartTLS duerfen nicht gleichzeitig aktiv sein." });
    }

    if (request.Port is < 1 or > 65535)
    {
        return Results.BadRequest(new { error = "LDAP-Port muss zwischen 1 und 65535 liegen." });
    }

    await settings.SaveAsync(request, cancellationToken);
    return Results.Ok(settings.BuildResponse());
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
        return Results.BadRequest(new { error = ex is InvalidOperationException ? ex.Message : LdapExceptionFormatter.FriendlyMessage(ex) });
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("Search").LogError(ex, "LDAP search failed.");
        return Results.Json(new { error = "LDAP-Suche fehlgeschlagen. Bitte Server, SearchBase und Bind-Daten pruefen." }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/test-ldap", async (
    [FromBody] LdapTestRequest request,
    ILdapDiagnosticService diagnostic,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await diagnostic.TestAsync(request, cancellationToken));
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("LdapDiagnostic").LogError(ex, "LDAP diagnostic failed.");
        return Results.Ok(new LdapTestResponse(
            false,
            request.Server ?? "",
            0,
            false,
            false,
            request.VerifyCertificate ?? true,
            request.SearchBase ?? "",
            false,
            "",
            false,
            false,
            [new LdapTestStep("Test", false, "LDAP-Test konnte nicht ausgefuehrt werden.", $"{ex.GetType().Name}: {ex.Message}")]));
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

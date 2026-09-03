using System.Text.Json;
using AdGroupUserCompare.Models;
using AdGroupUserCompare.Options;
using Microsoft.Extensions.Options;

namespace AdGroupUserCompare.Services;

public sealed class LdapSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly LdapOptions _defaults;
    private readonly string _settingsPath;
    private SavedLdapSettings? _saved;
    private LdapOptions _current;

    public LdapSettingsStore(IOptions<LdapOptions> options)
    {
        _defaults = Clone(options.Value);
        _settingsPath = FirstNonEmpty(_defaults.SettingsPath, Path.Combine(AppContext.BaseDirectory, "data", "ad-settings.json"));
        _defaults.SettingsPath = _settingsPath;
        _saved = LoadSavedSettings();
        _current = Merge(_defaults, _saved);
    }

    public LdapOptions Current
    {
        get
        {
            lock (_sync)
            {
                return Clone(_current);
            }
        }
    }

    public async Task<LdapOptions> SaveAsync(LdapSettingsRequest request, CancellationToken cancellationToken)
    {
        var endpoint = LdapEndpointResolver.Resolve(request.Server, request.Port, request.UseSsl, request.UseStartTls);
        var saved = new SavedLdapSettings
        {
            Server = endpoint.Server,
            Port = endpoint.Port,
            UseSsl = endpoint.UseSsl,
            UseStartTls = endpoint.UseStartTls,
            VerifyCertificate = request.VerifyCertificate,
            SearchBase = request.SearchBase?.Trim() ?? "",
            DefaultGroupPattern = request.GroupPattern?.Trim() ?? "",
            BindDn = request.BindDn?.Trim() ?? "",
            BindPassword = ResolveSavedPassword(request),
            UsePaging = request.UsePaging
        };

        var json = JsonSerializer.Serialize(saved, SerializerOptions);
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, _settingsPath, overwrite: true);

        lock (_sync)
        {
            _saved = saved;
            _current = Merge(_defaults, _saved);
            return Clone(_current);
        }
    }

    public AppConfigResponse BuildResponse()
    {
        var value = Current;
        LdapEndpoint endpoint;
        try
        {
            endpoint = LdapEndpointResolver.Resolve(value);
        }
        catch (InvalidOperationException)
        {
            endpoint = new LdapEndpoint(value.Server, value.Port, value.UseSsl, value.UseStartTls);
        }

        return new AppConfigResponse(
            endpoint.Server,
            endpoint.Port,
            endpoint.UseSsl,
            endpoint.UseStartTls,
            value.VerifyCertificate,
            value.SearchBase,
            value.DefaultGroupPattern,
            !string.IsNullOrWhiteSpace(value.BindDn),
            value.BindDn,
            !string.IsNullOrEmpty(value.BindPassword),
            value.UsePaging,
            File.Exists(_settingsPath));
    }

    private string? ResolveSavedPassword(LdapSettingsRequest request)
    {
        if (request.ClearBindPassword)
        {
            return "";
        }

        if (!string.IsNullOrEmpty(request.BindPassword))
        {
            return request.BindPassword;
        }

        lock (_sync)
        {
            return _saved?.BindPassword;
        }
    }

    private SavedLdapSettings? LoadSavedSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<SavedLdapSettings>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static LdapOptions Merge(LdapOptions defaults, SavedLdapSettings? saved)
    {
        var current = Clone(defaults);
        if (saved is null)
        {
            return current;
        }

        current.Server = saved.Server ?? "";
        current.Port = NormalizePort(saved.Port);
        current.UseSsl = saved.UseSsl;
        current.UseStartTls = saved.UseStartTls;
        current.VerifyCertificate = saved.VerifyCertificate;
        current.SearchBase = saved.SearchBase ?? "";
        current.DefaultGroupPattern = saved.DefaultGroupPattern ?? "";
        current.BindDn = saved.BindDn ?? "";
        if (saved.BindPassword is not null)
        {
            current.BindPassword = saved.BindPassword;
        }
        current.UsePaging = saved.UsePaging;
        return current;
    }

    public static LdapOptions Clone(LdapOptions value)
    {
        return new LdapOptions
        {
            Server = value.Server,
            Port = value.Port,
            UseSsl = value.UseSsl,
            UseStartTls = value.UseStartTls,
            VerifyCertificate = value.VerifyCertificate,
            SearchBase = value.SearchBase,
            DefaultGroupPattern = value.DefaultGroupPattern,
            BindDn = value.BindDn,
            BindPassword = value.BindPassword,
            UsePaging = value.UsePaging,
            PageSize = value.PageSize,
            MemberRangeSize = value.MemberRangeSize,
            SettingsPath = value.SettingsPath
        };
    }

    private static int NormalizePort(int port)
    {
        return port is >= 1 and <= 65535 ? port : 636;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private sealed class SavedLdapSettings
    {
        public string? Server { get; set; }

        public int Port { get; set; } = 636;

        public bool UseSsl { get; set; } = true;

        public bool UseStartTls { get; set; }

        public bool VerifyCertificate { get; set; } = true;

        public string? SearchBase { get; set; }

        public string? DefaultGroupPattern { get; set; }

        public string? BindDn { get; set; }

        public string? BindPassword { get; set; }

        public bool UsePaging { get; set; } = true;
    }
}

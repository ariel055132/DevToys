using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevToys.Blazor.Core.Languages;
using DevToys.Core.Settings;
using DevToys.Core.Tools;

namespace DevToys.Blazor.BuiltInTools.Settings;

/// <summary>
/// Transfers an explicit set of portable preferences, never arbitrary storage entries or tool input.
/// </summary>
[Export(typeof(PreferencesTransferService))]
internal sealed class PreferencesTransferService
{
    internal const int MaximumFileSize = 1024 * 1024;
    private const string FileFormat = "DevToys.Preferences";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly ISettingsProvider _settings;
    private readonly GuiToolProvider _tools;
    private readonly SmartDetectionService _smartDetection;
    private readonly Dictionary<string, Preference> _preferences;

    [ImportingConstructor]
    public PreferencesTransferService(ISettingsProvider settings, GuiToolProvider tools, SmartDetectionService smartDetection)
    {
        _settings = settings;
        _tools = tools;
        _smartDetection = smartDetection;
        Preference[] preferences =
        {
            Create(nameof(PredefinedSettings.Language), PredefinedSettings.Language, value => LanguageManager.Instance.AvailableLanguages.Any(language => language.InternalName == value)),
            Create(nameof(PredefinedSettings.Theme), PredefinedSettings.Theme, Enum.IsDefined),
            Create(nameof(PredefinedSettings.FavoriteToolOrder), PredefinedSettings.FavoriteToolOrder,
                names => names.All(name => !string.IsNullOrWhiteSpace(name)) && names.Distinct(StringComparer.Ordinal).Count() == names.Length),
            Create(nameof(PredefinedSettings.CompactMode), PredefinedSettings.CompactMode),
            Create(nameof(PredefinedSettings.CheckForUpdate), PredefinedSettings.CheckForUpdate),
            Create(nameof(PredefinedSettings.ShowMostRecentTools), PredefinedSettings.ShowMostRecentTools),
            Create(nameof(PredefinedSettings.SmartDetection), PredefinedSettings.SmartDetection),
            Create(nameof(PredefinedSettings.SmartDetectionPaste), PredefinedSettings.SmartDetectionPaste),
            Create(nameof(PredefinedSettings.TextEditorFont), PredefinedSettings.TextEditorFont, value => value is not null && value.Length <= 256 && !value.Any(char.IsControl)),
            Create(nameof(PredefinedSettings.TextEditorTextWrapping), PredefinedSettings.TextEditorTextWrapping),
            Create(nameof(PredefinedSettings.TextEditorLineNumbers), PredefinedSettings.TextEditorLineNumbers),
            Create(nameof(PredefinedSettings.TextEditorHighlightCurrentLine), PredefinedSettings.TextEditorHighlightCurrentLine),
            Create(nameof(PredefinedSettings.TextEditorRenderWhitespace), PredefinedSettings.TextEditorRenderWhitespace),
            Create(nameof(PredefinedSettings.TextEditorEndOfLinePreference), PredefinedSettings.TextEditorEndOfLinePreference, Enum.IsDefined),
            Create(nameof(PredefinedSettings.TextEditorPasteClearsText), PredefinedSettings.TextEditorPasteClearsText)
        };
        _preferences = preferences.ToDictionary(preference => preference.Name, StringComparer.Ordinal);
    }

    public string Export()
    {
        var toolPreferences = new SortedDictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        foreach (GuiToolInstance tool in _tools.AllTools)
        {
            var values = new Dictionary<string, object>();
            if (!tool.NotFavorable)
            {
                values.Add("isFavorite", _tools.GetToolIsFavorite(tool));
            }
            if (!tool.NotSearchable)
            {
                values.Add("searchAliases", _tools.GetToolSearchAliases(tool));
            }
            if (tool.AcceptedDataTypeNames.Count > 0)
            {
                values.Add("smartDetectionEnabled", _smartDetection.GetToolEnabled(tool));
            }
            if (values.Count > 0)
            {
                toolPreferences.Add(tool.InternalComponentName, values);
            }
        }

        string json = JsonSerializer.Serialize(new
        {
            format = FileFormat,
            version = 1,
            settings = _preferences.ToDictionary(pair => pair.Key, pair => pair.Value.Read()),
            tools = toolPreferences
        }, JsonOptions);
        ValidateSize(Encoding.UTF8.GetByteCount(json));
        return json;
    }

    public async Task<PreferencesImport> ReadImportAsync(Stream source)
    {
        // Bound reads even for non-seekable sandbox streams and files that grow while being read.
        using var content = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            ValidateSize(content.Length + read);
            content.Write(buffer, 0, read);
        }
        content.Position = 0;
        using var reader = new StreamReader(content, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        try
        {
            return PrepareImport(await reader.ReadToEndAsync());
        }
        catch (DecoderFallbackException exception)
        {
            throw new FormatException("Invalid preferences encoding.", exception);
        }
    }

    public PreferencesImport PrepareImport(string json)
    {
        ValidateSize(Encoding.UTF8.GetByteCount(json));
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            Dictionary<string, JsonElement> root = ReadObject(document.RootElement);
            if (root.Count != 4
                || !root.TryGetValue("format", out JsonElement format) || format.GetString() != FileFormat
                || !root.TryGetValue("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 1
                || !root.TryGetValue("settings", out JsonElement settings)
                || !root.TryGetValue("tools", out JsonElement tools))
            {
                throw new FormatException("Unsupported preferences format or version.");
            }

            var changes = new List<PreferenceChange>();
            foreach ((string name, JsonElement value) in ReadObject(settings))
            {
                if (!_preferences.TryGetValue(name, out Preference? preference))
                {
                    throw new FormatException("Unknown preference.");
                }
                changes.Add(preference.Prepare(value));
            }

            var importedTools = ReadObject(tools);
            var installedTools = _tools.AllTools.ToDictionary(tool => tool.InternalComponentName, StringComparer.Ordinal);
            string[] missingTools = importedTools.Keys.Where(name => !installedTools.ContainsKey(name)).ToArray();
            if (missingTools.Length > 0)
            {
                throw new MissingPreferenceToolsException(missingTools);
            }

            foreach ((string name, JsonElement toolValues) in importedTools)
            {
                GuiToolInstance tool = installedTools[name];
                foreach ((string key, JsonElement value) in ReadObject(toolValues))
                {
                    Preference preference = key switch
                    {
                        "isFavorite" when !tool.NotFavorable => CreateFavoritePreference(tool),
                        "searchAliases" when !tool.NotSearchable => Create(
                            key, () => _tools.GetToolSearchAliases(tool).ToArray(),
                            aliases => _tools.TrySetToolSearchAliases(tool, aliases), ValidAliases),
                        "smartDetectionEnabled" when tool.AcceptedDataTypeNames.Count > 0 => Create(
                            key, () => _smartDetection.GetToolEnabled(tool), enabled => _smartDetection.SetToolEnabled(tool, enabled)),
                        _ => throw new FormatException("Unknown or unsupported tool preference.")
                    };
                    changes.Add(preference.Prepare(value));
                }
            }
            if (changes.Count == 0)
            {
                throw new FormatException("No preferences to import.");
            }
            return new PreferencesImport(changes);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new FormatException("Invalid preferences file.", exception);
        }
    }

    private Preference CreateFavoritePreference(GuiToolInstance tool)
    {
        Preference preference = Create("isFavorite", () => _tools.GetToolIsFavorite(tool),
            favorite => _tools.SetToolIsFavorite(tool, favorite));
        return preference with
        {
            Prepare = element =>
            {
                PreferenceChange change = preference.Prepare(element);
                return change with
                {
                    CaptureRestore = () =>
                    {
                        Action restoreFavorite = change.CaptureRestore();
                        string[] order = _settings.GetSetting(PredefinedSettings.FavoriteToolOrder);
                        return () =>
                        {
                            restoreFavorite();
                            _settings.SetSetting(PredefinedSettings.FavoriteToolOrder, order);
                        };
                    }
                };
            }
        };
    }

    private Preference Create<T>(string name, SettingDefinition<T> definition, Func<T, bool>? validate = null)
        => Create(name, () => _settings.GetSetting(definition), value => _settings.SetSetting(definition, value), validate);

    private static Preference Create<T>(string name, Func<T> read, Action<T> write, Func<T, bool>? validate = null)
        => new(name, () => JsonSerializer.SerializeToElement(read(), JsonOptions), element =>
        {
            T? value = element.Deserialize<T>(JsonOptions);
            if (value is null || (validate is not null && !validate(value)))
            {
                throw new FormatException("Invalid preference value.");
            }
            return new PreferenceChange(() => write(value), () =>
            {
                T previous = read();
                return () => write(previous);
            });
        });

    private static bool ValidAliases(string[] aliases)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return aliases.All(alias => !string.IsNullOrWhiteSpace(alias) && unique.Add(alias.Trim()));
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement element)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || !result.TryAdd(property.Name, property.Value))
            {
                throw new FormatException("Duplicate or empty preference name.");
            }
        }
        return result;
    }

    private static void ValidateSize(long size)
    {
        if (size > MaximumFileSize)
        {
            throw new FormatException("Preferences files must not exceed 1 MiB.");
        }
    }

    private sealed record Preference(string Name, Func<JsonElement> Read, Func<JsonElement, PreferenceChange> Prepare);
}

internal sealed class MissingPreferenceToolsException(string[] toolNames) : Exception("Install the missing tools before importing preferences.")
{
    public IReadOnlyList<string> ToolNames { get; } = toolNames;
}

internal sealed record PreferenceChange(Action Apply, Func<Action> CaptureRestore);

/// <summary>
/// A fully validated import. Validation and the confirmation dialog never write preferences.
/// </summary>
internal sealed class PreferencesImport(IReadOnlyList<PreferenceChange> changes)
{
    public int Count => changes.Count;

    public void Apply()
    {
        Action[] restore = changes.Select(change => change.CaptureRestore()).ToArray();
        int index = 0;
        try
        {
            for (; index < changes.Count; index++)
            {
                changes[index].Apply();
            }
        }
        catch (Exception importError)
        {
            // Include the failing write: a storage provider or change listener may throw after saving.
            var errors = new List<Exception> { importError };
            for (int rollback = Math.Min(index, restore.Length - 1); rollback >= 0; rollback--)
            {
                try
                {
                    restore[rollback]();
                }
                catch (Exception restoreError)
                {
                    errors.Add(restoreError);
                }
            }
            throw new AggregateException("Preferences could not be imported.", errors);
        }
    }
}

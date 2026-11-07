using System.Text;
using DevToys.Blazor.Core.Languages;
using DevToys.Core.Settings;
using Microsoft.Extensions.Logging;

namespace DevToys.Blazor.BuiltInTools.Settings;

internal sealed partial class SettingsGuiTool
{
    private readonly PreferencesTransferService _preferencesTransfer;
    private readonly IUIButton _exportPreferences = Button("export-preferences");
    private readonly IUIButton _importPreferences = Button("import-preferences");
    private readonly IUIInfoBar _preferencesStatus = InfoBar("preferences-transfer-status");
    private UIToolView? _view;
    private bool _transferringPreferences;

    private async ValueTask ExportPreferencesAsync()
    {
        await TransferPreferencesAsync(async () =>
        {
            // Prepare the entire file before a save picker can truncate an existing backup.
            string json = _preferencesTransfer.Export();
            using FileStream? destination = await _fileStorage.PickSaveFileWithSuggestedNameAsync("DevToys-preferences.json", "*.json");
            if (destination is null)
            {
                return;
            }
            destination.SetLength(0);
            using (var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true))
            {
                await writer.WriteAsync(json);
                await writer.FlushAsync();
            }
            _preferencesStatus.Success().Title(Settings.PreferencesExported).Description(null).Open();
        });
    }

    private async ValueTask ImportPreferencesAsync()
    {
        await TransferPreferencesAsync(async () =>
        {
            PreferencesImport import;
            string fileName;
            using (SandboxedFileReader? file = await _fileStorage.PickOpenFileAsync("*.json"))
            {
                if (file is null)
                {
                    return;
                }
                fileName = file.FileName;
                using Stream source = await file.GetNewAccessToFileContentAsync(CancellationToken.None);
                import = await _preferencesTransfer.ReadImportAsync(source);
            }

            bool confirmed = false;
            UIDialog dialog = await View.OpenDialogAsync(
                Stack().Vertical().WithChildren(
                    Label().Style(UILabelStyle.Subtitle).Text(Settings.ImportPreferences),
                    Label().Text(string.Format(Settings.PreferencesImportConfirmation, fileName, import.Count)).WrapIfNeeded()),
                footerContent: Stack().Horizontal().AlignHorizontally(UIHorizontalAlignment.Right).WithChildren(
                    Button("cancel-preferences-import").Text(Settings.PreferencesCancel).OnClick(() => View.CurrentOpenedDialog?.Close()),
                    Button("confirm-preferences-import").Text(Settings.ImportPreferences).AccentAppearance().OnClick(() =>
                    {
                        confirmed = true;
                        View.CurrentOpenedDialog?.Close();
                    })),
                isDismissible: true);
            await dialog.DialogCloseAwaiter;
            if (!confirmed)
            {
                return;
            }

            import.Apply();
            RefreshImportedPreferences();
            _preferencesStatus.Success().Title(Settings.PreferencesImported).Description(Settings.PreferencesRestart).Open();
        });
    }

    private async ValueTask TransferPreferencesAsync(Func<Task> operation)
    {
        if (_transferringPreferences)
        {
            return;
        }
        _transferringPreferences = true;
        _exportPreferences.Disable();
        _importPreferences.Disable();
        _preferencesStatus.Close();
        try
        {
            await operation();
        }
        catch (MissingPreferenceToolsException exception)
        {
            _preferencesStatus.Error().Title(Settings.PreferencesMissingTools)
                .Description(string.Join(", ", exception.ToolNames)).Open();
        }
        catch (FormatException)
        {
            _preferencesStatus.Error().Title(Settings.PreferencesInvalidFile).Description(null).Open();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or AggregateException)
        {
            this.Log().LogError(exception, "Unable to transfer preferences.");
            _preferencesStatus.Error().Title(Settings.PreferencesTransferFailed).Description(null).Open();
        }
        finally
        {
            _transferringPreferences = false;
            _exportPreferences.Enable();
            _importPreferences.Enable();
        }
    }

    private void RefreshImportedPreferences()
    {
        var languages = (IUISelectDropDownList)View.GetChildElementById("language-select-drop-down-list")!;
        string languageName = _settingsProvider.GetSetting(PredefinedSettings.Language);
        languages.Select(_availableLanguages.Single(item => ((LanguageDefinition)item.Value!).InternalName == languageName));

        // A font name remains portable even when the font has not been installed on this machine.
        string font = _settingsProvider.GetSetting(PredefinedSettings.TextEditorFont);
        var fonts = (IUISelectDropDownList)_textEditorFontSetting.InteractiveElement!;
        if (!fonts.Items!.Any(item => Equals(item.Value, font)))
        {
            fonts.WithItems(fonts.Items!.Append(Item(font, font)).ToArray());
        }
        fonts.Select(fonts.Items!.Single(item => Equals(item.Value, font)));
        OnTextEditorFontChanged(font);
        OnTextEditorEndOfLinePreferenceChanged(_settingsProvider.GetSetting(PredefinedSettings.TextEditorEndOfLinePreference));
        OnSmartDetectionOptionChanged(_settingsProvider.GetSetting(PredefinedSettings.SmartDetection));
    }
}

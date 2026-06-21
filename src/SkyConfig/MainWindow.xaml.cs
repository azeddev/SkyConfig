using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SkyConfig.Core;

namespace SkyConfig.App;

public partial class MainWindow : Window
{
    private SkylanderDump? _dump;
    private string? _currentPath;
    private bool _suppressEvents;
    private bool _controlsDirty;
    private bool _modelDirty;
    private UpgradeState _upgradeState = UpgradeState.Empty;

    public MainWindow()
    {
        InitializeComponent();
        FigureCombo.ItemsSource = FigureCatalog.Figures;
        LevelCombo.ItemsSource = Enumerable.Range(1, 20);
        UpgradePathCombo.ItemsSource = Enum.GetValues<UpgradePath>();
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(ControlChanged));
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    public void OpenInitialPath(string path) => OpenPath(path);

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSaveOrDiscard())
            return;

        var dialog = new NewFigureDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SkylanderDump dump = SkylanderDump.Create(dialog.FigureId, dialog.VariantId);
            LoadDump(dump, null, $"New {dump.Definition?.Name ?? "figure"}.sky");
            _modelDirty = true;
            UpdateTitle();
            SetStatus("New dump created in memory. Save it to choose a location.");
        }
        catch (Exception exception)
        {
            ShowError("Could not create the dump.", exception);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSaveOrDiscard())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Open Skylander dump",
            Filter = "Skylander dump (*.sky;*.bin;*.dmp;*.dump)|*.sky;*.bin;*.dmp;*.dump|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            OpenPath(dialog.FileName);
    }

    private void OpenPath(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            LoadDump(SkylanderDump.Load(bytes), Path.GetFullPath(path), Path.GetFileName(path));
            SetStatus("File loaded. Values are decrypted locally; the file has not been changed.");
        }
        catch (Exception exception)
        {
            ShowError("Could not open the Skylander dump.", exception);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent(false);

    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveCurrent(true);

    private void CloseFile_Click(object sender, RoutedEventArgs e)
    {
        if (_dump is null || !ConfirmSaveOrDiscard())
            return;

        UnloadDump();
    }

    private bool SaveCurrent(bool forceSaveAs)
    {
        if (_dump is null || !TryApplyControls())
            return false;

        string? target = forceSaveAs || _currentPath is null ? ChooseSavePath() : _currentPath;
        if (target is null)
            return false;

        try
        {
            byte[] bytes = _dump.ToEncryptedBytes();
            WriteAtomically(target, bytes);
            LoadDump(SkylanderDump.Load(bytes), Path.GetFullPath(target), Path.GetFileName(target));
            SetStatus($"Saved {Path.GetFileName(target)}. Existing files are backed up with a .bak extension.");
            return true;
        }
        catch (Exception exception)
        {
            ShowError("Could not save the Skylander dump. Close it in RPCS3 if the file is currently loaded on the portal.", exception);
            return false;
        }
    }

    private string? ChooseSavePath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Skylander dump",
            Filter = "Skylander dump (*.sky)|*.sky|All files (*.*)|*.*",
            DefaultExt = ".sky",
            AddExtension = true,
            FileName = _currentPath is null ? FileNameText.Text : Path.GetFileName(_currentPath)
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temporary, bytes);

        try
        {
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, fullPath + ".bak", true);
            else
                File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (TryApplyControls())
            SetStatus("Changes applied in memory. Use Save to write the encrypted dump.");
    }

    private bool TryApplyControls()
    {
        if (_dump is null)
            return false;

        try
        {
            ushort id = ParseUInt16(IdBox.Text, "ID", false);
            ushort variant = ParseUInt16(VariantBox.Text, "Variant", true);
            bool wasCharacter = _dump.SupportsCharacterData;
            bool identityChanged = id != _dump.CharacterId || variant != _dump.VariantId;
            CharacterData? currentCharacter = wasCharacter ? _dump.ReadCharacterData() : null;
            CharacterData? requestedCharacter = currentCharacter is null ? null : ReadCharacterControls(currentCharacter);
            bool characterChanged = requestedCharacter is not null && requestedCharacter != currentCharacter;

            if (!identityChanged && !characterChanged)
            {
                _controlsDirty = false;
                UpdateTitle();
                return true;
            }

            if (characterChanged)
                _dump.ApplyCharacterData(requestedCharacter!);

            if (identityChanged)
                _dump.ApplyIdentity(id, variant);

            if (!wasCharacter && _dump.SupportsCharacterData)
                _dump.ApplyCharacterData(_dump.ReadCharacterData());

            _modelDirty = true;
            _controlsDirty = false;
            RefreshControls();
            UpdateTitle();
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private CharacterData ReadCharacterControls(CharacterData current)
    {
        int experience = ParseInt32(ExperienceBox.Text, "Experience", 0, SkylanderDump.MaxExperience);
        ushort gold = ParseUInt16(GoldBox.Text, "Gold", false);
        if (gold > SkylanderDump.MaxGold)
            throw new ArgumentOutOfRangeException(nameof(gold), $"Gold must be between 0 and {SkylanderDump.MaxGold}.");

        uint playTime = ParseUInt32(PlayTimeBox.Text, "Play time", false);
        ushort heroRank = ParseUInt16(HeroRankBox.Text, "Hero rank", false);
        uint flags = ParseUInt32(HeroicFlagsBox.Text, "Heroic flags", true);

        return current with
        {
            Experience = experience,
            Level = SkylanderDump.LevelFromExperience(experience),
            Gold = gold,
            PlayTime = playTime,
            HeroRank = heroRank,
            Nickname = NicknameBox.Text,
            HeroicChallengeFlags = flags,
            Upgrades = _upgradeState with
            {
                Path = UpgradePathCombo.SelectedItem is UpgradePath path ? path : UpgradePath.None
            },
            SpyrosAdventureHat = ParseByte(SsaHatBox.Text, "Spyro's Adventure hat"),
            GiantsHat = ParseByte(GiantsHatBox.Text, "Giants hat"),
            SwapForceOrTrapTeamHat = ParseByte(SwapHatBox.Text, "Swap Force / Trap Team hat"),
            SuperChargersHat = ParseByte(SuperHatBox.Text, "SuperChargers hat")
        };
    }

    private void LoadDump(SkylanderDump dump, string? path, string displayName)
    {
        _dump = dump;
        _currentPath = path;
        FileNameText.Text = displayName;
        FilePathText.Text = path ?? "Not saved";
        _modelDirty = false;
        _controlsDirty = false;
        RefreshControls();

        IdentityGroup.IsEnabled = true;
        CloseButton.IsEnabled = true;
        CloseMenuItem.IsEnabled = true;
        ApplyButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
        SaveMenuItem.IsEnabled = true;
        SaveAsMenuItem.IsEnabled = true;
        RevertMenuItem.IsEnabled = path is not null;
        UpdateTitle();
    }

    private void UnloadDump()
    {
        _suppressEvents = true;
        try
        {
            _dump = null;
            _currentPath = null;
            _controlsDirty = false;
            _modelDirty = false;
            _upgradeState = UpgradeState.Empty;

            FileNameText.Text = "None";
            FilePathText.Text = "-";
            SerialText.Text = "-";
            FigureCombo.SelectedItem = null;
            FigureCombo.Text = string.Empty;
            IdBox.Text = string.Empty;
            VariantBox.Text = string.Empty;
            ClassificationText.Text = "-";
            ClearCharacterControls();

            IdentityGroup.IsEnabled = false;
            CharacterGroup.IsEnabled = false;
            CloseButton.IsEnabled = false;
            CloseMenuItem.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            SaveMenuItem.IsEnabled = false;
            SaveAsMenuItem.IsEnabled = false;
            RevertMenuItem.IsEnabled = false;

            HeaderStatusText.Text = "Header: -";
            HeaderStatusText.Foreground = Brushes.Black;
            AreaAStatusText.Text = "Area A: -";
            AreaAStatusText.Foreground = Brushes.Black;
            AreaBStatusText.Text = "Area B: -";
            AreaBStatusText.Foreground = Brushes.Black;
            ActiveAreaText.Text = "Active area: -";
            HexText.Clear();
            SetStatus("File closed. Open a .sky file or create a new one.");
            UpdateTitle();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void RefreshControls()
    {
        if (_dump is null)
            return;

        _suppressEvents = true;
        try
        {
            FigureDefinition? definition = _dump.Definition;
            FigureCombo.SelectedItem = definition;
            FigureCombo.Text = definition?.DisplayName ?? $"Unknown [{_dump.CharacterId}, 0x{_dump.VariantId:X4}]";
            IdBox.Text = _dump.CharacterId.ToString(CultureInfo.InvariantCulture);
            VariantBox.Text = $"0x{_dump.VariantId:X4}";
            ClassificationText.Text = definition is null
                ? "Unknown figure"
                : $"{FriendlyGame(definition.Game)} / {definition.Element} / {definition.Type}";
            SerialText.Text = $"0x{_dump.Serial:X8}";

            CharacterGroup.IsEnabled = _dump.SupportsCharacterData;
            if (_dump.SupportsCharacterData)
                PopulateCharacterControls(_dump.ReadCharacterData());
            else
                ClearCharacterControls();

            RefreshDiagnostics();
            RefreshHex();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void PopulateCharacterControls(CharacterData data)
    {
        NicknameBox.Text = data.Nickname;
        LevelCombo.SelectedItem = data.Level;
        ExperienceBox.Text = data.Experience.ToString(CultureInfo.InvariantCulture);
        GoldBox.Text = data.Gold.ToString(CultureInfo.InvariantCulture);
        PlayTimeBox.Text = data.PlayTime.ToString(CultureInfo.InvariantCulture);
        HeroRankBox.Text = data.HeroRank.ToString(CultureInfo.InvariantCulture);
        _upgradeState = data.Upgrades;
        UpgradePathCombo.SelectedItem = data.Upgrades.Path;
        HeroicFlagsBox.Text = $"0x{data.HeroicChallengeFlags:X8}";
        HeroicCountText.Text = $"{data.CompletedHeroicChallenges} of 32";
        SsaHatBox.Text = data.SpyrosAdventureHat.ToString(CultureInfo.InvariantCulture);
        GiantsHatBox.Text = data.GiantsHat.ToString(CultureInfo.InvariantCulture);
        SwapHatBox.Text = data.SwapForceOrTrapTeamHat.ToString(CultureInfo.InvariantCulture);
        SuperHatBox.Text = data.SuperChargersHat.ToString(CultureInfo.InvariantCulture);
    }

    private void ClearCharacterControls()
    {
        foreach (TextBox box in new[] { NicknameBox, ExperienceBox, GoldBox, PlayTimeBox, HeroRankBox, HeroicFlagsBox, SsaHatBox, GiantsHatBox, SwapHatBox, SuperHatBox })
            box.Text = string.Empty;
        LevelCombo.SelectedItem = null;
        UpgradePathCombo.SelectedItem = null;
        _upgradeState = UpgradeState.Empty;
        HeroicCountText.Text = "-";
    }

    private void RefreshDiagnostics()
    {
        DumpIntegrity integrity = _dump!.Integrity;
        HeaderStatusText.Text = $"Header: {PassFail(integrity.HeaderChecksum)}";
        HeaderStatusText.Foreground = StatusBrush(integrity.HeaderChecksum);
        SetAreaStatus(AreaAStatusText, integrity.AreaA);
        SetAreaStatus(AreaBStatusText, integrity.AreaB);
        ActiveAreaText.Text = _dump.SupportsCharacterData ? $"Active area: {(_dump.ActiveAreaIndex == 0 ? "A" : "B")}" : "Active area: not used by this figure type";
    }

    private static void SetAreaStatus(TextBlock target, AreaIntegrity area)
    {
        target.Text = $"Area {area.Name}: {area.ValidChecksumCount}/4 checks, counter {area.Counter} (extended {area.ExtendedCounter})";
        target.Foreground = area.CoreValid ? Brushes.DarkGreen : area.ValidChecksumCount == 0 ? Brushes.DimGray : Brushes.DarkGoldenrod;
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null || !ConfirmDiscardOnly())
            return;
        OpenPath(_currentPath);
    }

    private void FigureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || FigureCombo.SelectedItem is not FigureDefinition figure)
            return;
        IdBox.Text = figure.Id.ToString(CultureInfo.InvariantCulture);
        VariantBox.Text = $"0x{figure.Variant:X4}";
        ClassificationText.Text = $"{FriendlyGame(figure.Game)} / {figure.Element} / {figure.Type}";
        MarkControlsDirty();
    }

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || LevelCombo.SelectedItem is not int level)
            return;
        _suppressEvents = true;
        ExperienceBox.Text = SkylanderDump.ExperienceForLevel(level).ToString(CultureInfo.InvariantCulture);
        _suppressEvents = false;
        MarkControlsDirty();
    }

    private void ExperienceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || !int.TryParse(ExperienceBox.Text, out int experience) || experience < 0)
            return;
        int level = SkylanderDump.LevelFromExperience(experience);
        _suppressEvents = true;
        LevelCombo.SelectedItem = level;
        _suppressEvents = false;
    }

    private void HeroicFlagsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        try
        {
            uint flags = ParseUInt32(HeroicFlagsBox.Text, "Heroic flags", true);
            HeroicCountText.Text = $"{BitOperations.PopCount(flags)} of 32";
        }
        catch
        {
            HeroicCountText.Text = "Invalid";
        }
    }

    private void ResetProgress_Click(object sender, RoutedEventArgs e)
    {
        ExperienceBox.Text = "0";
        GoldBox.Text = "0";
        PlayTimeBox.Text = "0";
        HeroRankBox.Text = "0";
        HeroicFlagsBox.Text = "0x00000000";
        _upgradeState = UpgradeState.Empty;
        UpgradePathCombo.SelectedItem = UpgradePath.None;
        SsaHatBox.Text = GiantsHatBox.Text = SwapHatBox.Text = SuperHatBox.Text = "0";
        MarkControlsDirty();
    }

    private void MaxProgress_Click(object sender, RoutedEventArgs e)
    {
        ExperienceBox.Text = SkylanderDump.MaxExperience.ToString(CultureInfo.InvariantCulture);
        GoldBox.Text = SkylanderDump.MaxGold.ToString(CultureInfo.InvariantCulture);
        HeroicFlagsBox.Text = "0xFFFFFFFF";
        MarkControlsDirty();
    }

    private void UpgradePathCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || UpgradePathCombo.SelectedItem is not UpgradePath path)
            return;
        _upgradeState = _upgradeState with
        {
            Path = path,
            StoredSecondaryPath = path switch
            {
                UpgradePath.Primary => false,
                UpgradePath.Secondary => true,
                _ => _upgradeState.StoredSecondaryPath
            }
        };
        MarkControlsDirty();
    }

    private void ManageUpgrades_Click(object sender, RoutedEventArgs e)
    {
        if (_dump is null)
            return;

        ushort id = ushort.TryParse(IdBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out ushort parsedId)
            ? parsedId
            : _dump.CharacterId;
        FigureDefinition? definition = FigureCombo.SelectedItem as FigureDefinition ?? _dump.Definition;
        UpgradeState current = _upgradeState with
        {
            Path = UpgradePathCombo.SelectedItem is UpgradePath path ? path : _upgradeState.Path
        };
        var dialog = new UpgradeManagerWindow(UpgradeCatalog.Find(id, definition), current) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _upgradeState = dialog.Result;
        _suppressEvents = true;
        UpgradePathCombo.SelectedItem = _upgradeState.Path;
        _suppressEvents = false;
        MarkControlsDirty();
    }

    private void HexMode_Changed(object sender, RoutedEventArgs e) => RefreshHex();

    private void RefreshHex()
    {
        if (_dump is not null && HexText is not null)
            HexText.Text = _dump.GetHexDump(DecryptedHexCheckBox.IsChecked == true);
    }

    private void ControlChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressEvents && _dump is not null)
            MarkControlsDirty();
    }

    private void MarkControlsDirty()
    {
        _controlsDirty = true;
        UpdateTitle();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            Open_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            New_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
        {
            CloseFile_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.S)
        {
            SaveCurrent(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files || !ConfirmSaveOrDiscard())
            return;
        OpenPath(files[0]);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmSaveOrDiscard())
            e.Cancel = true;
    }

    private bool ConfirmSaveOrDiscard()
    {
        if (!_controlsDirty && !_modelDirty)
            return true;

        MessageBoxResult result = MessageBox.Show(this, "Save changes to the current dump?", "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveCurrent(false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private bool ConfirmDiscardOnly()
    {
        if (!_controlsDirty && !_modelDirty)
            return true;
        return MessageBox.Show(this, "Discard all changes and reload the file?", "Revert", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void UpdateTitle()
    {
        string marker = _controlsDirty || _modelDirty ? "*" : string.Empty;
        Title = _dump is null ? "SkyConfig" : $"{marker}{FileNameText.Text} - SkyConfig";
    }

    private void SetStatus(string text) => StatusText.Text = text;
    private static string PassFail(bool value) => value ? "valid" : "invalid";
    private static Brush StatusBrush(bool value) => value ? Brushes.DarkGreen : Brushes.DarkRed;

    private static string FriendlyGame(string game) => game switch
    {
        "SpyrosAdv" => "Spyro's Adventure",
        "SwapForce" => "Swap Force",
        "TrapTeam" => "Trap Team",
        "Superchargers" => "SuperChargers",
        _ => game
    };

    private static ushort ParseUInt16(string text, string label, bool hexadecimal)
    {
        uint value = ParseUInt32(text, label, hexadecimal);
        if (value > ushort.MaxValue)
            throw new OverflowException($"{label} must be between 0 and {ushort.MaxValue}.");
        return (ushort)value;
    }

    private static byte ParseByte(string text, string label)
    {
        ushort value = ParseUInt16(text, label, false);
        if (value > byte.MaxValue)
            throw new OverflowException($"{label} must be between 0 and {byte.MaxValue}.");
        return (byte)value;
    }

    private static uint ParseUInt32(string text, string label, bool hexadecimal)
    {
        string value = text.Trim();
        if (hexadecimal && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];
        NumberStyles style = hexadecimal ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
        if (!uint.TryParse(value, style, CultureInfo.InvariantCulture, out uint result))
            throw new FormatException($"{label} is not a valid {(hexadecimal ? "hexadecimal" : "whole")} number.");
        return result;
    }

    private static int ParseInt32(string text, string label, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < minimum || value > maximum)
            throw new FormatException($"{label} must be a whole number between {minimum} and {maximum}.");
        return value;
    }

    private void ShowError(string message, Exception exception) =>
        MessageBox.Show(this, $"{message}\n\n{exception.Message}", "SkyConfig", MessageBoxButton.OK, MessageBoxImage.Error);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            "SkyConfig 1.0\n\nOffline editor for 1,024-byte Skylander dumps used by RPCS3.\n\nThis project is not affiliated with Activision or RPCS3.",
            "About SkyConfig", MessageBoxButton.OK, MessageBoxImage.Information);
}

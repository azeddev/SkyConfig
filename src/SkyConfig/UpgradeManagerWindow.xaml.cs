using System.Windows;
using System.Windows.Controls;
using SkyConfig.Core;

namespace SkyConfig.App;

public partial class UpgradeManagerWindow : Window
{
    private readonly UpgradeProfile _profile;
    private bool _ready;

    public UpgradeState Result { get; private set; }

    public UpgradeManagerWindow(UpgradeProfile profile, UpgradeState state)
    {
        _profile = profile;
        Result = state;
        InitializeComponent();

        FigureNameText.Text = profile.FigureName;
        CatalogStatusText.Text = profile.HasNamedData
            ? "Named upgrade data is available for this character. The dump stores unlock flags, not the names."
            : "No verified name profile was found for this figure. Generic labels map to the same dump flags.";

        Base1Check.Content = profile.BaseUpgrades[0];
        Base2Check.Content = profile.BaseUpgrades[1];
        Base3Check.Content = profile.BaseUpgrades[2];
        Base4Check.Content = profile.BaseUpgrades[3];
        PrimaryGroup.Header = $"Primary path: {profile.PrimaryPathName}";
        Primary1Check.Content = profile.PrimaryUpgrades[0];
        Primary2Check.Content = profile.PrimaryUpgrades[1];
        Primary3Check.Content = profile.PrimaryUpgrades[2];
        SecondaryGroup.Header = $"Secondary path: {profile.SecondaryPathName}";
        Secondary1Check.Content = profile.SecondaryUpgrades[0];
        Secondary2Check.Content = profile.SecondaryUpgrades[1];
        Secondary3Check.Content = profile.SecondaryUpgrades[2];
        SoulGemCheck.Content = string.IsNullOrWhiteSpace(profile.SoulGemName)
            ? "Soul Gem"
            : $"Soul Gem: {profile.SoulGemName}";
        WowPowCheck.Content = string.IsNullOrWhiteSpace(profile.WowPowName)
            ? "Wow Pow / Sky-Chi"
            : $"Wow Pow / Sky-Chi: {profile.WowPowName}";

        PathCombo.ItemsSource = new[]
        {
            new PathOption(UpgradePath.None, "No path selected"),
            new PathOption(UpgradePath.Primary, $"Primary: {profile.PrimaryPathName}"),
            new PathOption(UpgradePath.Secondary, $"Secondary: {profile.SecondaryPathName}")
        };
        ShowState(state);
        _ready = true;
        UpdateResult();
    }

    private void ShowState(UpgradeState state)
    {
        PathCombo.SelectedItem = ((IEnumerable<PathOption>)PathCombo.ItemsSource).Single(item => item.Path == state.Path);
        Base1Check.IsChecked = state.Base1;
        Base2Check.IsChecked = state.Base2;
        Base3Check.IsChecked = state.Base3;
        Base4Check.IsChecked = state.Base4;
        Primary1Check.IsChecked = state.Primary1;
        Primary2Check.IsChecked = state.Primary2;
        Primary3Check.IsChecked = state.Primary3;
        Secondary1Check.IsChecked = state.Secondary1;
        Secondary2Check.IsChecked = state.Secondary2;
        Secondary3Check.IsChecked = state.Secondary3;
        SoulGemCheck.IsChecked = state.SoulGem;
        WowPowCheck.IsChecked = state.WowPow;
    }

    private void UpgradeControl_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready)
            UpdateResult();
    }

    private void UpdateResult()
    {
        UpgradePath path = PathCombo.SelectedItem is PathOption option ? option.Path : UpgradePath.None;
        bool storedSecondary = path switch
        {
            UpgradePath.Primary => false,
            UpgradePath.Secondary => true,
            _ => Result.StoredSecondaryPath
        };

        Result = new UpgradeState(
            path, storedSecondary,
            Base1Check.IsChecked == true, Base2Check.IsChecked == true,
            Base3Check.IsChecked == true, Base4Check.IsChecked == true,
            Primary1Check.IsChecked == true, Primary2Check.IsChecked == true,
            Primary3Check.IsChecked == true, Secondary1Check.IsChecked == true,
            Secondary2Check.IsChecked == true, Secondary3Check.IsChecked == true,
            SoulGemCheck.IsChecked == true, WowPowCheck.IsChecked == true);
        RawBitsText.Text = $"0x{Result.ToRawBits():X4}";
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _ready = false;
        Result = UpgradeState.Empty;
        ShowState(Result);
        _ready = true;
        UpdateResult();
    }

    private void UnlockSelected_Click(object sender, RoutedEventArgs e)
    {
        UpgradePath path = PathCombo.SelectedItem is PathOption option && option.Path != UpgradePath.None
            ? option.Path
            : UpgradePath.Primary;
        Result = ReadWithAllBase() with
        {
            Path = path,
            Primary1 = path == UpgradePath.Primary || Result.Primary1,
            Primary2 = path == UpgradePath.Primary || Result.Primary2,
            Primary3 = path == UpgradePath.Primary || Result.Primary3,
            Secondary1 = path == UpgradePath.Secondary || Result.Secondary1,
            Secondary2 = path == UpgradePath.Secondary || Result.Secondary2,
            Secondary3 = path == UpgradePath.Secondary || Result.Secondary3
        };
        _ready = false;
        ShowState(Result);
        _ready = true;
        UpdateResult();
    }

    private void UnlockAll_Click(object sender, RoutedEventArgs e)
    {
        Result = ReadWithAllBase() with
        {
            Primary1 = true,
            Primary2 = true,
            Primary3 = true,
            Secondary1 = true,
            Secondary2 = true,
            Secondary3 = true,
            SoulGem = true,
            WowPow = true
        };
        _ready = false;
        ShowState(Result);
        _ready = true;
        UpdateResult();
    }

    private UpgradeState ReadWithAllBase()
    {
        UpdateResult();
        return Result with { Base1 = true, Base2 = true, Base3 = true, Base4 = true };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        UpdateResult();
        DialogResult = true;
    }

    private sealed record PathOption(UpgradePath Path, string Label);
}

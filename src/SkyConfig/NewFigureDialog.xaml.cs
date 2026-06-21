using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SkyConfig.Core;

namespace SkyConfig.App;

public partial class NewFigureDialog : Window
{
    public ushort FigureId { get; private set; }
    public ushort VariantId { get; private set; }

    public NewFigureDialog()
    {
        InitializeComponent();
        FigureCombo.ItemsSource = FigureCatalog.Figures;
        FigureCombo.SelectedItem = FigureCatalog.Find(16, 0);
    }

    private void FigureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FigureCombo.SelectedItem is not FigureDefinition figure)
            return;
        IdBox.Text = figure.Id.ToString(CultureInfo.InvariantCulture);
        VariantBox.Text = $"0x{figure.Variant:X4}";
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ushort.TryParse(IdBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out ushort id))
                throw new FormatException("ID must be a decimal number from 0 to 65535.");
            string variantText = VariantBox.Text.Trim();
            if (variantText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                variantText = variantText[2..];
            if (!ushort.TryParse(variantText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort variant))
                throw new FormatException("Variant must be a hexadecimal number from 0000 to FFFF.");
            FigureId = id;
            VariantId = variant;
            DialogResult = true;
        }
        catch (FormatException exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

using System.Windows;
using HotsOverlay.Core;

namespace HotsOverlay.App;

public partial class CustomBuildWindow : Window
{
    /// <summary>The build the user entered; set when the dialog closes with DialogResult == true.</summary>
    public HeroBuild? Result { get; private set; }

    public CustomBuildWindow(string heroName)
    {
        InitializeComponent();
        HeroText.Text = $"New build for {heroName}";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CustomBuild.TryCreate(NameBox.Text, TalentsBox.Text, out var build, out var error))
        {
            ErrorText.Text = error;
            return;
        }

        Result = build;
        DialogResult = true;
    }
}

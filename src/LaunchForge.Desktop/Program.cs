using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace LaunchForge.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class App : Application
{
    public override void Initialize()
    {
        var theme = new FluentTheme();
        theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse("#14B8A6") };
        Styles.Add(theme);
        Styles.Add(new Style(selector => selector.OfType<Button>())
        {
            Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(7)) }
        });
        Styles.Add(new Style(selector => selector.OfType<TextBox>())
        {
            Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(7)) }
        });
        Styles.Add(new Style(selector => selector.OfType<ComboBox>())
        {
            Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(7)) }
        });
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

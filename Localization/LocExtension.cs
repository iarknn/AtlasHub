using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace AtlasHub.Localization;

[MarkupExtensionReturnType(typeof(BindingExpression))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Designer'da DI container yok; sadece key'i göster
        if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            return Key;
        }

        // Runtime'da LocalizationService indexer'ına OneWay binding
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Svc,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
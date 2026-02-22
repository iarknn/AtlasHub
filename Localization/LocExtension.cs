using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace AtlasHub.Localization;

[MarkupExtensionReturnType(typeof(BindingExpression))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]")
        {
            Source = Loc.Svc,
            Mode = BindingMode.OneWay
        }.ProvideValue(serviceProvider);
    }
}

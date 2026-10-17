using Avalonia;
using Avalonia.Markup.Xaml;
using JxTerminal.Services;
using System;

namespace JxTerminal.Converters
{
    public class LocalizeExtension : MarkupExtension
    {
        public string Key { get; set; }

        public LocalizeExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var target = (IProvideValueTarget?)serviceProvider?.GetService(typeof(IProvideValueTarget));
            if (target?.TargetObject is AvaloniaObject targetObject && target.TargetProperty is AvaloniaProperty targetProperty)
            {
                targetObject.Bind(targetProperty, LocalizationService.Instance.GetObservable(Key));
            }

            return LocalizationService.Instance[Key];
        }
    }
}

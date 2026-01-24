using Avalonia.Data;
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
            var binding = new Binding
            {
                Source = LocalizationService.Instance,
                Path = $"[{Key}]",
                Mode = BindingMode.OneWay
            };

            return binding;
        }
    }
}

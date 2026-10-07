using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Markup;

namespace PDA.Media.Desktop.MarkupExtensions
{
    public class EnumBindingSourceExtension : MarkupExtension
    {
        public Type EnumType { get; private set; }
        public EnumBindingSourceExtension(Type enumType)
        {
            if (enumType is null || !enumType.IsEnum)
                throw new ArgumentException("Type must be an enum and cannot be null.", nameof(enumType));

            EnumType = enumType;
        }

        public override object? ProvideValue(IServiceProvider serviceProvider)
        {
            return Enum.GetValues(EnumType);
        }
    }
}
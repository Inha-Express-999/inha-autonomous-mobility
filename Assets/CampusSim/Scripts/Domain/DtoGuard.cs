using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace InhaExpress.Client.Domain
{
    internal static class DtoGuard
    {
        internal static string Text(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Required text is empty.", name);
            return value;
        }

        internal static string OptionalId(string value, string name) => value == null ? null : Text(value, name);

        internal static double Finite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name);
            return value;
        }

        internal static double NonNegative(double value, string name)
        {
            if (Finite(value, name) < 0) throw new ArgumentOutOfRangeException(name);
            return value;
        }

        internal static double? OptionalNumber(double? value, string name) =>
            value.HasValue ? NonNegative(value.Value, name) : (double?)null;

        internal static T EnumValue<T>(T value, string name) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(name);
            return value;
        }

        internal static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> source, string name)
        {
            if (source == null) throw new ArgumentNullException(name);
            var items = new List<T>();
            foreach (var item in source)
            {
                if (item == null) throw new ArgumentException("Collection contains null.", name);
                items.Add(item);
            }
            return items.AsReadOnly();
        }
    }
}

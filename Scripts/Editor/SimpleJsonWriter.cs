using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tauntastic.Packages.Editor
{
    /// <summary>Minimal JSON writer, the counterpart of <see cref="SimpleJson"/>: writes Dictionary of string to object, List of object, strings, booleans, numbers and null as indented JSON.</summary>
    public static class SimpleJsonWriter
    {
        /// <summary>Turns a value into JSON text. Throws ArgumentException for a type that cannot be written.</summary>
        public static string Serialize(object value)
        {
            var builder = new StringBuilder();
            Write(builder, value, 0);
            return builder.ToString();
        }

        private static void Write(StringBuilder builder, object value, int indent)
        {
            if (value == null)
            {
                builder.Append("null");
            }
            else if (value is string)
            {
                WriteString(builder, (string)value);
            }
            else if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
            }
            else if (value is int || value is long || value is short || value is byte)
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else if (value is double || value is float)
            {
                builder.Append(Convert.ToDouble(value).ToString("R", CultureInfo.InvariantCulture));
            }
            else if (value is Dictionary<string, object>)
            {
                WriteObject(builder, (Dictionary<string, object>)value, indent);
            }
            else if (value is List<object>)
            {
                WriteArray(builder, (List<object>)value, indent);
            }
            else
            {
                throw new ArgumentException("Cannot write a value of type " + value.GetType().Name + " as JSON.");
            }
        }

        private static void WriteObject(StringBuilder builder, Dictionary<string, object> dictionary, int indent)
        {
            if (dictionary.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append("{\n");
            int index = 0;
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                builder.Append(' ', (indent + 1) * 2);
                WriteString(builder, pair.Key);
                builder.Append(": ");
                Write(builder, pair.Value, indent + 1);
                builder.Append(++index < dictionary.Count ? ",\n" : "\n");
            }
            builder.Append(' ', indent * 2).Append('}');
        }

        private static void WriteArray(StringBuilder builder, List<object> list, int indent)
        {
            if (list.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append("[\n");
            for (int i = 0; i < list.Count; i++)
            {
                builder.Append(' ', (indent + 1) * 2);
                Write(builder, list[i], indent + 1);
                builder.Append(i + 1 < list.Count ? ",\n" : "\n");
            }
            builder.Append(' ', indent * 2).Append(']');
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < ' ')
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}

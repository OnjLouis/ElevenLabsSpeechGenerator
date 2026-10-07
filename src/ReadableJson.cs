using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace ElevenLabsSpeechGenerator
{
    internal static class ReadableJson
    {
        public static string Format(string json)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), XmlDictionaryReaderQuotas.Max))
            using (var output = new MemoryStream())
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(output, new UTF8Encoding(false), false, true, "  "))
            {
                writer.WriteNode(reader, true);
                writer.Flush();
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        public static bool Equivalent(string first, string second)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                return SameValue(serializer.DeserializeObject(first), serializer.DeserializeObject(second));
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static bool SameValue(object first, object second)
        {
            var left = first as Dictionary<string, object>;
            var right = second as Dictionary<string, object>;
            if (left != null || right != null)
                return left != null && right != null && left.Count == right.Count &&
                    left.All(pair => right.ContainsKey(pair.Key) && SameValue(pair.Value, right[pair.Key]));

            var leftItems = first as object[];
            var rightItems = second as object[];
            if (leftItems != null || rightItems != null)
                return leftItems != null && rightItems != null && leftItems.Length == rightItems.Length &&
                    leftItems.Zip(rightItems, SameValue).All(equal => equal);

            return Equals(first, second);
        }
    }
}

using Newtonsoft.Json;
using System;
using System.Text;

namespace NetGear.Core.Common
{
    public static class JsonExtension
    {
        private static JsonSerializerSettings settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        public static byte[] ToSerializedBytes<T>(this T obj)
        {
            if (null == obj) return null;
            var json = JsonConvert.SerializeObject(obj, settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public static T ToDeserializedObject<T>(this byte[] bytes)
        {
            if (null == bytes || bytes.Length == 0) return default(T);
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes), settings);
        }

        public static string ToSerializedBase64String<T>(this T obj)
        {
            if (null == obj) return null;
            var bytes = obj.ToSerializedBytes();
            return Convert.ToBase64String(bytes);
        }

        public static T ToDeserializedObjectFromBase64String<T>(this string base64String)
        {
            try
            {
                var bytes = Convert.FromBase64String(base64String);
                return bytes.ToDeserializedObject<T>();
            }
            catch { }
            return default(T);
        }
    }
}

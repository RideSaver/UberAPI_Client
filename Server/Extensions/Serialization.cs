using Newtonsoft.Json;
using Newtonsoft.Json.Bson;

namespace UberClient.Extensions
{
    public static class Serialization
    {
        public static byte[]? ToByteArray(this object obj)
        {
            if (obj is null) return null;
                   
            MemoryStream MS = new MemoryStream();
            using(BsonDataWriter writer = new BsonDataWriter(MS))
            {
                JsonSerializer serializer= new JsonSerializer();
                serializer.Serialize(writer, obj);
            }
            return MS.ToArray(); 
        }
        public static T? FromByteArray<T>(this byte[] byteArray) where T : class
        {
            if (byteArray is null) return default;

            T data;
            MemoryStream ms = new MemoryStream(byteArray);
            using (BsonDataReader reader = new BsonDataReader(ms))
            {
                JsonSerializer serializer= new JsonSerializer();
                data = serializer.Deserialize<T>(reader)!;
            }

            return data;
        }

    }
}

using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
namespace UberClient.Server.Extensions.Cache
{
    //! \class Serialization
    /*!
     * This class uses the serialization and deserialization mechanism. Serialization will convert
     * an object's state into a byte stream, while deserialization will convert a byte stream into
     * the appropriate object.
    */
    public static class Serialization
    {
        public static byte[] ToByteArray(this object obj)
        {
            if (obj == null)
            {
                return null;
            }
            BinaryFormatter binaryFormatter = new BinaryFormatter();
            using (MemoryStream memoryStream = new MemoryStream())
            {
                binaryFormatter.Serialize(memoryStream, obj);
                return memoryStream.ToArray();
            }
        }
        public static T FromByteArray<T>(this byte[] byteArray) where T : class
        {
            if (byteArray == null)
            {
                return default(T);
            }
            BinaryFormatter binaryFormatter = new BinaryFormatter();
            using (MemoryStream memoryStream = new MemoryStream(byteArray))
            {
                return binaryFormatter.Deserialize(memoryStream) as T;
            }
        }

    }
}

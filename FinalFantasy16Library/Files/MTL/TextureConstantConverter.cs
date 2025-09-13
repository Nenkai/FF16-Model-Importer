using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using static FinalFantasy16Library.Files.MTL.MtlFile;

namespace FinalFantasy16Library.Files.MTL;

public class TextureConstantConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(TextureConstant);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var textureConstant = (TextureConstant)value;

        writer.WriteStartObject();
        writer.WritePropertyName("Type");
        writer.WriteValue(textureConstant.Type.ToString());

        writer.WritePropertyName("Value");
        switch (textureConstant.Type)
        {
            case ConstantType.HalfFloat:
                serializer.Serialize(writer, (Half)textureConstant.Value);
                break;
            case ConstantType.Rgba:
                serializer.Serialize(writer, (Rgba)textureConstant.Value);
                break;
        }

        writer.WritePropertyName("Name");
        writer.WriteValue(textureConstant.Name);

        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        JObject obj = JObject.Load(reader);

        var textureConstant = new TextureConstant
        {
            Type = Enum.Parse<ConstantType>(obj["Type"]!.ToString()),
            Name = obj["Name"]!.ToString()
        };

        switch (textureConstant.Type)
        {
            case ConstantType.HalfFloat:
                textureConstant.Value = obj["Value"]!.ToObject<Half>(serializer);
                break;
            case ConstantType.Rgba:
                textureConstant.Value = obj["Value"]!.ToObject<Rgba>(serializer);
                break;
        }

        return textureConstant;
    }
}

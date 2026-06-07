using Newtonsoft.Json.Linq;

namespace Koren.IO.Interface;

public interface ISettingsFile {
    JToken Serialize();
    void Deserialize(JToken token);
}
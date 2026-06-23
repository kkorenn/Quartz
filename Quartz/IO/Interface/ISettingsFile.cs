using Newtonsoft.Json.Linq;

namespace Quartz.IO.Interface;

public interface ISettingsFile {
    JToken Serialize();
    void Deserialize(JToken token);
}
using System.Text.Json.Serialization;

namespace AQ.GroupManagementLibrary;

public class NormalRequestBody
{
    public NormalRequestBody(string serial)
    {
        this.Serial = serial;
    }
    
    [JsonPropertyName("Serial")]
    public string Serial { get; set; }
}
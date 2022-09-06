namespace AQ.DeviceIdentifier;

public interface IDeviceIdentifierService
{
    public string Get();

    public void Set(string newId);
}
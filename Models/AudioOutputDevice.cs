namespace StretchCord.Models
{
    public sealed class AudioOutputDevice
    {
        public string? Id { get; }
        public string Name { get; }
        public bool IsDefault { get; }

        public AudioOutputDevice(string? id, string name, bool isDefault = false)
        {
            Id = id;
            Name = name;
            IsDefault = isDefault;
        }

        public override string ToString() => Name;
    }
}

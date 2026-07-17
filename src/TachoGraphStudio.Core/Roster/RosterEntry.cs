using System.Text.Json.Serialization;

namespace TachoGraphStudio.Core.Roster;

public sealed record RosterEntry
{
    private string _detail = string.Empty;
    private string _driver = string.Empty;
    private string _registrationNumber = string.Empty;
    private string _specification = string.Empty;
    private string _vehicleType = string.Empty;

    [JsonPropertyName("ctrl_num")]
    public required long ControlNumber { get; init; }

    [JsonPropertyName("detail")]
    public string Detail
    {
        get => _detail;
        init => _detail = value ?? string.Empty;
    }

    [JsonPropertyName("spec")]
    public string Specification
    {
        get => _specification;
        init => _specification = value ?? string.Empty;
    }

    [JsonPropertyName("vehicle_num")]
    public string RegistrationNumber
    {
        get => _registrationNumber;
        init => _registrationNumber = value ?? string.Empty;
    }

    [JsonPropertyName("vehicle_type")]
    public string VehicleType
    {
        get => _vehicleType;
        init => _vehicleType = value ?? string.Empty;
    }

    [JsonPropertyName("driver")]
    public string Driver
    {
        get => _driver;
        init => _driver = value ?? string.Empty;
    }

    [JsonPropertyName("work_period")]
    public string? WorkPeriod { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("is_tacho_target")]
    public bool IsTachoTarget { get; init; }
}

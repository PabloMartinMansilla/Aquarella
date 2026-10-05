namespace Aquarella.Services;
// In-process notifications keyed by business; no database entities are shared across circuits.
public sealed class DatabaseChanges
{
    public event Action<Guid, string>? Changed;
    public void Publish(Guid businessId, string area) => Changed?.Invoke(businessId, area);
}

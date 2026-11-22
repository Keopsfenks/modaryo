using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.Domain.Common;

public abstract class BaseEntity
{
    /// <summary>UUID v7 (zamana göre sıralı).</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    private readonly List<BaseEvent> _domainEvents = new();

    [NotMapped]
    public IReadOnlyCollection<BaseEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(BaseEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void RemoveDomainEvent(BaseEvent domainEvent)
    {
        _domainEvents.Remove(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

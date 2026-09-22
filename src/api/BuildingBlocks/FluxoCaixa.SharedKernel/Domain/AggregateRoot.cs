namespace FluxoCaixa.SharedKernel.Domain;

/// <summary>
/// Evento de domínio: fato relevante ocorrido dentro de um agregado.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OcorridoEm { get; }
}

/// <summary>
/// Raiz de agregado: fronteira de consistência que registra os eventos de domínio que produz.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>Construtor para materialização pelo ORM.</summary>
    protected AggregateRoot()
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}

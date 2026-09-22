namespace FluxoCaixa.SharedKernel.Domain;

/// <summary>
/// Value object: sem identidade, igualdade pelos componentes estruturais.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);

    public bool Equals(ValueObject? other) =>
        other is not null
        && other.GetType() == GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var componente in GetEqualityComponents())
        {
            hash.Add(componente);
        }

        return hash.ToHashCode();
    }

    protected abstract IEnumerable<object?> GetEqualityComponents();
}

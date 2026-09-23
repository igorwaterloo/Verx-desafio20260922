using System.Diagnostics.Metrics;

namespace Lancamentos.Application.UnitTests;

/// <summary><see cref="IMeterFactory"/> isolado por teste: os coletores filtram pelo escopo desta fábrica.</summary>
internal sealed class MedidoresDeTeste : IMeterFactory
{
    private readonly List<Meter> _medidores = [];

    public Meter Create(MeterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Scope = this;
        var medidor = new Meter(options);
        _medidores.Add(medidor);
        return medidor;
    }

    public void Dispose()
    {
        foreach (var medidor in _medidores)
        {
            medidor.Dispose();
        }
    }
}

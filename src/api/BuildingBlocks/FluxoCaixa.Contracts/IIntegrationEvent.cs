namespace FluxoCaixa.Contracts;

/// <summary>
/// Evento de integração: contrato público entre bounded contexts (Published Language).
/// Todo evento carrega o tenant (MT-02), usado pelo consumidor para definir o contexto de execução.
/// </summary>
/// <remarks>
/// Evolução: apenas campos opcionais podem ser adicionados. Mudança incompatível gera um novo
/// tipo (<c>...V2</c>) publicado em paralelo durante a migração dos consumidores.
/// </remarks>
public interface IIntegrationEvent
{
    Guid EventId { get; }

    DateTimeOffset OcorridoEm { get; }

    int Versao { get; }

    Guid TenantId { get; }
}

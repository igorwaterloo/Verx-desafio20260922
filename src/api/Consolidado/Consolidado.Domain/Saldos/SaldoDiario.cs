using FluxoCaixa.SharedKernel.Tenancy;

namespace Consolidado.Domain.Saldos;

public enum TipoMovimento
{
    Credito = 1,
    Debito = 2,
}

/// <summary>
/// Projeção do saldo de um dia do tenant (RC-01 a RC-05). A aplicação de movimentos é
/// comutativa (somas), então a ordem de chegada dos eventos não altera o resultado (RC-02).
/// </summary>
public sealed class SaldoDiario : ITenantEntity
{
    private SaldoDiario(Guid tenantId, DateOnly data)
    {
        TenantId = tenantId;
        Data = data;
        Versao = [];
    }

    public Guid TenantId { get; private set; }

    public DateOnly Data { get; private set; }

    public decimal TotalCreditos { get; private set; }

    public decimal TotalDebitos { get; private set; }

    public decimal Saldo => TotalCreditos - TotalDebitos;

    public int QuantidadeLancamentos { get; private set; }

    /// <summary>Instante do evento mais recente aplicado (máximo, para manter a comutatividade).</summary>
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <summary>Concorrência otimista (rowversion): dois workers no mesmo dia não perdem atualizações.</summary>
    public byte[] Versao { get; private set; }

    public static SaldoDiario Novo(Guid tenantId, DateOnly data)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("O saldo precisa pertencer a um tenant.", nameof(tenantId));
        }

        return new SaldoDiario(tenantId, data);
    }

    public void Aplicar(TipoMovimento tipo, decimal valor, DateTimeOffset ocorridoEm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(valor);

        switch (tipo)
        {
            case TipoMovimento.Credito:
                TotalCreditos += valor;
                break;
            case TipoMovimento.Debito:
                TotalDebitos += valor;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Tipo de movimento inválido.");
        }

        QuantidadeLancamentos++;
        if (ocorridoEm > AtualizadoEm)
        {
            AtualizadoEm = ocorridoEm;
        }
    }
}

/// <summary>
/// Inbox: evento de integração já aplicado. Gravado na mesma transação do saldo, garante que cada
/// evento produza efeito uma única vez mesmo com entrega at-least-once (RC-01, ADR-0005).
/// </summary>
public sealed class MensagemProcessada : ITenantEntity
{
    private MensagemProcessada(Guid eventId, Guid tenantId, DateTimeOffset processadaEm)
    {
        EventId = eventId;
        TenantId = tenantId;
        ProcessadaEm = processadaEm;
    }

    public Guid EventId { get; private set; }

    public Guid TenantId { get; private set; }

    public DateTimeOffset ProcessadaEm { get; private set; }

    public static MensagemProcessada Registrar(Guid eventId, Guid tenantId, DateTimeOffset processadaEm) =>
        new(eventId, tenantId, processadaEm);
}

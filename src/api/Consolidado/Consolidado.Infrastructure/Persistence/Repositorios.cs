using Consolidado.Application.Abstractions;
using Consolidado.Application.Saldos;
using Consolidado.Domain.Saldos;
using FluxoCaixa.SharedKernel.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Consolidado.Infrastructure.Persistence;

internal sealed class SaldoDiarioRepository(ConsolidadoDbContext contexto) : ISaldoDiarioRepository
{
    public Task<SaldoDiario?> ObterAsync(DateOnly data, CancellationToken cancellationToken) =>
        contexto.SaldosDiarios.FirstOrDefaultAsync(s => s.Data == data, cancellationToken);

    public void Adicionar(SaldoDiario saldo) => contexto.SaldosDiarios.Add(saldo);
}

internal sealed class Inbox(ConsolidadoDbContext contexto, ITenantContext tenant) : IInbox
{
    public Task<bool> JaProcessadaAsync(Guid eventId, CancellationToken cancellationToken) =>
        contexto.MensagensProcessadas.AnyAsync(m => m.EventId == eventId, cancellationToken);

    public void Registrar(Guid eventId, DateTimeOffset processadaEm) =>
        contexto.MensagensProcessadas.Add(MensagemProcessada.Registrar(eventId, tenant.TenantId, processadaEm));
}

internal sealed class UnitOfWork(ConsolidadoDbContext contexto) : IUnitOfWork
{
    private const int ViolacaoDeIndiceUnico = 2601;
    private const int ViolacaoDeChavePrimaria = 2627;

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflitoDePersistenciaException("O saldo foi alterado por outra mensagem.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: ViolacaoDeIndiceUnico or ViolacaoDeChavePrimaria })
        {
            throw new ConflitoDePersistenciaException("Registro duplicado (saldo do dia ou evento já processado).", ex);
        }
    }
}

/// <summary>Lado de leitura (CQRS): consultas sem tracking, projetadas direto em DTOs.</summary>
internal sealed class SaldosLeitura(ConsolidadoDbContext contexto) : ISaldosLeitura
{
    public Task<SaldoDiarioDto?> ObterAsync(DateOnly data, CancellationToken cancellationToken) =>
        Projetar(contexto.SaldosDiarios.AsNoTracking().Where(s => s.Data == data)).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SaldoDiarioDto>> ListarPeriodoAsync(DateOnly inicio, DateOnly fim, CancellationToken cancellationToken) =>
        await Projetar(contexto.SaldosDiarios.AsNoTracking().Where(s => s.Data >= inicio && s.Data <= fim).OrderBy(s => s.Data))
            .ToListAsync(cancellationToken);

    private static IQueryable<SaldoDiarioDto> Projetar(IQueryable<SaldoDiario> consulta) =>
        consulta.Select(s => new SaldoDiarioDto(
            s.Data, s.TotalCreditos, s.TotalDebitos, s.TotalCreditos - s.TotalDebitos, s.QuantidadeLancamentos));
}

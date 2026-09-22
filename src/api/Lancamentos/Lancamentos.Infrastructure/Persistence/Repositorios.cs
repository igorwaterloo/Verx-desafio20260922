using FluxoCaixa.SharedKernel.Tenancy;
using Lancamentos.Application.Abstractions;
using Lancamentos.Application.Lancamentos;
using Lancamentos.Domain.Lancamentos;
using Lancamentos.Domain.Planos;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Lancamentos.Infrastructure.Persistence;

// Adaptadores das portas da Application. O filtro global do TenantDbContext restringe
// todas as consultas ao tenant corrente; nenhuma consulta recebe TenantId por parâmetro.

internal sealed class LancamentoRepository(LancamentosDbContext contexto) : ILancamentoRepository
{
    public Task<Lancamento?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.Lancamentos.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<int> ContarCriadosDesdeAsync(DateTimeOffset desde, CancellationToken cancellationToken) =>
        contexto.Lancamentos.CountAsync(l => l.CriadoEm >= desde && l.LancamentoOriginalId == null, cancellationToken);

    public void Adicionar(Lancamento lancamento) => contexto.Lancamentos.Add(lancamento);
}

internal sealed class TenantPlanoRepository(LancamentosDbContext contexto) : ITenantPlanoRepository
{
    public Task<TenantPlano?> ObterAsync(CancellationToken cancellationToken) =>
        contexto.TenantsPlanos.FirstOrDefaultAsync(cancellationToken);

    public void Adicionar(TenantPlano plano) => contexto.TenantsPlanos.Add(plano);
}

internal sealed class IdempotenciaRepository(LancamentosDbContext contexto, ITenantContext tenant) : IIdempotenciaRepository
{
    public async Task<Guid?> ObterLancamentoIdAsync(string chave, CancellationToken cancellationToken) =>
        await contexto.ChavesIdempotencia
            .Where(c => c.Chave == chave)
            .Select(c => (Guid?)c.LancamentoId)
            .FirstOrDefaultAsync(cancellationToken);

    public void Registrar(string chave, Guid lancamentoId, DateTimeOffset criadaEm) =>
        contexto.ChavesIdempotencia.Add(new ChaveIdempotencia(tenant.TenantId, chave, lancamentoId, criadaEm));
}

internal sealed class UnitOfWork(LancamentosDbContext contexto) : IUnitOfWork
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
            throw new ConflitoDePersistenciaException("Conflito de concorrência ao gravar.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: ViolacaoDeIndiceUnico or ViolacaoDeChavePrimaria })
        {
            throw new ConflitoDePersistenciaException("Registro duplicado.", ex);
        }
    }
}

/// <summary>Lado de leitura (CQRS): consultas sem tracking, projetadas direto em DTOs.</summary>
internal sealed class LancamentosLeitura(LancamentosDbContext contexto) : ILancamentosLeitura
{
    public Task<LancamentoDto?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken) =>
        Projetar(contexto.Lancamentos.AsNoTracking().Where(l => l.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public async Task<Pagina<LancamentoDto>> ListarPorDataAsync(DateOnly data, int pagina, int tamanhoPagina, CancellationToken cancellationToken)
    {
        var consulta = contexto.Lancamentos.AsNoTracking().Where(l => l.DataCompetencia == data);

        var total = await consulta.CountAsync(cancellationToken);
        var itens = await Projetar(consulta.OrderBy(l => l.CriadoEm).ThenBy(l => l.Id))
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(cancellationToken);

        return new Pagina<LancamentoDto>(itens, pagina, tamanhoPagina, total);
    }

    private static IQueryable<LancamentoDto> Projetar(IQueryable<Lancamento> consulta) =>
        consulta.Select(l => new LancamentoDto(
            l.Id, l.Tipo, l.Valor.Valor, l.DataCompetencia, l.Descricao,
            l.LancamentoOriginalId, l.Estornado, l.CriadoPor, l.CriadoEm));
}

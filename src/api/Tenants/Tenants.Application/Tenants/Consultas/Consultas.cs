using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using FluxoCaixa.SharedKernel.Tenancy;
using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Application.Tenants.Consultas;

public sealed record ObterTenantAtualQuery : IQuery<TenantDto>;

public sealed class ObterTenantAtualHandler(ITenantRepository tenants, ITenantContext tenantContext)
    : IQueryHandler<ObterTenantAtualQuery, TenantDto>
{
    public async Task<Result<TenantDto>> HandleAsync(ObterTenantAtualQuery query, CancellationToken cancellationToken) =>
        await tenants.ObterPorIdAsync(tenantContext.TenantId, cancellationToken) is { } tenant
            ? TenantDto.De(tenant)
            : TenantErros.NaoEncontrado;
}

public sealed record ListarPlanosQuery : IQuery<IReadOnlyList<Plano>>;

public sealed class ListarPlanosHandler : IQueryHandler<ListarPlanosQuery, IReadOnlyList<Plano>>
{
    public Task<Result<IReadOnlyList<Plano>>> HandleAsync(ListarPlanosQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(CatalogoDePlanos.Todos));
}

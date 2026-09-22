using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Domain;
using FluxoCaixa.SharedKernel.Tenancy;

namespace Lancamentos.Domain.Lancamentos;

/// <summary>
/// Registro imutável de uma entrada (crédito) ou saída (débito) no caixa do tenant.
/// Correções são feitas por estorno (RN-03, RN-04 — ADR-0014).
/// </summary>
public sealed class Lancamento : AggregateRoot<Guid>, ITenantEntity
{
    public const int DescricaoMinimo = 3;
    public const int DescricaoMaximo = 200;
    private const string PrefixoEstorno = "Estorno: ";

    private Lancamento(
        Guid tenantId,
        TipoLancamento tipo,
        Dinheiro valor,
        DateOnly dataCompetencia,
        string descricao,
        string criadoPor,
        DateTimeOffset criadoEm,
        Guid? lancamentoOriginalId)
        : base(Guid.CreateVersion7(criadoEm))
    {
        TenantId = tenantId;
        Tipo = tipo;
        Valor = valor;
        DataCompetencia = dataCompetencia;
        Descricao = descricao;
        CriadoPor = criadoPor;
        CriadoEm = criadoEm;
        LancamentoOriginalId = lancamentoOriginalId;
    }

    /// <summary>Construtor para materialização pelo EF Core.</summary>
    private Lancamento()
    {
        Valor = null!;
        Descricao = null!;
        CriadoPor = null!;
    }

    public Guid TenantId { get; private set; }

    public TipoLancamento Tipo { get; private set; }

    public Dinheiro Valor { get; private set; }

    public DateOnly DataCompetencia { get; private set; }

    public string Descricao { get; private set; }

    public Guid? LancamentoOriginalId { get; private set; }

    public bool Estornado { get; private set; }

    public string CriadoPor { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public bool EhEstorno => LancamentoOriginalId.HasValue;

    public static Result<Lancamento> Criar(
        Guid tenantId,
        TipoLancamento tipo,
        decimal valor,
        DateOnly dataCompetencia,
        string descricao,
        string criadoPor,
        DateTimeOffset agora)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("O lançamento precisa pertencer a um tenant.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(criadoPor);

        if (!Enum.IsDefined(tipo))
        {
            return LancamentoErros.TipoInvalido;
        }

        var dinheiro = Dinheiro.Criar(valor);
        if (dinheiro.IsFailure)
        {
            return dinheiro.Error;
        }

        var descricaoNormalizada = descricao?.Trim() ?? string.Empty;
        if (descricaoNormalizada.Length is < DescricaoMinimo or > DescricaoMaximo)
        {
            return LancamentoErros.DescricaoInvalida;
        }

        if (dataCompetencia > Calendario.DataEmSaoPaulo(agora))
        {
            return LancamentoErros.DataFutura;
        }

        var lancamento = new Lancamento(tenantId, tipo, dinheiro.Value, dataCompetencia, descricaoNormalizada, criadoPor, agora, null);
        lancamento.RaiseDomainEvent(new LancamentoCriado(lancamento.Id, agora));
        return lancamento;
    }

    /// <summary>
    /// Anula este lançamento criando um novo com o tipo inverso, mesmo valor e mesma data de
    /// competência (RN-04). Um lançamento só é estornado uma vez (RN-05) e um estorno não é estornável (RN-06).
    /// </summary>
    public Result<Lancamento> Estornar(string usuario, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usuario);

        if (EhEstorno)
        {
            return LancamentoErros.EstornoNaoPodeSerEstornado;
        }

        if (Estornado)
        {
            return LancamentoErros.JaEstornado;
        }

        var descricao = (PrefixoEstorno + Descricao)[..Math.Min(PrefixoEstorno.Length + Descricao.Length, DescricaoMaximo)];
        var estorno = new Lancamento(TenantId, Tipo.Inverso(), Valor, DataCompetencia, descricao, usuario, agora, Id);
        estorno.RaiseDomainEvent(new LancamentoCriado(estorno.Id, agora));

        Estornado = true;
        RaiseDomainEvent(new LancamentoEstornado(Id, estorno.Id, agora));

        return estorno;
    }
}

public sealed record LancamentoCriado(Guid LancamentoId, DateTimeOffset OcorridoEm) : IDomainEvent;

public sealed record LancamentoEstornado(Guid LancamentoId, Guid EstornoId, DateTimeOffset OcorridoEm) : IDomainEvent;

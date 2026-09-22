using FluxoCaixa.SharedKernel.Domain;
using Shouldly;

namespace FluxoCaixa.SharedKernel.UnitTests;

public sealed class AggregateRootTests
{
    private sealed record PedidoCriado(Guid PedidoId) : IDomainEvent
    {
        public DateTimeOffset OcorridoEm { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class Pedido : AggregateRoot<Guid>
    {
        public Pedido(Guid id)
            : base(id) => RaiseDomainEvent(new PedidoCriado(id));
    }

    [Fact]
    public void RaiseDomainEvent_RegistraOEvento()
    {
        var pedido = new Pedido(Guid.NewGuid());

        pedido.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<PedidoCriado>()
            .PedidoId.ShouldBe(pedido.Id);
    }

    [Fact]
    public void ClearDomainEvents_RemoveOsEventos()
    {
        var pedido = new Pedido(Guid.NewGuid());

        pedido.ClearDomainEvents();

        pedido.DomainEvents.ShouldBeEmpty();
    }
}

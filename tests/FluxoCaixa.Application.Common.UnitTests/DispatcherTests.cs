using FluxoCaixa.Application.Common.Cqrs;
using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Cqrs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace FluxoCaixa.Application.Common.UnitTests;

public sealed class DispatcherTests
{
    private static ServiceProvider CriarProvider(Action<IServiceCollection>? configurar = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<Espiao>();
        services.AddCqrs(typeof(DispatcherTests).Assembly);
        configurar?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task SendAsync_ComandoValido_ExecutaOHandlerERetornaOResultado()
    {
        await using var provider = CriarProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var resultado = await dispatcher.SendAsync(new CriarItem("Caneta"), TestContext.Current.CancellationToken);

        resultado.IsSuccess.ShouldBeTrue();
        resultado.Value.ShouldBe(CriarItemHandler.IdGerado);
        provider.GetRequiredService<Espiao>().Execucoes.ShouldBe(1);
    }

    [Fact]
    public async Task QueryAsync_ExecutaOHandlerDaQuery()
    {
        await using var provider = CriarProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var id = Guid.NewGuid();

        var resultado = await dispatcher.QueryAsync(new ObterItem(id), TestContext.Current.CancellationToken);

        resultado.Value.ShouldBe($"item-{id}");
    }

    [Fact]
    public async Task QueryAsync_FalhaDeNegocio_RetornaOErroDoHandler()
    {
        await using var provider = CriarProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var resultado = await dispatcher.QueryAsync(new ObterItem(Guid.Empty), TestContext.Current.CancellationToken);

        resultado.IsFailure.ShouldBeTrue();
        resultado.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task SendAsync_ComandoInvalido_RetornaErroDeValidacaoSemExecutarOHandler()
    {
        await using var provider = CriarProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var resultado = await dispatcher.SendAsync(new CriarItem(string.Empty), TestContext.Current.CancellationToken);

        resultado.IsFailure.ShouldBeTrue();
        var erro = resultado.Error.ShouldBeOfType<ValidationError>();
        erro.Errors[nameof(CriarItem.Nome)].ShouldContain("O nome é obrigatório.");
        provider.GetRequiredService<Espiao>().Execucoes.ShouldBe(0);
    }

    [Fact]
    public async Task SendAsync_SemHandlerRegistrado_LancaExcecaoDescritiva()
    {
        await using var provider = CriarProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var excecao = await Should.ThrowAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(new ComandoSemHandler(), TestContext.Current.CancellationToken));

        excecao.Message.ShouldContain(nameof(ComandoSemHandler));
    }

    [Fact]
    public async Task Decorators_ExecutamEmOrdemDeRegistroAntesDoHandler()
    {
        await using var provider = CriarProvider(services =>
            services.AddScoped(typeof(IRequestDecorator<,>), typeof(DecoratorDeTeste<,>)));
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.SendAsync(new CriarItem("Caneta"), TestContext.Current.CancellationToken);

        provider.GetRequiredService<Espiao>().Ordem.ShouldBe(["decorator", "handler"]);
    }

    public sealed class DecoratorDeTeste<TRequest, TResponse>(Espiao espiao) : IRequestDecorator<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<Result<TResponse>> HandleAsync(
            TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            espiao.Registrar("decorator");
            return next();
        }
    }
}

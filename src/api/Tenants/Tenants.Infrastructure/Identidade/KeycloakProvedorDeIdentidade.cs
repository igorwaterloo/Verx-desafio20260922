using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxoCaixa.SharedKernel;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Tenants.Application.Abstractions;
using Tenants.Domain.Tenants;

namespace Tenants.Infrastructure.Identidade;

/// <summary>
/// Adaptador da Admin REST API do Keycloak (Organizations = tenants — ADR-0008/0015/0017).
/// Todas as operações são idempotentes (busca pelo atributo <c>tenant_id</c> antes de criar), o que
/// permite retomar um onboarding interrompido. Falhas de rede, timeouts e 5xx viram
/// <see cref="ProvedorDeIdentidadeIndisponivelException"/>.
/// </summary>
internal sealed class KeycloakProvedorDeIdentidade(HttpClient http) : IProvedorDeIdentidade
{
    private const string AtributoTenant = "tenant_id";
    private const string AtributoPlano = "plano";

    public Task<string> GarantirOrganizacaoAsync(Guid tenantId, string nome, string planoCodigo, CancellationToken cancellationToken) =>
        ExecutarAsync(async () =>
        {
            if (await BuscarOrganizacaoAsync(tenantId, cancellationToken) is { } existente)
            {
                return existente;
            }

            var alias = $"t-{tenantId:N}";
            var corpo = new
            {
                name = nome,
                alias,
                enabled = true,
                description = "Tenant do Fluxo de Caixa (SaaS)",
                attributes = new Dictionary<string, string[]> { [AtributoTenant] = [tenantId.ToString()], [AtributoPlano] = [planoCodigo] },
            };

            using var resposta = await http.PostAsJsonAsync("organizations", corpo, cancellationToken);
            if (resposta.StatusCode == HttpStatusCode.Conflict)
            {
                // Nome de organização já usado por outra empresa: diferencia pelo alias do tenant.
                using var novaTentativa = await http.PostAsJsonAsync("organizations", corpo with { name = $"{nome} ({alias[..10]})" }, cancellationToken);
                return IdDoLocation(await GarantirSucessoAsync(novaTentativa, cancellationToken));
            }

            return IdDoLocation(await GarantirSucessoAsync(resposta, cancellationToken));
        });

    public Task<Result<string>> CriarUsuarioAsync(NovoUsuarioDoTenant usuario, CancellationToken cancellationToken) =>
        ExecutarAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(usuario);

            var (primeiroNome, sobrenome) = SepararNome(usuario.Nome);
            var corpo = new
            {
                username = usuario.Email,
                email = usuario.Email,
                firstName = primeiroNome,
                lastName = sobrenome,
                enabled = true,
                emailVerified = true,
                attributes = new Dictionary<string, string[]> { [AtributoTenant] = [usuario.TenantId.ToString()], [AtributoPlano] = [usuario.Plano] },
                credentials = new[] { new { type = "password", value = usuario.Senha, temporary = false } },
            };

            string usuarioId;
            using (var resposta = await http.PostAsJsonAsync("users", corpo, cancellationToken))
            {
                if (resposta.StatusCode == HttpStatusCode.Conflict)
                {
                    // Retomada do mesmo tenant → reaproveita; e-mail de outro tenant → conflito definitivo.
                    var existente = await BuscarUsuarioPorEmailAsync(usuario.Email, cancellationToken);
                    if (existente is null || Atributo(existente, AtributoTenant) != usuario.TenantId.ToString())
                    {
                        return Result.Failure<string>(TenantErros.EmailJaCadastrado);
                    }

                    usuarioId = existente["id"]!.GetValue<string>();
                }
                else
                {
                    usuarioId = IdDoLocation(await GarantirSucessoAsync(resposta, cancellationToken));
                }
            }

            await AtribuirPapelAsync(usuarioId, usuario.Papel, cancellationToken);

            using var membro = await http.PostAsJsonAsync($"organizations/{usuario.OrganizationId}/members", usuarioId, cancellationToken);
            if (membro.StatusCode != HttpStatusCode.Conflict)
            {
                await GarantirSucessoAsync(membro, cancellationToken);
            }

            return Result.Success(usuarioId);
        });

    public Task<IReadOnlyList<UsuarioDoTenant>> ListarUsuariosAsync(string organizationId, CancellationToken cancellationToken) =>
        ExecutarAsync(async () =>
        {
            var membros = await ObterJsonAsync<JsonArray>($"organizations/{organizationId}/members?first=0&max=200", cancellationToken);
            return (IReadOnlyList<UsuarioDoTenant>)membros
                .OfType<JsonObject>()
                .Select(m => new UsuarioDoTenant(
                    m["id"]!.GetValue<string>(),
                    m["email"]?.GetValue<string>() ?? m["username"]!.GetValue<string>(),
                    $"{m["firstName"]?.GetValue<string>()} {m["lastName"]?.GetValue<string>()}".Trim(),
                    m["enabled"]?.GetValue<bool>() ?? false))
                .ToList();
        });

    public Task AtualizarPlanoAsync(string organizationId, Guid tenantId, string planoCodigo, CancellationToken cancellationToken) =>
        ExecutarAsync(async () =>
        {
            var organizacao = await ObterJsonAsync<JsonObject>($"organizations/{organizationId}", cancellationToken);
            DefinirAtributo(organizacao, AtributoPlano, planoCodigo);
            await GarantirSucessoAsync(await http.PutAsJsonAsync($"organizations/{organizationId}", organizacao, cancellationToken), cancellationToken);

            // A claim "plano" vem do atributo do usuário: atualiza cada membro (no máximo 20 por plano).
            foreach (var membro in await ListarUsuariosAsync(organizationId, cancellationToken))
            {
                var usuario = await ObterJsonAsync<JsonObject>($"users/{membro.Id}", cancellationToken);
                DefinirAtributo(usuario, AtributoPlano, planoCodigo);
                await GarantirSucessoAsync(await http.PutAsJsonAsync($"users/{membro.Id}", usuario, cancellationToken), cancellationToken);
            }

            return true;
        });

    public Task RemoverOrganizacaoAsync(Guid tenantId, CancellationToken cancellationToken) =>
        ExecutarAsync(async () =>
        {
            // Compensação completa: usuários criados para o tenant e a organização.
            foreach (var usuario in (await ObterJsonAsync<JsonArray>($"users?q={AtributoTenant}:{tenantId}&briefRepresentation=true", cancellationToken)).OfType<JsonObject>())
            {
                await ExcluirAsync($"users/{usuario["id"]!.GetValue<string>()}", cancellationToken);
            }

            if (await BuscarOrganizacaoAsync(tenantId, cancellationToken) is { } organizationId)
            {
                await ExcluirAsync($"organizations/{organizationId}", cancellationToken);
            }

            return true;
        });

    private async Task<string?> BuscarOrganizacaoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var encontradas = await ObterJsonAsync<JsonArray>($"organizations?q={AtributoTenant}:{tenantId}&briefRepresentation=true", cancellationToken);
        return encontradas.OfType<JsonObject>().Select(o => o["id"]!.GetValue<string>()).FirstOrDefault();
    }

    private async Task<JsonObject?> BuscarUsuarioPorEmailAsync(string email, CancellationToken cancellationToken)
    {
        var encontrados = await ObterJsonAsync<JsonArray>($"users?email={Uri.EscapeDataString(email)}&exact=true&briefRepresentation=false", cancellationToken);
        return encontrados.OfType<JsonObject>().FirstOrDefault();
    }

    private async Task AtribuirPapelAsync(string usuarioId, string papel, CancellationToken cancellationToken)
    {
        var role = await ObterJsonAsync<JsonObject>($"roles/{Uri.EscapeDataString(papel)}", cancellationToken);
        var mapeamento = new[] { new { id = role["id"]!.GetValue<string>(), name = role["name"]!.GetValue<string>() } };
        await GarantirSucessoAsync(await http.PostAsJsonAsync($"users/{usuarioId}/role-mappings/realm", mapeamento, cancellationToken), cancellationToken);
    }

    private async Task<T> ObterJsonAsync<T>(string caminho, CancellationToken cancellationToken)
        where T : JsonNode
    {
        using var resposta = await http.GetAsync(caminho, cancellationToken);
        await GarantirSucessoAsync(resposta, cancellationToken);
        return (T)(await JsonNode.ParseAsync(await resposta.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken))!;
    }

    private async Task ExcluirAsync(string caminho, CancellationToken cancellationToken)
    {
        using var resposta = await http.DeleteAsync(caminho, cancellationToken);
        if (resposta.StatusCode != HttpStatusCode.NotFound)
        {
            await GarantirSucessoAsync(resposta, cancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> GarantirSucessoAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
        if (resposta.IsSuccessStatusCode)
        {
            return resposta;
        }

        var detalhe = await resposta.Content.ReadAsStringAsync(cancellationToken);
        var mensagem = $"Keycloak respondeu {(int)resposta.StatusCode} em {resposta.RequestMessage?.Method} {resposta.RequestMessage?.RequestUri?.AbsolutePath}: {detalhe}";
        throw (int)resposta.StatusCode >= 500
            ? new ProvedorDeIdentidadeIndisponivelException(mensagem)
            : new InvalidOperationException(mensagem);
    }

    private static async Task<T> ExecutarAsync<T>(Func<Task<T>> operacao)
    {
        try
        {
            return await operacao();
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException or TaskCanceledException)
        {
            throw new ProvedorDeIdentidadeIndisponivelException("Provedor de identidade indisponível.", ex);
        }
    }

    private static string IdDoLocation(HttpResponseMessage resposta) =>
        resposta.Headers.Location?.Segments[^1].TrimEnd('/')
        ?? throw new InvalidOperationException("Keycloak não retornou o Location do recurso criado.");

    private static (string Primeiro, string Sobrenome) SepararNome(string nome)
    {
        var partes = nome.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return partes.Length == 2 ? (partes[0], partes[1]) : (partes[0], partes[0]);
    }

    private static string? Atributo(JsonObject recurso, string nome) =>
        recurso["attributes"]?[nome]?.AsArray().FirstOrDefault()?.GetValue<string>();

    private static void DefinirAtributo(JsonObject recurso, string nome, string valor)
    {
        if (recurso["attributes"] is not JsonObject atributos)
        {
            atributos = [];
            recurso["attributes"] = atributos;
        }

        atributos[nome] = new JsonArray(JsonValue.Create(valor));
    }
}

/// <summary>Obtém e renova o token da conta de serviço (client credentials) para a Admin API.</summary>
internal sealed class KeycloakTokenHandler(IHttpClientFactory httpClientFactory, KeycloakOpcoes opcoes, TimeProvider tempo) : DelegatingHandler
{
    public const string ClienteDeToken = "keycloak-token";

    private readonly SemaphoreSlim _trava = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiraEm;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await ObterTokenAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trava.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task<string> ObterTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && tempo.GetUtcNow() < _expiraEm)
        {
            return _token;
        }

        await _trava.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && tempo.GetUtcNow() < _expiraEm)
            {
                return _token;
            }

            using var cliente = httpClientFactory.CreateClient(ClienteDeToken);
            using var resposta = await cliente.PostAsync(
                new Uri($"{opcoes.Url.TrimEnd('/')}/realms/{opcoes.Realm}/protocol/openid-connect/token"),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = opcoes.ClientId,
                    ["client_secret"] = opcoes.ClientSecret,
                }),
                cancellationToken);
            resposta.EnsureSuccessStatusCode();

            using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            _token = json.RootElement.GetProperty("access_token").GetString();
            _expiraEm = tempo.GetUtcNow().AddSeconds(json.RootElement.GetProperty("expires_in").GetInt32() - 30);
            return _token!;
        }
        finally
        {
            _trava.Release();
        }
    }
}

public sealed class KeycloakOpcoes
{
    public string Url { get; set; } = "http://localhost:8081";

    public string Realm { get; set; } = "fluxo-caixa";

    public string ClientId { get; set; } = "fluxo-caixa-tenants";

    public string ClientSecret { get; set; } = string.Empty;
}

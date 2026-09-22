using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.Domain;

namespace Tenants.Domain.Tenants;

/// <summary>CNPJ com dígitos verificadores válidos (RP-01). Armazenado somente com os 14 dígitos.</summary>
public sealed class Cnpj : ValueObject
{
    private static readonly int[] PesosPrimeiroDigito = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] PesosSegundoDigito = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    private Cnpj(string numero) => Numero = numero;

    public string Numero { get; }

    public string Formatado => $"{Numero[..2]}.{Numero[2..5]}.{Numero[5..8]}/{Numero[8..12]}-{Numero[12..]}";

    public static Result<Cnpj> Criar(string? valor)
    {
        var digitos = new string((valor ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var somenteMascara = (valor ?? string.Empty).All(c => char.IsAsciiDigit(c) || c is '.' or '/' or '-' or ' ');

        if (!somenteMascara || digitos.Length != 14 || digitos.Distinct().Count() == 1)
        {
            return TenantErros.CnpjInvalido;
        }

        var primeiro = Digito(digitos, PesosPrimeiroDigito);
        var segundo = Digito(digitos, PesosSegundoDigito);
        if (digitos[12] - '0' != primeiro || digitos[13] - '0' != segundo)
        {
            return TenantErros.CnpjInvalido;
        }

        return new Cnpj(digitos);
    }

    public override string ToString() => Formatado;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Numero;
    }

    private static int Digito(string digitos, int[] pesos)
    {
        var soma = pesos.Select((peso, i) => (digitos[i] - '0') * peso).Sum();
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}

namespace ERP.Domain.CurrentAccounts;

public static class TaxNumber
{
    public static bool IsValidVkn(string value)
    {
        if (!IsDigits(value, 10))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var t = (Digit(value, i) + 9 - i) % 10;
            var r = (t * (1 << (9 - i))) % 9;
            if (t != 0 && r == 0)
            {
                r = 9;
            }

            sum += r;
        }

        return (10 - (sum % 10)) % 10 == Digit(value, 9);
    }

    public static bool IsValidTckn(string value)
    {
        if (!IsDigits(value, 11) || value[0] == '0')
        {
            return false;
        }

        var odd = Digit(value, 0) + Digit(value, 2) + Digit(value, 4) + Digit(value, 6) + Digit(value, 8);
        var even = Digit(value, 1) + Digit(value, 3) + Digit(value, 5) + Digit(value, 7);
        var d10 = ((((odd * 7) - even) % 10) + 10) % 10;
        if (d10 != Digit(value, 9))
        {
            return false;
        }

        var total = 0;
        for (var i = 0; i < 10; i++)
        {
            total += Digit(value, i);
        }

        return total % 10 == Digit(value, 10);
    }

    private static int Digit(string value, int index) => value[index] - '0';

    private static bool IsDigits(string? value, int length)
        => !string.IsNullOrEmpty(value) && value.Length == length && value.All(c => c is >= '0' and <= '9');
}

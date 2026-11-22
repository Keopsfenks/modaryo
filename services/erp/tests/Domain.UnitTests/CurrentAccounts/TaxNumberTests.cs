using NUnit.Framework;
using Shouldly;
using ERP.Domain.CurrentAccounts;

namespace ERP.Domain.UnitTests.CurrentAccounts;

public class TaxNumberTests
{
    [TestCase("1234567890")]
    [TestCase("9876543217")]
    public void Vkn_Valid(string value) => TaxNumber.IsValidVkn(value).ShouldBeTrue();

    [TestCase("1234567891")]
    [TestCase("123456789")]
    [TestCase("12345678901")]
    [TestCase("12345A7890")]
    [TestCase("")]
    [TestCase(null)]
    public void Vkn_Invalid(string? value) => TaxNumber.IsValidVkn(value!).ShouldBeFalse();

    [TestCase("10000000146")]
    public void Tckn_Valid(string value) => TaxNumber.IsValidTckn(value).ShouldBeTrue();

    [TestCase("10000000147")]
    [TestCase("01234567890")]
    [TestCase("1000000014")]
    [TestCase("100000001466")]
    [TestCase("1000000014A")]
    [TestCase("")]
    [TestCase(null)]
    public void Tckn_Invalid(string? value) => TaxNumber.IsValidTckn(value!).ShouldBeFalse();
}

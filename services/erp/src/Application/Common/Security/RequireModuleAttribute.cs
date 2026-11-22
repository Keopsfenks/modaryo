namespace ERP.Application.Common.Security;

/// <summary>
///     İsteğin, firmada ilgili modülün açık olmasını gerektirdiğini belirtir (örn. "pos", "sube").
///     Kapalıysa 403 "module_disabled". Birden fazla verilirse hepsi açık olmalıdır.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequireModuleAttribute(
    string moduleKey
) : Attribute {
    public string ModuleKey { get; } = moduleKey;
}
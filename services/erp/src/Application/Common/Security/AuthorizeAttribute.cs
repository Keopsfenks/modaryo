namespace ERP.Application.Common.Security;

/// <summary>
///     İsteğin oturum açmış kullanıcı gerektirdiğini belirtir. <see cref="Permission" /> verilirse
///     kullanıcının aktif firmada bu izne sahip olması gerekir ("*" her izni karşılar). "a|b" biçiminde birden çok izin verilirse biri yeterlidir;
///     birden çok öznitelik ise birlikte (AND) aranır.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class AuthorizeAttribute : Attribute {
    public string Permission { get; set; } = string.Empty;
}
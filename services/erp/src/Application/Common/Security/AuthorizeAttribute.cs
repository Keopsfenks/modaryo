namespace ERP.Application.Common.Security;

/// <summary>
/// İsteğin oturum açmış kullanıcı gerektirdiğini belirtir. <see cref="Permission"/> verilirse
/// kullanıcının aktif firmada bu izne sahip olması gerekir ("*" her izni karşılar).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class AuthorizeAttribute : Attribute
{
    public string Permission { get; set; } = string.Empty;
}

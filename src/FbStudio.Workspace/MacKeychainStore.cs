using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace FbStudio.Workspace;

/// <summary>
/// macOS: el Llavero del usuario, con la API de Security.framework (contraseñas genéricas de servicio «FbStudio»). El
/// secreto no pasa por la línea de comandos. Si la app cambia (otra versión sin firmar), macOS pregunta si la deja leer.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainStore : ISecretStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ItemNotFound = -25300; // errSecItemNotFound
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("FbStudio");

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    /// <summary>Si se puede usar el Llavero (una búsqueda que no encuentra nada no es un error).</summary>
    public static bool IsAvailable()
    {
        try
        {
            var account = Encoding.UTF8.GetBytes("fbstudio-probe");
            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out _, out var data, out var item);
            Release(data, item);
            return status is 0 or ItemNotFound;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public void Set(string key, string secret)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var password = Encoding.UTF8.GetBytes(secret);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out _, out var data, out var item);
        try
        {
            status = status switch
            {
                0 => SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)password.Length, password),
                ItemNotFound => SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account,
                    (uint)password.Length, password, IntPtr.Zero),
                _ => status,
            };
        }
        finally
        {
            Release(data, item);
        }
        if (status != 0) throw new InvalidOperationException(string.Format(Core.Resources.CoreStrings.KeyringFailed, "OSStatus " + status));
    }

    public string? Get(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out var length, out var data, out var item);
        try
        {
            if (status == ItemNotFound) return null;
            if (status != 0) throw new InvalidOperationException(string.Format(Core.Resources.CoreStrings.KeyringFailed, "OSStatus " + status));
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Release(data, item);
        }
    }

    public void Remove(string key)
    {
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service, (uint)account.Length, account, out _, out var data, out var item);
        try
        {
            if (status == 0) SecKeychainItemDelete(item);
        }
        finally
        {
            Release(data, item);
        }
    }

    private static void Release(IntPtr data, IntPtr item)
    {
        if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
        if (item != IntPtr.Zero) CFRelease(item);
    }
}

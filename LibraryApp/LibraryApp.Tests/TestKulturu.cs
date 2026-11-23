using System.Globalization;
using System.Runtime.CompilerServices;

namespace LibraryApp.Tests;

/// <summary>
/// Testler makinenin bölge ayarından bağımsız çalışır: Türkçe kültürde "I" harfinin küçük harfi "ı" olduğu için
/// bellek içi sağlayıcının büyük/küçük harf duyarsız eşleşmesi geliştirici makinesinde farklı sonuç verebilir.
/// (Üretimde eşleşmeyi SQL Server sütun harmanlaması yapar.)
/// </summary>
internal static class TestKulturu
{
    [ModuleInitializer]
    internal static void Ayarla()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}

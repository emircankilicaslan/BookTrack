using System.ComponentModel.DataAnnotations;

namespace LibraryApp.Models
{
    /// <summary>T.C. kimlik numarasının resmi sağlama algoritmasını denetler.</summary>
    public static class TcknDogrulayici
    {
        public static bool Gecerli(string? tckn)
        {
            if (tckn is not { Length: 11 } || !tckn.All(c => c is >= '0' and <= '9')) return false;
            var d = tckn.Select(c => c - '0').ToArray();
            if (d[0] == 0) return false;

            var tekler = d[0] + d[2] + d[4] + d[6] + d[8];
            var ciftler = d[1] + d[3] + d[5] + d[7];
            var onuncu = (((tekler * 7 - ciftler) % 10) + 10) % 10;
            if (d[9] != onuncu) return false;

            return d.Take(10).Sum() % 10 == d[10];
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class TcknAttribute : ValidationAttribute
    {
        public TcknAttribute() : base("Geçerli bir T.C. kimlik numarası giriniz.") { }

        // Boş/null değer Required'a bırakılır.
        public override bool IsValid(object? value) =>
            value is null or "" || TcknDogrulayici.Gecerli(value as string);
    }
}

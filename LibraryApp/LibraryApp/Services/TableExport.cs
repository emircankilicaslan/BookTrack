using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace LibraryApp.Services
{
    /// <summary>Tablo verisini CSV veya Excel (.xlsx) olarak üretir; ek paket gerektirmez.</summary>
    public static class TableExport
    {
        public const string CsvType = "text/csv; charset=utf-8";
        public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>Hücre değerini metne çevirir: tarihler gün.ay.yıl, ondalıklar Türkçe virgülle.</summary>
        public static string Format(object? value) => value switch
        {
            null => "",
            DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : d.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            decimal m => m.ToString("0.00", CultureInfo.GetCultureInfo("tr-TR")),
            bool b => b ? "Evet" : "Hayır",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        /// <summary>
        /// CSV: UTF-8 BOM (Excel Türkçe karakterleri doğru açar), ayraç noktalı virgül. Formül enjeksiyonuna karşı
        /// =, +, -, @ ile başlayan metinlerin önüne kesme işareti konur.
        /// </summary>
        public static byte[] Csv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(';', headers.Select(Quote))).Append("\r\n");
            foreach (var row in rows)
                sb.Append(string.Join(';', row.Select(v => Quote(Safe(v))))).Append("\r\n");
            return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        private static string Safe(object? value)
        {
            var text = Format(value);
            if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0])) return "'" + text;
            return text;
        }

        private static string Quote(string text) =>
            text.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

        /// <summary>Tek sayfalı, başlığı kalın .xlsx. Sayılar sayı, diğerleri metin olarak yazılır.</summary>
        public static byte[] Xlsx(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                Entry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");
                Entry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Entry(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    $"<sheets><sheet name=\"{Esc(SheetName(sheetName))}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Entry(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");
                Entry(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                    "</styleSheet>");

                var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
                var r = 1;
                sheet.Append($"<row r=\"{r}\">");
                for (var c = 0; c < headers.Count; c++)
                    sheet.Append($"<c r=\"{Ref(c, r)}\" t=\"inlineStr\" s=\"1\"><is><t>{Esc(headers[c])}</t></is></c>");
                sheet.Append("</row>");
                foreach (var row in rows)
                {
                    r++;
                    sheet.Append($"<row r=\"{r}\">");
                    for (var c = 0; c < row.Count; c++)
                    {
                        var v = row[c];
                        if (v is null) continue;
                        if (v is int or long or short or byte)
                            sheet.Append($"<c r=\"{Ref(c, r)}\"><v>{Convert.ToString(v, CultureInfo.InvariantCulture)}</v></c>");
                        else if (v is decimal m)
                            sheet.Append($"<c r=\"{Ref(c, r)}\"><v>{m.ToString(CultureInfo.InvariantCulture)}</v></c>");
                        else
                            sheet.Append($"<c r=\"{Ref(c, r)}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(Format(v))}</t></is></c>");
                    }
                    sheet.Append("</row>");
                }
                sheet.Append("</sheetData></worksheet>");
                Entry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            }
            return stream.ToArray();
        }

        private static void Entry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        /// <summary>0 tabanlı sütun ve 1 tabanlı satırdan A1 başvurusu (A..Z, AA..).</summary>
        public static string Ref(int column, int row)
        {
            var name = "";
            for (var n = column; n >= 0; n = n / 26 - 1) name = (char)('A' + n % 26) + name;
            return name + row.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>XML'de geçersiz denetim karakterlerini atar ve kaçışlar.</summary>
        private static string Esc(string text) =>
            SecurityElement.Escape(new string(text.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray())) ?? "";

        /// <summary>Excel sayfa adı: en çok 31 karakter, []:*?/\ yasak.</summary>
        private static string SheetName(string name)
        {
            var clean = new string(name.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
            if (clean.Length == 0) clean = "Sayfa1";
            return clean.Length <= 31 ? clean : clean[..31];
        }
    }
}

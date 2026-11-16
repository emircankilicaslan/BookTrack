using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryApp.Migrations
{
    /// <inheritdoc />
    public partial class VeriButunlugu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Mevcut veri kontrolleri: otomatik karar verilemeyen durumlarda açık hata ile durur
            //    (migration tek işlemdir, hiçbir değişiklik uygulanmaz).
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM Books GROUP BY ISBN HAVING COUNT(*) > 1)
    THROW 50001, 'Tekrar eden ISBN kayitlari var. Once su sorgu ile bulup birlestirin veya duzeltin: SELECT ISBN, COUNT(*) FROM Books GROUP BY ISBN HAVING COUNT(*) > 1', 1;");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM Books WHERE LEN(Title) > 300 OR LEN(ISBN) > 20)
    THROW 50002, 'Basligi 300 veya ISBN''i 20 karakterden uzun kitaplar var. Once kisaltin: SELECT Id, LEN(Title), LEN(ISBN) FROM Books WHERE LEN(Title) > 300 OR LEN(ISBN) > 20', 1;");

            // 2) Kitabi silinmis (yetim) odunc kayitlari yabanci anahtar eklenebilsin diye ayrilir,
            //    ancak silinmez: CheckOuts_Yetim tablosuna tasinir.
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.CheckOuts_Yetim') IS NULL
    EXEC('SELECT * INTO dbo.CheckOuts_Yetim FROM dbo.CheckOuts WHERE 1 = 0');
EXEC('SET IDENTITY_INSERT dbo.CheckOuts_Yetim ON;
      INSERT INTO dbo.CheckOuts_Yetim (Id, BookId, Name, PhoneNumber, TCKN, CheckOutDate, DueDate, ActualReturnDate)
      SELECT c.Id, c.BookId, c.Name, c.PhoneNumber, c.TCKN, c.CheckOutDate, c.DueDate, c.ActualReturnDate
      FROM dbo.CheckOuts c WHERE NOT EXISTS (SELECT 1 FROM dbo.Books b WHERE b.Id = c.BookId);
      SET IDENTITY_INSERT dbo.CheckOuts_Yetim OFF;
      DELETE c FROM dbo.CheckOuts c WHERE NOT EXISTS (SELECT 1 FROM dbo.Books b WHERE b.Id = c.BookId);');");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Books",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ISBN",
                table: "Books",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_CheckOuts_BookId",
                table: "CheckOuts",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_ISBN",
                table: "Books",
                column: "ISBN",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CheckOuts_Books_BookId",
                table: "CheckOuts",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CheckOuts_Books_BookId",
                table: "CheckOuts");

            migrationBuilder.DropIndex(
                name: "IX_CheckOuts_BookId",
                table: "CheckOuts");

            migrationBuilder.DropIndex(
                name: "IX_Books_ISBN",
                table: "Books");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Books",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "ISBN",
                table: "Books",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);
        }
    }
}

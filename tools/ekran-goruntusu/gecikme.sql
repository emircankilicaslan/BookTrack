USE LibraryDb;
GO
-- Odunc taki ilk uc kitap gecikmis olsun (ekran goruntulerinde Geciken gosterimi icin)
UPDATE Books SET DueDate = DATEADD(day, -14, DueDate), CheckOutDate = DATEADD(day, -30, CheckOutDate)
WHERE Id IN (SELECT TOP 3 Id FROM Books WHERE IsCheckedOut = 1 ORDER BY Id);
UPDATE CheckOuts SET DueDate = DATEADD(day, -14, DueDate), CheckOutDate = DATEADD(day, -30, CheckOutDate)
WHERE ActualReturnDate IS NULL AND BookId IN (SELECT TOP 3 Id FROM Books WHERE IsCheckedOut = 1 ORDER BY Id);
GO
-- Iade edilmis gecmis kayitlar son alti aya dagilsin; bir kismi gecikmeli iade ve ceza icersin
UPDATE CheckOuts SET CheckOutDate = DATEADD(month, -(Id % 6), CheckOutDate), DueDate = DATEADD(month, -(Id % 6), DueDate),
  ActualReturnDate = DATEADD(month, -(Id % 6), ActualReturnDate)
WHERE ActualReturnDate IS NOT NULL;
UPDATE CheckOuts SET LateDays = 2 + (Id % 3), FineAmount = (2 + (Id % 3)) * 5 WHERE ActualReturnDate IS NOT NULL AND Id % 4 = 0;
GO
SELECT COUNT(*) AS GecikenKitap FROM Books WHERE IsCheckedOut = 1 AND DueDate < CAST(GETDATE() AS date);
GO

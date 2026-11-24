-- Migration for an existing ReactKL archive. Run as database administrator.
IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> N'KartyLeczenia'
    THROW 50000, N'Wybierz bazę KartyLeczenia.', 1;

IF OBJECT_ID(N'dbo.catalog_variants', N'U') IS NULL
    THROW 50001, N'W bazie brakuje tabeli dbo.catalog_variants.', 1;

IF DATABASE_PRINCIPAL_ID(N'KartyLeczeniaOperator') IS NULL
    THROW 50002, N'W bazie brakuje roli KartyLeczeniaOperator.', 1;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.catalog_variant_withdrawal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.catalog_variant_withdrawal (
        variant_id int NOT NULL REFERENCES dbo.catalog_variants(id) ON DELETE CASCADE,
        commodity varchar(12) NOT NULL CHECK (commodity IN ('tissues','milk','eggs','food')),
        short_text nvarchar(1000) NOT NULL,
        status varchar(20) NOT NULL CHECK (status IN
            ('period','not_applicable','not_reported','not_authorized','condition')),
        duration int NULL CHECK (duration >= 0),
        unit varchar(5) NULL CHECK (unit IN ('hours','days')),
        condition_text nvarchar(1000) NULL,
        source_text nvarchar(max) NOT NULL,
        CONSTRAINT PK_catalog_variant_withdrawal PRIMARY KEY (variant_id, commodity)
    );
END;

EXEC(N'GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.catalog_variant_withdrawal TO KartyLeczeniaOperator;');

COMMIT TRANSACTION;
PRINT N'Tabela krótkich karencji jest gotowa.';

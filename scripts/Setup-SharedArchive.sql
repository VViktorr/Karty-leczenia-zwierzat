-- Run as an administrator in the separately created KartyLeczenia database.
IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> N'KartyLeczenia'
    THROW 50000, N'Wybierz osobną bazę KartyLeczenia przed uruchomieniem skryptu.', 1;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.treatment_cards', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.treatment_cards (
        id uniqueidentifier NOT NULL PRIMARY KEY,
        archive_order bigint IDENTITY NOT NULL,
        source_scope nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
        payer_id int NULL,
        client nvarchar(max) NOT NULL,
        document nvarchar(max) NOT NULL,
        treatment_date date NOT NULL,
        search_text nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
        card_json nvarchar(max) NOT NULL,
        print_json nvarchar(max) NOT NULL
    );
    CREATE INDEX IX_treatment_cards_date ON dbo.treatment_cards (treatment_date DESC, archive_order DESC);
END;

IF OBJECT_ID(N'dbo.animal_descriptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.animal_descriptions (
        description_key binary(32) NOT NULL PRIMARY KEY,
        source_scope nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
        payer_id int NOT NULL,
        description nvarchar(max) NOT NULL,
        last_used bigint NOT NULL
    );
    CREATE INDEX IX_animal_descriptions_payer ON dbo.animal_descriptions (payer_id, last_used DESC);
END;

IF OBJECT_ID(N'dbo.clinic_settings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.clinic_settings (
        id tinyint NOT NULL PRIMARY KEY CHECK (id = 1),
        name nvarchar(max) NOT NULL,
        address nvarchar(max) NOT NULL,
        doctor nvarchar(max) NOT NULL
    );
END;

IF DATABASE_PRINCIPAL_ID(N'KartyLeczeniaOperator') IS NULL
    CREATE ROLE KartyLeczeniaOperator AUTHORIZATION dbo;

GRANT SELECT, INSERT, UPDATE ON dbo.treatment_cards TO KartyLeczeniaOperator;
GRANT SELECT, INSERT, UPDATE ON dbo.animal_descriptions TO KartyLeczeniaOperator;
GRANT SELECT, INSERT, UPDATE ON dbo.clinic_settings TO KartyLeczeniaOperator;

COMMIT TRANSACTION;

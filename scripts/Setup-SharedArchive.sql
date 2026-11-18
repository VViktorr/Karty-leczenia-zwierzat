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

IF COL_LENGTH(N'dbo.treatment_cards', N'deleted_at_utc') IS NULL
    ALTER TABLE dbo.treatment_cards ADD deleted_at_utc datetime2(0) NULL;

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

IF COL_LENGTH(N'dbo.clinic_settings', N'use_doctor_stamp') IS NULL
    ALTER TABLE dbo.clinic_settings ADD use_doctor_stamp bit NOT NULL
        CONSTRAINT DF_clinic_settings_use_doctor_stamp DEFAULT (0);

IF COL_LENGTH(N'dbo.clinic_settings', N'doctor_stamp_png') IS NULL
    ALTER TABLE dbo.clinic_settings ADD doctor_stamp_png varbinary(max) NULL;

IF COL_LENGTH(N'dbo.clinic_settings', N'use_clinic_stamp') IS NULL
    ALTER TABLE dbo.clinic_settings ADD use_clinic_stamp bit NOT NULL
        CONSTRAINT DF_clinic_settings_use_clinic_stamp DEFAULT (0);

IF COL_LENGTH(N'dbo.clinic_settings', N'clinic_stamp_png') IS NULL
    ALTER TABLE dbo.clinic_settings ADD clinic_stamp_png varbinary(max) NULL;

IF COL_LENGTH(N'dbo.clinic_settings', N'phone') IS NULL
    ALTER TABLE dbo.clinic_settings ADD phone nvarchar(max) NOT NULL
        CONSTRAINT DF_clinic_settings_phone DEFAULT (N'');

IF OBJECT_ID(N'dbo.catalog_products', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.catalog_products (
        symbol nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
        name nvarchar(max) NOT NULL, description nvarchar(max) NULL, rpl_id nvarchar(max) NULL,
        upd_id nvarchar(max) NULL, status nvarchar(max) NULL, decision nvarchar(max) NULL,
        revision rowversion NOT NULL
    );
    CREATE TABLE dbo.catalog_documents (
        path nvarchar(450) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
        pdf varbinary(max) NOT NULL
    );
    CREATE TABLE dbo.catalog_variants (
        id int IDENTITY NOT NULL PRIMARY KEY,
        symbol nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL REFERENCES dbo.catalog_products(symbol),
        name nvarchar(max) NULL, rpl_id nvarchar(max) NULL, upd_id nvarchar(max) NULL,
        species nvarchar(max) NULL, indication nvarchar(max) NULL, route nvarchar(max) NULL, dose nvarchar(max) NULL,
        edible_tissues nvarchar(max) NULL, milk nvarchar(max) NULL, restrictions nvarchar(max) NULL,
        source nvarchar(max) NULL, verified_at nvarchar(max) NULL, card_text nvarchar(max) NULL, scope nvarchar(max) NULL,
        pdf_path nvarchar(450) COLLATE Latin1_General_100_BIN2 NULL REFERENCES dbo.catalog_documents(path),
        withdrawal_short nvarchar(max) NULL
    );
    CREATE INDEX IX_catalog_variants_symbol ON dbo.catalog_variants(symbol);
    CREATE TABLE dbo.catalog_price_withdrawal (
        id int NOT NULL PRIMARY KEY REFERENCES dbo.catalog_variants(id) ON DELETE CASCADE,
        compact_text nvarchar(max) NOT NULL
    );
    CREATE TABLE dbo.catalog_upd_search (
        id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, name nvarchar(max) NOT NULL
    );
    CREATE TABLE dbo.catalog_import (id tinyint NOT NULL PRIMARY KEY CHECK (id = 1));
END;

IF OBJECT_ID(N'dbo.treatment_choices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.treatment_choices (
        choice_key binary(32) NOT NULL PRIMARY KEY,
        source_scope nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
        symbol nvarchar(max) NOT NULL, product nvarchar(max) NOT NULL,
        species nvarchar(max) NOT NULL, route nvarchar(max) NOT NULL
    );
END;

IF DATABASE_PRINCIPAL_ID(N'KartyLeczeniaOperator') IS NULL
    CREATE ROLE KartyLeczeniaOperator AUTHORIZATION dbo;

GRANT SELECT, INSERT, UPDATE ON dbo.treatment_cards TO KartyLeczeniaOperator;
REVOKE DELETE ON dbo.treatment_cards FROM KartyLeczeniaOperator;
GRANT SELECT, INSERT, UPDATE ON dbo.animal_descriptions TO KartyLeczeniaOperator;
GRANT SELECT, INSERT, UPDATE ON dbo.clinic_settings TO KartyLeczeniaOperator;

GRANT SELECT, INSERT, UPDATE ON dbo.catalog_products TO KartyLeczeniaOperator;
GRANT SELECT, INSERT ON dbo.catalog_documents TO KartyLeczeniaOperator;
GRANT SELECT, INSERT, UPDATE ON dbo.catalog_variants TO KartyLeczeniaOperator;
GRANT SELECT, INSERT, UPDATE ON dbo.catalog_price_withdrawal TO KartyLeczeniaOperator;
GRANT SELECT ON dbo.catalog_import TO KartyLeczeniaOperator;
GRANT SELECT, INSERT, UPDATE ON dbo.treatment_choices TO KartyLeczeniaOperator;

COMMIT TRANSACTION;

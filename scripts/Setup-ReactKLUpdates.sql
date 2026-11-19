-- Separate update channel for ReactKL. Does not modify the legacy
-- dbo.program_update table and never publishes the WinForms installer.
-- Run with sysadmin/dbo permissions on the PROGRAM database KartyLeczenia.
USE [KartyLeczenia];
GO
IF OBJECT_ID(N'dbo.react_program_update',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.react_program_update
    (
        id tinyint NOT NULL CONSTRAINT PK_react_program_update PRIMARY KEY
            CONSTRAINT CK_react_program_update_id CHECK(id=1),
        release_id uniqueidentifier NOT NULL,
        version nvarchar(32) NOT NULL,
        sha256 binary(32) NOT NULL,
        installer varbinary(max) NOT NULL,
        published_at_utc datetime2(0) NOT NULL
            CONSTRAINT DF_react_program_update_pub DEFAULT SYSUTCDATETIME()
    );
END;
GO
IF DATABASE_PRINCIPAL_ID(N'KartyLeczeniaOperator') IS NOT NULL
BEGIN
    GRANT SELECT ON dbo.react_program_update TO KartyLeczeniaOperator;
END;
GO

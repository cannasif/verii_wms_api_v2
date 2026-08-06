IF OBJECT_ID(N'[dbo].[RII_PC_ATTACHMENT]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RII_PC_ATTACHMENT] (
        [Id]            bigint         NOT NULL IDENTITY(1,1),
        [BranchCode]    nvarchar(10)   NOT NULL CONSTRAINT [DF_RII_PC_ATTACHMENT_BranchCode] DEFAULT (N'0'),
        [CreatedDate]   datetime2      NULL,
        [UpdatedDate]   datetime2      NULL,
        [DeletedDate]   datetime2      NULL,
        [IsDeleted]     bit            NOT NULL CONSTRAINT [DF_RII_PC_ATTACHMENT_IsDeleted] DEFAULT (CONVERT([bit],(0))),
        [CreatedBy]     bigint         NULL,
        [UpdatedBy]     bigint         NULL,
        [DeletedBy]     bigint         NULL,
        [OwnerType]     nvarchar(30)   NOT NULL,
        [OwnerId]       bigint         NOT NULL,
        [FileName]      nvarchar(260)  NOT NULL,
        [ContentType]   nvarchar(100)  NOT NULL,
        [StoragePath]   nvarchar(500)  NOT NULL,
        [Caption]       nvarchar(500)  NULL,
        [FileSize]      bigint         NOT NULL,
        CONSTRAINT [PK_RII_PC_ATTACHMENT] PRIMARY KEY ([Id])
    );

    CREATE INDEX [IX_RII_PC_ATTACHMENT_OwnerType_OwnerId_CreatedDate]
        ON [dbo].[RII_PC_ATTACHMENT] ([OwnerType], [OwnerId], [CreatedDate]);
END
GO

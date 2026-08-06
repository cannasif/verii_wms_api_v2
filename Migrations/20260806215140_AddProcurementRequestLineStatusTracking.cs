using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementRequestLineStatusTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'dbo.RII_PC_REQUEST_LINE', N'Status') IS NULL
                BEGIN
                    EXEC(N'ALTER TABLE dbo.RII_PC_REQUEST_LINE
                        ADD [Status] nvarchar(30) NOT NULL
                            CONSTRAINT DF_RII_PC_REQUEST_LINE_Status DEFAULT (N''Draft'')');

                    EXEC(N'UPDATE L
                        SET L.[Status] = CASE R.[Status]
                            WHEN N''Draft'' THEN N''Draft''
                            WHEN N''PendingApproval'' THEN N''PendingApproval''
                            WHEN N''Approved'' THEN N''Approved''
                            WHEN N''Rejected'' THEN N''Rejected''
                            WHEN N''Cancelled'' THEN N''Cancelled''
                            WHEN N''Converted'' THEN N''Approved''
                            WHEN N''PartiallyConverted'' THEN N''Approved''
                            WHEN N''PartiallyApproved'' THEN N''PendingApproval''
                            ELSE N''Draft''
                        END
                        FROM dbo.RII_PC_REQUEST_LINE L
                        INNER JOIN dbo.RII_PC_REQUEST R
                            ON R.Id = L.ProcurementRequestId
                        WHERE L.IsDeleted = 0 AND R.IsDeleted = 0');
                END;

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.RII_PC_REQUEST_LINE')
                      AND name = N'IX_RII_PC_REQUEST_LINE_RequestId_Status'
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.RII_PC_REQUEST_LINE')
                      AND name = N'IX_RII_PC_REQUEST_LINE_ProcurementRequestId_Status'
                )
                BEGIN
                    EXEC sp_rename
                        N'dbo.RII_PC_REQUEST_LINE.IX_RII_PC_REQUEST_LINE_RequestId_Status',
                        N'IX_RII_PC_REQUEST_LINE_ProcurementRequestId_Status',
                        N'INDEX';
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.RII_PC_REQUEST_LINE')
                      AND name = N'IX_RII_PC_REQUEST_LINE_ProcurementRequestId_Status'
                )
                BEGIN
                    EXEC(N'CREATE INDEX IX_RII_PC_REQUEST_LINE_ProcurementRequestId_Status
                        ON dbo.RII_PC_REQUEST_LINE (ProcurementRequestId, [Status])');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.RII_PC_REQUEST_LINE')
                      AND name = N'IX_RII_PC_REQUEST_LINE_ProcurementRequestId_Status'
                )
                    DROP INDEX IX_RII_PC_REQUEST_LINE_ProcurementRequestId_Status
                        ON dbo.RII_PC_REQUEST_LINE;

                IF COL_LENGTH(N'dbo.RII_PC_REQUEST_LINE', N'Status') IS NOT NULL
                BEGIN
                    DECLARE @DefaultConstraint sysname;
                    SELECT @DefaultConstraint = dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.default_object_id = dc.object_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.RII_PC_REQUEST_LINE')
                      AND c.name = N'Status';

                    IF @DefaultConstraint IS NOT NULL
                        EXEC(N'ALTER TABLE dbo.RII_PC_REQUEST_LINE DROP CONSTRAINT [' + @DefaultConstraint + N']');

                    ALTER TABLE dbo.RII_PC_REQUEST_LINE DROP COLUMN [Status];
                END;
                """);
        }
    }
}
